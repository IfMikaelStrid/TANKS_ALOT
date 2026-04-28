using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum GameMode
{
    Active,
    Passive,
    Reactive,
    Dev
}

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Game Settings")]
    public int numberOfRounds = 3;
    public float roundTime = 60f;
    public float intervalTime = 5f;
    public GameMode gameMode = GameMode.Dev;

    [Header("Reactive Mode")]
    [Tooltip("Seconds between reactive input intervals during a round.")]
    public float reactiveInterval = 15f;

    // ── Round state ──
    public int CurrentRound { get; private set; }
    public float RoundTimeRemaining { get; private set; }
    public bool RoundActive { get; private set; }
    public bool RoundTimerPaused { get; private set; }
    public bool GameInProgress { get; private set; }

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
        if (gameMode == GameMode.Dev)
        {
            Debug.Log("[GameManager] Dev mode — no rounds or timers.");
            return;
        }

        StartGame();
    }

    // ═══════════════════════════════════════════════════════════════
    //  Game Flow
    // ═══════════════════════════════════════════════════════════════

    public void StartGame()
    {
        GameInProgress = true;
        CurrentRound = 0;
        roundWins.Clear();
        StartNextRound();
    }

    void StartNextRound()
    {
        CurrentRound++;

        if (CurrentRound > numberOfRounds)
        {
            EndGame();
            return;
        }

        // discover alive players from InputListeners in scene
        alivePlayers.Clear();
        submittedPlayers.Clear();
        foreach (var listener in FindObjectsByType<InputListener>(FindObjectsSortMode.None))
            alivePlayers.Add(listener.playerNumber);

        if (alivePlayers.Count < 2)
        {
            Debug.LogWarning("[GameManager] Need at least 2 players to start a round.");
            EndGame();
            return;
        }

        RoundTimeRemaining = roundTime;
        RoundActive = false;
        RoundTimerPaused = false;

        if (gameMode == GameMode.Passive)
        {
            waitingForFirstSubmit = false;
            Debug.Log($"[GameManager] Round {CurrentRound}/{numberOfRounds} starting (passive mode).");
            BeginRound();
        }
        else
        {
            waitingForFirstSubmit = true;
            Debug.Log($"[GameManager] Round {CurrentRound}/{numberOfRounds} ready. Waiting for first submit...");
        }
    }

    void BeginRound()
    {
        RoundActive = true;
        waitingForFirstSubmit = false;

        Debug.Log($"[GameManager] Round {CurrentRound} started! Mode: {gameMode}, Time: {roundTime}s");
        TankEventBus.RoundStarted(CurrentRound);

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
        while (RoundTimeRemaining > 0f && RoundActive)
        {
            if (!RoundTimerPaused)
                RoundTimeRemaining -= Time.deltaTime;

            yield return null;
        }

        if (RoundActive)
        {
            Debug.Log("[GameManager] Round time expired!");
            EndRound(-1); // no winner (draw)
        }
    }

    IEnumerator ReactiveIntervalRoutine()
    {
        while (RoundActive)
        {
            float elapsed = 0f;
            while (elapsed < reactiveInterval)
            {
                if (!RoundTimerPaused && RoundActive)
                    elapsed += Time.deltaTime;
                yield return null;
            }

            if (!RoundActive) yield break;

            // pause and notify all alive players
            PauseRoundTimer();

            foreach (int pn in alivePlayers)
                TankEventBus.ReactiveInterval(pn);

            Debug.Log("[GameManager] Reactive interval — players may re-input.");
            // timer stays paused until all alive players re-submit
            submittedPlayers.Clear();
        }
    }

    public void PauseRoundTimer()
    {
        if (!RoundTimerPaused)
        {
            RoundTimerPaused = true;
            TankEventBus.RoundTimerPaused();
        }
    }

    public void ResumeRoundTimer()
    {
        if (RoundTimerPaused)
        {
            RoundTimerPaused = false;
            TankEventBus.RoundTimerResumed();
        }
    }

    void EndRound(int winnerPlayerNumber)
    {
        RoundActive = false;

        if (roundCoroutine != null) { StopCoroutine(roundCoroutine); roundCoroutine = null; }
        if (reactiveCoroutine != null) { StopCoroutine(reactiveCoroutine); reactiveCoroutine = null; }

        if (winnerPlayerNumber > 0)
        {
            if (!roundWins.ContainsKey(winnerPlayerNumber))
                roundWins[winnerPlayerNumber] = 0;
            roundWins[winnerPlayerNumber]++;
        }

        Debug.Log($"[GameManager] Round {CurrentRound} ended. Winner: {(winnerPlayerNumber > 0 ? $"Player {winnerPlayerNumber}" : "Draw")}");
        TankEventBus.RoundEnded(CurrentRound, winnerPlayerNumber);

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
        foreach (var ps in FindObjectsByType<PlayerStart>(FindObjectsSortMode.None))
            ps.ResetTank();
    }

    void EndGame()
    {
        GameInProgress = false;

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
        TankEventBus.GameOver(bestPlayer);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Event Handlers
    // ═══════════════════════════════════════════════════════════════

    void HandleTankDestroyed(int playerNumber)
    {
        alivePlayers.Remove(playerNumber);
        Debug.Log($"[GameManager] Player {playerNumber} eliminated. {alivePlayers.Count} remaining.");

        if (!RoundActive) return;

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
        submittedPlayers.Add(playerNumber);

        // first submit of any player starts the round
        if (waitingForFirstSubmit)
        {
            BeginRound();
            return;
        }

        // reactive mode: resume timer once all alive players have re-submitted
        if (gameMode == GameMode.Reactive && RoundTimerPaused && RoundActive)
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
