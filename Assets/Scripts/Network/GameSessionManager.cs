using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Bridges the WebSocket server with the game logic.
/// Handles incoming messages from player apps, dispatches commands via TankEventBus,
/// and broadcasts game state updates back to connected clients.
/// </summary>
public class GameSessionManager : MonoBehaviour
{
    public static GameSessionManager Instance { get; private set; }

    [Header("State Broadcast")]
    [Tooltip("How often to broadcast GameState to all clients (seconds).")]
    public float broadcastInterval = 0.2f;

    // maps clientId → assigned playerNumber
    readonly Dictionary<string, int> clientPlayerMap = new Dictionary<string, int>();
    // maps playerNumber → clientId
    readonly Dictionary<int, string> playerClientMap = new Dictionary<int, string>();

    // maps playerNumber → the script currently being executed
    readonly Dictionary<int, Coroutine> runningScripts = new Dictionary<int, Coroutine>();

    // command-done flags per player
    readonly Dictionary<int, bool> commandDoneFlags = new Dictionary<int, bool>();

    WebSocketServer server;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnEnable()
    {
        // Game events → broadcast to clients
        TankEventBus.OnRoundStarted += HandleRoundStarted;
        TankEventBus.OnRoundEnded += HandleRoundEnded;
        TankEventBus.OnGameOver += HandleGameOver;
        TankEventBus.OnTankDestroyed += HandleTankDestroyed;
        TankEventBus.OnReactiveInterval += HandleReactiveInterval;
        TankEventBus.OnCommandDone += HandleCommandDone;
    }

    void OnDisable()
    {
        TankEventBus.OnRoundStarted -= HandleRoundStarted;
        TankEventBus.OnRoundEnded -= HandleRoundEnded;
        TankEventBus.OnGameOver -= HandleGameOver;
        TankEventBus.OnTankDestroyed -= HandleTankDestroyed;
        TankEventBus.OnReactiveInterval -= HandleReactiveInterval;
        TankEventBus.OnCommandDone -= HandleCommandDone;
    }

    void Start()
    {
        server = WebSocketServer.Instance;
        if (server == null)
        {
            Debug.LogError("[GameSessionManager] No WebSocketServer found. Add one to the scene.");
            return;
        }

        server.OnMessageReceived += HandleMessage;
        server.OnClientConnected += HandleClientConnected;
        server.OnClientDisconnected += HandleClientDisconnected;

        server.StartServer();
        StartCoroutine(BroadcastGameStateLoop());
    }

