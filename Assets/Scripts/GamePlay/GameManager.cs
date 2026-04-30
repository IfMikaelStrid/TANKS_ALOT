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
/// Server-authoritative round/game manager. Passive mode only is fully implemented for multiplayer.
/// Other modes are kept for offline/dev testing but should not be used in network play.
/// </summary>
public class GameManager : NetworkBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Game Settings")]
    public int numberOfRounds = 3;
    public float roundTime = 60f;
    public float intervalTime = 5f;
    public GameMode gameMode = GameMode.Passive;

    [Header("Round State (synced)")]
    readonly NetworkVariable<int> _currentRound = new NetworkVariable<int>(0);
    readonly NetworkVariable<float> _roundTimeRemaining = new NetworkVariable<float>(0f);
    readonly NetworkVariable<bool> _roundActive = new NetworkVariable<bool>(false);

    public int CurrentRound => _currentRound.Value;
    public float RoundTimeRemaining => _roundTimeRemaining.Value;
    public bool RoundActive => _roundActive.Value;
    public bool RoundTimerPaused { get; private set; }
    public bool GameInProgress { get; private set; }

    readonly HashSet<int> alivePlayers = new HashSet<int>();
    readonly Dictionary<int, int> roundWins = new Dictionary<int, int>();

    Coroutine roundCoroutine;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            TankEventBus.OnTankDestroyed += HandleTankDestroyed;
            // Force passive mode in network play.
            if (gameMode != GameMode.Passive && gameMode != GameMode.Dev)
            {
                Debug.LogWarning($"[GameManager] Forcing GameMode to Passive (was {gameMode}) — multiplayer currently supports passive only.");
                gameMode = GameMode.Passive;
            }
            // Wait a moment for clients/tanks to spawn, then start.
            StartCoroutine(StartGameDelayed());
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer) TankEventBus.OnTankDestroyed -= HandleTankDestroyed;
    }

    IEnumerator StartGameDelayed()
    {
        yield return new WaitForSeconds(2f);
        StartGame();
    }

    void Start()
    {
        // Offline / dev mode: original behaviour.
        bool networked = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        if (networked) return;
        if (gameMode == GameMode.Dev) { Debug.Log("[GameManager] Dev mode — no rounds or timers."); return; }

        TankEventBus.OnTankDestroyed += HandleTankDestroyed;
        StartGame();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        TankEventBus.OnTankDestroyed -= HandleTankDestroyed;
    }

    // ════════════════════════════════════════════════════════════════
    //  Game flow (server-authoritative when networked)
    // ════════════════════════════════════════════════════════════════

    public void StartGame()
    {
        GameInProgress = true;
        if (IsServerOrOffline()) _currentRound.Value = 0;
        roundWins.Clear();
        StartNextRound();
    }

    void StartNextRound()
    {
        if (!IsServerOrOffline()) return;

        _currentRound.Value = _currentRound.Value + 1;

        if (_currentRound.Value > numberOfRounds) { EndGame(); return; }

        alivePlayers.Clear();
        foreach (var listener in FindObjectsByType<InputListener>(FindObjectsSortMode.None))
            alivePlayers.Add(listener.PlayerNumber);

        if (alivePlayers.Count < 2)
        {
            Debug.LogWarning("[GameManager] Need at least 2 players to start a round.");
            EndGame();
            return;
        }

        _roundTimeRemaining.Value = roundTime;
        _roundActive.Value = false;
        RoundTimerPaused = false;

        Debug.Log($"[GameManager] Round {_currentRound.Value}/{numberOfRounds} starting (passive mode).");
        BeginRound();
    }

    void BeginRound()
    {
        _roundActive.Value = true;

        Debug.Log($"[GameManager] Round {_currentRound.Value} started!");
        TankEventBus.RoundStarted(_currentRound.Value);
        BroadcastRoundStartedClientRpc(_currentRound.Value);

        if (roundCoroutine != null) StopCoroutine(roundCoroutine);
        roundCoroutine = StartCoroutine(RoundTimerRoutine());
    }

    IEnumerator RoundTimerRoutine()
    {
        while (_roundTimeRemaining.Value > 0f && _roundActive.Value)
        {
            if (!RoundTimerPaused)
                _roundTimeRemaining.Value -= Time.deltaTime;
            yield return null;
        }
        if (_roundActive.Value) EndRound(-1);
    }

    void EndRound(int winnerPlayerNumber)
    {
        _roundActive.Value = false;
        if (roundCoroutine != null) { StopCoroutine(roundCoroutine); roundCoroutine = null; }

        if (winnerPlayerNumber > 0)
        {
            if (!roundWins.ContainsKey(winnerPlayerNumber)) roundWins[winnerPlayerNumber] = 0;
            roundWins[winnerPlayerNumber]++;
        }

        Debug.Log($"[GameManager] Round {_currentRound.Value} ended. Winner: {(winnerPlayerNumber > 0 ? $"Player {winnerPlayerNumber}" : "Draw")}");
        TankEventBus.RoundEnded(_currentRound.Value, winnerPlayerNumber);
        BroadcastRoundEndedClientRpc(_currentRound.Value, winnerPlayerNumber);

        StartCoroutine(IntervalThenNextRound());
    }

    IEnumerator IntervalThenNextRound()
    {
        if (intervalTime > 0f) yield return new WaitForSeconds(intervalTime);
        ResetAllTanks();
        StartNextRound();
    }

    void ResetAllTanks()
    {
        foreach (var ps in FindObjectsByType<PlayerStart>(FindObjectsSortMode.None))
            ps.ResetTank();
    }

    void EndGame()
    {
        GameInProgress = false;
        int bestPlayer = -1, bestWins = -1;
        foreach (var kv in roundWins)
            if (kv.Value > bestWins) { bestWins = kv.Value; bestPlayer = kv.Key; }

        Debug.Log($"[GameManager] Game over! Winner: {(bestPlayer > 0 ? $"Player {bestPlayer} ({bestWins} wins)" : "No winner")}");
        TankEventBus.GameOver(bestPlayer);
        BroadcastGameOverClientRpc(bestPlayer);
    }

    [Rpc(SendTo.NotServer)] void BroadcastRoundStartedClientRpc(int round) => TankEventBus.RoundStarted(round);
    [Rpc(SendTo.NotServer)] void BroadcastRoundEndedClientRpc(int round, int winner) => TankEventBus.RoundEnded(round, winner);
    [Rpc(SendTo.NotServer)] void BroadcastGameOverClientRpc(int winner) => TankEventBus.GameOver(winner);

    void HandleTankDestroyed(int playerNumber)
    {
        if (!IsServerOrOffline()) return;
        alivePlayers.Remove(playerNumber);
        Debug.Log($"[GameManager] Player {playerNumber} eliminated. {alivePlayers.Count} remaining.");

        if (!_roundActive.Value) return;
        if (alivePlayers.Count <= 1)
        {
            int winner = -1;
            foreach (int pn in alivePlayers) winner = pn;
            EndRound(winner);
        }
    }

    bool IsServerOrOffline()
    {
        bool networked = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        return !networked || IsServer;
    }

    public bool IsPlayerAlive(int playerNumber) => alivePlayers.Contains(playerNumber);
    public int AlivePlayerCount => alivePlayers.Count;
    public int GetRoundWins(int playerNumber) => roundWins.TryGetValue(playerNumber, out int w) ? w : 0;
}
