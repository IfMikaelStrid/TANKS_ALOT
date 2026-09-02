using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public enum GameMode
{
    Active,
    Passive,
    Reactive,
    Dev
}

/// <summary>
/// Owns round and game flow. Offline it runs locally; in a session only the server
/// advances state and every peer receives it through NetworkVariables and RPCs.
/// </summary>
public class GameManager : NetworkBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Game Settings")]
    public int numberOfRounds = 3;
    public float roundTime = 60f;
    public float intervalTime = 5f;
    public GameMode gameMode = GameMode.Dev;

    [Header("Multiplayer")]
    [Tooltip("Players required before the first round starts.")]
    [Min(2)]
    public int minimumPlayers = 2;

    [Header("Reactive Mode")]
    [Tooltip("Seconds between reactive input intervals during a round.")]
    public float reactiveInterval = 15f;

    // ── Replicated view of the authoritative state below ──
    readonly NetworkVariable<int> m_NetRound = new NetworkVariable<int>();
    readonly NetworkVariable<float> m_NetTimeRemaining = new NetworkVariable<float>();
    readonly NetworkVariable<bool> m_NetRoundActive = new NetworkVariable<bool>();
    readonly NetworkVariable<bool> m_NetTimerPaused = new NetworkVariable<bool>();
    readonly NetworkVariable<bool> m_NetGameInProgress = new NetworkVariable<bool>();
    readonly NetworkVariable<GameMode> m_NetMode = new NetworkVariable<GameMode>(GameMode.Dev);

    // ── Authoritative state (server, or the local machine when offline) ──
    int round;
    float timeRemaining;
    bool roundActive;
    bool timerPaused;
    bool gameInProgress;

    public int CurrentRound => IsSpawned ? m_NetRound.Value : round;
    public float RoundTimeRemaining => IsSpawned ? m_NetTimeRemaining.Value : timeRemaining;
    public bool RoundActive => IsSpawned ? m_NetRoundActive.Value : roundActive;
    public bool RoundTimerPaused => IsSpawned ? m_NetTimerPaused.Value : timerPaused;
    public bool GameInProgress => IsSpawned ? m_NetGameInProgress.Value : gameInProgress;
    public GameMode Mode => IsSpawned ? m_NetMode.Value : gameMode;

    /// <summary>True where round flow may be advanced.</summary>
    bool IsFlowAuthority => !IsSpawned || IsServer;

    // players holding a tank in the session
    readonly HashSet<int> connectedPlayers = new HashSet<int>();
    // tracks which players are alive this round
    readonly HashSet<int> alivePlayers = new HashSet<int>();
    // tracks which players have submitted (for first-submit-starts-round)
    readonly HashSet<int> submittedPlayers = new HashSet<int>();
    // round wins per player
    readonly Dictionary<int, int> roundWins = new Dictionary<int, int>();

    bool waitingForFirstSubmit;
    Coroutine roundCoroutine;
    Coroutine reactiveCoroutine;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void OnEnable()
    {
        TankEventBus.OnTankDestroyed += HandleTankDestroyed;
        TankEventBus.OnPlayerSubmitted += HandlePlayerSubmitted;
    }

    void OnDisable()
    {
        TankEventBus.OnTankDestroyed -= HandleTankDestroyed;
        TankEventBus.OnPlayerSubmitted -= HandlePlayerSubmitted;
    }

    void Start()
    {
        // Networked play waits for players; the server starts it from the roster instead.
        if (PlayerStart.NetworkManaged) return;

        if (gameMode == GameMode.Dev)
        {
            Debug.Log("[GameManager] Dev mode — no rounds or timers.");
            return;
        }

        StartGame();
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        m_NetMode.Value = gameMode;

        if (gameMode == GameMode.Dev)
            Debug.Log("[GameManager] Dev mode — no rounds or timers.");
    }

    // ═══════════════════════════════════════════════════════════════
    //  Roster
    // ═══════════════════════════════════════════════════════════════

    public void NotifyPlayerJoined(int playerNumber)
    {
        if (!IsFlowAuthority || playerNumber < 1) return;

        connectedPlayers.Add(playerNumber);

        if (gameMode == GameMode.Dev || gameInProgress) return;

        if (connectedPlayers.Count >= minimumPlayers)
        {
            Debug.Log($"[GameManager] {connectedPlayers.Count} players connected — starting game.");
            StartGame();
        }
    }

    public void NotifyPlayerLeft(int playerNumber)
    {
        if (!IsFlowAuthority || playerNumber < 1) return;

        connectedPlayers.Remove(playerNumber);
        submittedPlayers.Remove(playerNumber);

        // Otherwise the round waits forever on a player who is no longer here.
        HandleTankDestroyed(playerNumber);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Game Flow
    // ═══════════════════════════════════════════════════════════════

    public void StartGame()
    {
        if (!IsFlowAuthority) return;

        gameInProgress = true;
        round = 0;
        roundWins.Clear();
        Publish();
        StartNextRound();
    }

    void StartNextRound()
    {
        round++;

        if (round > numberOfRounds)
        {
            EndGame();
            return;
        }

        alivePlayers.Clear();
        submittedPlayers.Clear();

        if (PlayerStart.NetworkManaged)
        {
            alivePlayers.UnionWith(connectedPlayers);
        }
        else
        {
            foreach (var listener in FindObjectsByType<InputListener>(FindObjectsSortMode.None))
                alivePlayers.Add(listener.playerNumber);
        }

        if (alivePlayers.Count < 2)
        {
            Debug.LogWarning("[GameManager] Need at least 2 players to start a round.");
            EndGame();
            return;
        }

        timeRemaining = roundTime;
        roundActive = false;
        timerPaused = false;
        Publish();

        if (gameMode == GameMode.Passive)
        {
            waitingForFirstSubmit = false;
            Debug.Log($"[GameManager] Round {round}/{numberOfRounds} starting (passive mode).");
            BeginRound();
        }
        else
        {
            waitingForFirstSubmit = true;
            Debug.Log($"[GameManager] Round {round}/{numberOfRounds} ready. Waiting for first submit...");
        }
    }

    void BeginRound()
    {
        roundActive = true;
        waitingForFirstSubmit = false;
        Publish();

        Debug.Log($"[GameManager] Round {round} started! Mode: {gameMode}, Time: {roundTime}s");
        BroadcastRoundStarted(round);

        if (roundCoroutine != null) StopCoroutine(roundCoroutine);
        roundCoroutine = StartCoroutine(RoundTimerRoutine());

        if (gameMode == GameMode.Reactive)
        {
            if (reactiveCoroutine != null) StopCoroutine(reactiveCoroutine);
            reactiveCoroutine = StartCoroutine(ReactiveIntervalRoutine());
        }
    }

    IEnumerator RoundTimerRoutine()
    {
        float nextPublish = 0f;

        while (timeRemaining > 0f && roundActive)
        {
            if (!timerPaused)
                timeRemaining -= Time.deltaTime;

            // The countdown only shows whole seconds, so 5 Hz on the wire is plenty.
            nextPublish -= Time.deltaTime;
            if (nextPublish <= 0f)
            {
                nextPublish = 0.2f;
                Publish();
            }

            yield return null;
        }

        timeRemaining = Mathf.Max(0f, timeRemaining);
        Publish();

        if (roundActive)
        {
            Debug.Log("[GameManager] Round time expired!");
            EndRound(-1); // no winner (draw)
        }
    }

    IEnumerator ReactiveIntervalRoutine()
    {
        while (roundActive)
        {
            float elapsed = 0f;
            while (elapsed < reactiveInterval)
            {
                if (!timerPaused && roundActive)
                    elapsed += Time.deltaTime;
                yield return null;
            }

            if (!roundActive) yield break;

            PauseRoundTimer();

            foreach (int pn in alivePlayers)
                BroadcastReactiveInterval(pn);

            Debug.Log("[GameManager] Reactive interval — players may re-input.");
            submittedPlayers.Clear();
        }
    }

    public void PauseRoundTimer()
    {
        if (!IsFlowAuthority || timerPaused) return;

        timerPaused = true;
        Publish();
        BroadcastTimerPaused();
    }

    public void ResumeRoundTimer()
    {
        if (!IsFlowAuthority || !timerPaused) return;

        timerPaused = false;
        Publish();
        BroadcastTimerResumed();
    }

    void EndRound(int winnerPlayerNumber)
    {
        roundActive = false;
        Publish();

        if (roundCoroutine != null) { StopCoroutine(roundCoroutine); roundCoroutine = null; }
        if (reactiveCoroutine != null) { StopCoroutine(reactiveCoroutine); reactiveCoroutine = null; }

        if (winnerPlayerNumber > 0)
        {
            if (!roundWins.ContainsKey(winnerPlayerNumber))
                roundWins[winnerPlayerNumber] = 0;
            roundWins[winnerPlayerNumber]++;
        }

        Debug.Log($"[GameManager] Round {round} ended. Winner: {(winnerPlayerNumber > 0 ? $"Player {winnerPlayerNumber}" : "Draw")}");
        BroadcastRoundEnded(round, winnerPlayerNumber);

        StartCoroutine(IntervalThenNextRound());
    }

    IEnumerator IntervalThenNextRound()
    {
        if (intervalTime > 0f)
        {
            Debug.Log($"[GameManager] Interval: {intervalTime}s before next round.");
            yield return new WaitForSeconds(intervalTime);
        }

        ResetAllTanks();
        StartNextRound();
    }

    void ResetAllTanks()
    {
        if (PlayerStart.NetworkManaged)
        {
            if (TankSpawnManager.Instance != null)
                TankSpawnManager.Instance.ResetAllTanks();
            return;
        }

        foreach (var ps in FindObjectsByType<PlayerStart>(FindObjectsSortMode.None))
            ps.ResetTank();
    }

    void EndGame()
    {
        gameInProgress = false;
        Publish();

        int bestPlayer = -1;
        int bestWins = -1;
        foreach (var kv in roundWins)
        {
            if (kv.Value > bestWins)
            {
                bestWins = kv.Value;
                bestPlayer = kv.Key;
            }
        }

        Debug.Log($"[GameManager] Game over! Winner: {(bestPlayer > 0 ? $"Player {bestPlayer} ({bestWins} wins)" : "No winner")}");
        BroadcastGameOver(bestPlayer);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Replication
    // ═══════════════════════════════════════════════════════════════

    void Publish()
    {
        if (!IsSpawned || !IsServer) return;

        m_NetRound.Value = round;
        m_NetTimeRemaining.Value = timeRemaining;
        m_NetRoundActive.Value = roundActive;
        m_NetTimerPaused.Value = timerPaused;
        m_NetGameInProgress.Value = gameInProgress;
    }

    void BroadcastRoundStarted(int roundNumber)
    {
        if (IsSpawned) RoundStartedRpc(roundNumber);
        else TankEventBus.RoundStarted(roundNumber);
    }

    void BroadcastRoundEnded(int roundNumber, int winner)
    {
        if (IsSpawned) RoundEndedRpc(roundNumber, winner);
        else TankEventBus.RoundEnded(roundNumber, winner);
    }

    void BroadcastGameOver(int winner)
    {
        if (IsSpawned) GameOverRpc(winner);
        else TankEventBus.GameOver(winner);
    }

    void BroadcastTimerPaused()
    {
        if (IsSpawned) TimerPausedRpc();
        else TankEventBus.RoundTimerPaused();
    }

    void BroadcastTimerResumed()
    {
        if (IsSpawned) TimerResumedRpc();
        else TankEventBus.RoundTimerResumed();
    }

    void BroadcastReactiveInterval(int playerNumber)
    {
        if (IsSpawned) ReactiveIntervalRpc(playerNumber);
        else TankEventBus.ReactiveInterval(playerNumber);
    }

    [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
    void RoundStartedRpc(int roundNumber) => TankEventBus.RoundStarted(roundNumber);

    [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
    void RoundEndedRpc(int roundNumber, int winner) => TankEventBus.RoundEnded(roundNumber, winner);

    [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
    void GameOverRpc(int winner) => TankEventBus.GameOver(winner);

    [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
    void TimerPausedRpc() => TankEventBus.RoundTimerPaused();

    [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
    void TimerResumedRpc() => TankEventBus.RoundTimerResumed();

    [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
    void ReactiveIntervalRpc(int playerNumber) => TankEventBus.ReactiveInterval(playerNumber);

    // ═══════════════════════════════════════════════════════════════
    //  Event Handlers
    // ═══════════════════════════════════════════════════════════════

    void HandleTankDestroyed(int playerNumber)
    {
        if (!IsFlowAuthority) return;
        if (!alivePlayers.Remove(playerNumber)) return;

        Debug.Log($"[GameManager] Player {playerNumber} eliminated. {alivePlayers.Count} remaining.");

        if (!roundActive) return;

        if (alivePlayers.Count <= 1)
        {
            int winner = -1;
            foreach (int pn in alivePlayers)
                winner = pn;

            EndRound(winner);
        }
    }

    void HandlePlayerSubmitted(int playerNumber)
    {
        if (!IsFlowAuthority) return;

        submittedPlayers.Add(playerNumber);

        // first submit of any player starts the round
        if (waitingForFirstSubmit)
        {
            BeginRound();
            return;
        }

        // reactive mode: resume timer once all alive players have re-submitted
        if (gameMode == GameMode.Reactive && timerPaused && roundActive)
        {
            if (submittedPlayers.IsSupersetOf(alivePlayers))
            {
                Debug.Log("[GameManager] All players re-submitted. Resuming round timer.");
                ResumeRoundTimer();
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Queries
    // ═══════════════════════════════════════════════════════════════

    public bool IsPlayerAlive(int playerNumber) => alivePlayers.Contains(playerNumber);
    public int AlivePlayerCount => alivePlayers.Count;
    public int GetRoundWins(int playerNumber) => roundWins.TryGetValue(playerNumber, out int w) ? w : 0;
}