    void OnDestroy()
    {
        if (server != null)
        {
            server.OnMessageReceived -= HandleMessage;
            server.OnClientConnected -= HandleClientConnected;
            server.OnClientDisconnected -= HandleClientDisconnected;
        }
        if (Instance == this) Instance = null;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Incoming messages from player apps
    // ═══════════════════════════════════════════════════════════════

    void HandleMessage(string clientId, string rawJson)
    {
        NetworkEnvelope envelope;
        try
        {
            envelope = NetworkMessageHelper.Unwrap(rawJson);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[GameSessionManager] Bad message from {clientId}: {ex.Message}");
            return;
        }

        switch (envelope.type)
        {
            case "JoinRequest":
                HandleJoinRequest(clientId, envelope.payload);
                break;
            case "UpdateSettings":
                HandleUpdateSettings(clientId, envelope.payload);
                break;
            case "SubmitScript":
                HandleSubmitScript(clientId, envelope.payload);
                break;
            case "Heartbeat":
                // no-op, keeps connection alive
                break;
            default:
                Debug.LogWarning($"[GameSessionManager] Unknown message type '{envelope.type}' from {clientId}");
                break;
        }
    }

    void HandleJoinRequest(string clientId, string payload)
    {
        var req = NetworkMessageHelper.ParsePayload<JoinRequest>(payload);

        // validate room code
        if (!string.Equals(req.roomCode, server.RoomCode, StringComparison.OrdinalIgnoreCase))
        {
            var fail = NetworkMessageHelper.Wrap("JoinResponse", new JoinResponse
            {
                success = false,
                playerNumber = -1,
                error = "Invalid room code."
            });
            server.SendToClient(clientId, fail);
            return;
        }

        // check max players
        if (server.ConnectedPlayerCount > server.maxPlayers)
        {
            var fail = NetworkMessageHelper.Wrap("JoinResponse", new JoinResponse
            {
                success = false,
                playerNumber = -1,
                error = "Game is full."
            });
            server.SendToClient(clientId, fail);
            return;
        }

        // assign player number (find first available)
        int playerNumber = AssignPlayerNumber();
        if (playerNumber < 0)
        {
            var fail = NetworkMessageHelper.Wrap("JoinResponse", new JoinResponse
            {
                success = false,
                playerNumber = -1,
                error = "No available player slots."
            });
            server.SendToClient(clientId, fail);
            return;
        }

        var client = server.GetClient(clientId);
        if (client != null)
        {
            client.PlayerNumber = playerNumber;
            client.PlayerName = string.IsNullOrEmpty(req.playerName) ? $"Player {playerNumber}" : req.playerName;
        }

        clientPlayerMap[clientId] = playerNumber;
        playerClientMap[playerNumber] = clientId;

        Debug.Log($"[GameSessionManager] {client.PlayerName} joined as Player {playerNumber}");

        // send success
        var resp = NetworkMessageHelper.Wrap("JoinResponse", new JoinResponse
        {
            success = true,
            playerNumber = playerNumber,
            error = ""
        });
        server.SendToClient(clientId, resp);

        // broadcast updated player list to all clients
        BroadcastPlayerList();
    }

    void HandleUpdateSettings(string clientId, string payload)
    {
        if (!clientPlayerMap.TryGetValue(clientId, out int playerNumber)) return;

        var req = NetworkMessageHelper.ParsePayload<UpdateSettingsRequest>(payload);
        var color = new Color(req.colorR, req.colorG, req.colorB);

        var client = server.GetClient(clientId);
        if (client != null) client.TankColor = color;

        // apply color to the PlayerStart for this player
        foreach (var ps in FindObjectsByType<PlayerStart>(FindObjectsSortMode.None))
        {
            if (ps.playerNumber == playerNumber)
            {
                ps.tankColor = color;
                break;
            }
        }

        Debug.Log($"[GameSessionManager] Player {playerNumber} updated settings (color: {color})");
        BroadcastPlayerList();
    }

    void HandleSubmitScript(string clientId, string payload)
    {
        if (!clientPlayerMap.TryGetValue(clientId, out int playerNumber)) return;

        var req = NetworkMessageHelper.ParsePayload<SubmitScriptRequest>(payload);

        if (string.IsNullOrWhiteSpace(req.script))
        {
            SendCommandLog(clientId, "Script is empty.", "warning");
            return;
        }

        // parse
        List<TankNode> nodes;
        try
        {
            nodes = TankScriptParser.Parse(req.script);
        }
        catch (FormatException ex)
        {
            // try to extract line number from message
            server.SendToClient(clientId, NetworkMessageHelper.Wrap("ScriptError", new ScriptErrorMessage
            {
                message = ex.Message,
                line = -1
            }));
            SendCommandLog(clientId, "Parse error: " + ex.Message, "error");
            return;
        }

        if (nodes.Count == 0)
        {
            SendCommandLog(clientId, "Script produced no commands.", "warning");
            return;
        }

        // notify GameManager of submission
        TankEventBus.PlayerSubmitted(playerNumber);

        // stop any previously running script for this player
        if (runningScripts.TryGetValue(playerNumber, out var oldCo) && oldCo != null)
            StopCoroutine(oldCo);

        // determine execution mode
        GameMode mode = GameManager.Instance != null ? GameManager.Instance.gameMode : GameMode.Dev;

        SendCommandLog(clientId, "▶ Script submitted.", "success");

        Coroutine co = StartCoroutine(ExecuteScriptForPlayer(playerNumber, clientId, nodes, mode));
        runningScripts[playerNumber] = co;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Script execution (mirrors TankConsoleUI execution flow)
    // ═══════════════════════════════════════════════════════════════

    IEnumerator ExecuteScriptForPlayer(int playerNumber, string clientId, List<TankNode> nodes, GameMode mode)
    {
        yield return new WaitForSeconds(0.1f);

        bool loop = (mode == GameMode.Passive || mode == GameMode.Reactive);
        bool roundOver = false;

        // subscribe to round end to stop looping
        Action<int, int> onRoundEnd = (rn, winner) => roundOver = true;
        TankEventBus.OnRoundEnded += onRoundEnd;

        try
        {
            do
            {
                yield return ExecuteBlock(playerNumber, clientId, nodes);

                if (loop && !roundOver)
                    SendCommandLog(clientId, "↻ Looping...", "info");
            }
            while (loop && !roundOver);

            SendCommandLog(clientId, "✓ Done.", "success");
        }
        finally
        {
            TankEventBus.OnRoundEnded -= onRoundEnd;
            runningScripts.Remove(playerNumber);
        }
    }

    IEnumerator ExecuteBlock(int playerNumber, string clientId, List<TankNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node is MoveNode move)
            {
                SendCommandLog(clientId, $"  MOVE {move.distance}", "info");
                yield return RunCommand(playerNumber, () => TankEventBus.MoveForward(playerNumber, move.distance));
            }
            else if (node is TurnNode turn)
            {
                string extra = turn.arcRadius > 0 ? $" (arc {turn.arcRadius})" : "";
                SendCommandLog(clientId, $"  TURN {turn.degrees}{extra}", "info");
                yield return RunCommand(playerNumber, () => TankEventBus.Turn(playerNumber, turn.degrees, turn.arcRadius));
            }
            else if (node is BoostNode)
            {
                SendCommandLog(clientId, "  BOOST", "info");
                yield return RunCommand(playerNumber, () => TankEventBus.Boost(playerNumber));
            }
            else if (node is FireNode)
            {
                SendCommandLog(clientId, "  FIRE", "info");
                yield return RunCommand(playerNumber, () => TankEventBus.Fire(playerNumber));
            }
            else if (node is WaitNode wait)
            {
                SendCommandLog(clientId, $"  WAIT {wait.seconds}s", "info");
                yield return new WaitForSeconds(wait.seconds);
            }
            else if (node is FindNode)
            {
                SendCommandLog(clientId, "  FIND", "info");
                yield return RunCommand(playerNumber, () => TankEventBus.Find(playerNumber));
            }
            else if (node is ForNode forNode)
            {
                SendCommandLog(clientId, $"  FOR {forNode.count}", "info");
                for (int i = 0; i < forNode.count; i++)
                    yield return ExecuteBlock(playerNumber, clientId, forNode.body);
            }
            else if (node is IfNode ifNode)
            {
                bool result = EvaluateCondition(playerNumber, ifNode.condition);
                SendCommandLog(clientId, $"  IF {ifNode.condition} → {result}", "info");
                if (result)
                    yield return ExecuteBlock(playerNumber, clientId, ifNode.body);
                else if (ifNode.elseBody.Count > 0)
                    yield return ExecuteBlock(playerNumber, clientId, ifNode.elseBody);
            }

            // small delay between commands
            yield return new WaitForSeconds(0.1f);
        }
    }

    IEnumerator RunCommand(int playerNumber, Action dispatch)
    {
        commandDoneFlags[playerNumber] = false;
        dispatch();

        while (!commandDoneFlags.ContainsKey(playerNumber) || !commandDoneFlags[playerNumber])
            yield return null;
    }

    void HandleCommandDone(int playerNumber)
    {
        commandDoneFlags[playerNumber] = true;
    }

    bool EvaluateCondition(int playerNumber, TankCondition condition)
    {
        foreach (var listener in FindObjectsByType<InputListener>(FindObjectsSortMode.None))
        {
            if (listener.playerNumber == playerNumber)
                return listener.EvaluateCondition(condition);
        }
        return false;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Game event handlers → broadcast to clients
    // ═══════════════════════════════════════════════════════════════

    void HandleRoundStarted(int roundNumber)
    {
        string msg = NetworkMessageHelper.Wrap("RoundStarted", new RoundStartedMessage
        {
            roundNumber = roundNumber
        });
        server.Broadcast(msg);
    }

    void HandleRoundEnded(int roundNumber, int winnerPlayerNumber)
    {
        // stop all running scripts
        foreach (var kv in new Dictionary<int, Coroutine>(runningScripts))
        {
            if (kv.Value != null) StopCoroutine(kv.Value);
        }
        runningScripts.Clear();

        string msg = NetworkMessageHelper.Wrap("RoundEnded", new RoundEndedMessage
        {
            roundNumber = roundNumber,
            winnerPlayerNumber = winnerPlayerNumber
        });
        server.Broadcast(msg);
    }

    void HandleGameOver(int winnerPlayerNumber)
    {
        var wins = new List<PlayerWins>();
        if (GameManager.Instance != null)
        {
            foreach (var client in server.GetAllClients())
            {
                if (client.PlayerNumber > 0)
                {
                    wins.Add(new PlayerWins
                    {
                        playerNumber = client.PlayerNumber,
                        wins = GameManager.Instance.GetRoundWins(client.PlayerNumber)
                    });
                }
            }
        }

        string msg = NetworkMessageHelper.Wrap("GameOver", new GameOverMessage
        {
            winnerPlayerNumber = winnerPlayerNumber,
            roundWins = wins
        });
        server.Broadcast(msg);
    }

    void HandleTankDestroyed(int playerNumber)
    {
        string msg = NetworkMessageHelper.Wrap("TankDestroyed", new TankDestroyedMessage
        {
            playerNumber = playerNumber
        });
        server.Broadcast(msg);
    }

    void HandleReactiveInterval(int playerNumber)
    {
        // send only to the specific player
        if (playerClientMap.TryGetValue(playerNumber, out string clientId))
        {
            string msg = NetworkMessageHelper.Wrap("ReactiveInterval", new ReactiveIntervalMessage
            {
                playerNumber = playerNumber
            });
            server.SendToClient(clientId, msg);
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Periodic game state broadcast
    // ═══════════════════════════════════════════════════════════════

    IEnumerator BroadcastGameStateLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(broadcastInterval);

            if (server == null || !server.IsRunning) continue;
            if (server.ConnectedPlayerCount == 0) continue;

            // send per-player game state (health differs per player)
            foreach (var client in server.GetAllClients())
            {
                if (client.PlayerNumber <= 0) continue;

                int health = 0;
                int maxHealth = 3;
                var gm = GameManager.Instance;

                // find this player's health
                foreach (var th in FindObjectsByType<TankHealth>(FindObjectsSortMode.None))
                {
                    var listener = th.GetComponent<InputListener>();
                    if (listener != null && listener.playerNumber == client.PlayerNumber)
                    {
                        health = th.CurrentHealth;
                        maxHealth = th.maxHealth;
                        break;
                    }
                }

                var state = new GameStateMessage
                {
                    roundNumber = gm != null ? gm.CurrentRound : 0,
                    timeRemaining = gm != null ? gm.RoundTimeRemaining : 0f,
                    health = health,
                    maxHealth = maxHealth,
                    roundActive = gm != null && gm.RoundActive,
                    timerPaused = gm != null && gm.RoundTimerPaused,
                    alivePlayerCount = gm != null ? gm.AlivePlayerCount : 0,
                    gameMode = gm != null ? gm.gameMode.ToString() : "Dev"
                };

                server.SendToClient(client.Id, NetworkMessageHelper.Wrap("GameState", state));
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Client connect / disconnect
    // ═══════════════════════════════════════════════════════════════

    void HandleClientConnected(string clientId)
    {
        Debug.Log($"[GameSessionManager] New client connection: {clientId}");
    }

    void HandleClientDisconnected(string clientId)
    {
        if (clientPlayerMap.TryGetValue(clientId, out int playerNumber))
        {
            Debug.Log($"[GameSessionManager] Player {playerNumber} disconnected.");

            // stop their running script
            if (runningScripts.TryGetValue(playerNumber, out var co) && co != null)
                StopCoroutine(co);
            runningScripts.Remove(playerNumber);

            clientPlayerMap.Remove(clientId);
            playerClientMap.Remove(playerNumber);
        }

        BroadcastPlayerList();
    }

    // ═══════════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════════

    int AssignPlayerNumber()
    {
        // find PlayerStart objects in scene and assign to the first unoccupied one
        var starts = FindObjectsByType<PlayerStart>(FindObjectsSortMode.None);
        var usedNumbers = new HashSet<int>(playerClientMap.Keys);

        foreach (var ps in starts)
        {
            if (!usedNumbers.Contains(ps.playerNumber))
                return ps.playerNumber;
        }

        // fallback: generate next number
        for (int i = 1; i <= server.maxPlayers; i++)
        {
            if (!usedNumbers.Contains(i))
                return i;
        }

        return -1;
    }

    void BroadcastPlayerList()
    {
        var players = new List<PlayerInfo>();
        foreach (var client in server.GetAllClients())
        {
            if (client.PlayerNumber <= 0) continue;

            bool alive = true;
            if (GameManager.Instance != null)
                alive = GameManager.Instance.IsPlayerAlive(client.PlayerNumber);

            players.Add(new PlayerInfo
            {
                playerNumber = client.PlayerNumber,
                name = client.PlayerName,
                colorR = client.TankColor.r,
                colorG = client.TankColor.g,
                colorB = client.TankColor.b,
                alive = alive,
                connected = true
            });
        }

        string msg = NetworkMessageHelper.Wrap("PlayerList", new PlayerListMessage { players = players });
        server.Broadcast(msg);
    }

    void SendCommandLog(string clientId, string text, string level)
    {
        string msg = NetworkMessageHelper.Wrap("CommandLog", new CommandLogMessage
        {
            text = text,
            level = level
        });
        server.SendToClient(clientId, msg);
    }
}
