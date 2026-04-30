using System;

public static class TankEventBus
{
    // ── Tank commands ──
    public static event Action<int, float> OnMoveForward;
    public static event Action<int, float, float> OnTurn;
    public static event Action<int> OnBoost;
    public static event Action<int> OnFire;
    public static event Action<int> OnFind;
    public static event Action<int> OnCommandDone;

    // ── Game events ──
    public static event Action<int> OnRoundStarted;        // roundNumber
    public static event Action<int, int> OnRoundEnded;     // roundNumber, winnerPlayerNumber
    public static event Action<int> OnGameOver;             // winnerPlayerNumber
    public static event Action<int> OnTankDestroyed;        // playerNumber
    public static event Action OnRoundTimerPaused;
    public static event Action OnRoundTimerResumed;
    public static event Action<int> OnPlayerSubmitted;      // playerNumber (first submit starts round)
    public static event Action<int> OnReactiveInterval;     // playerNumber (reactive mode: time to re-input)
    public static event Action<int, string> OnScriptError;  // playerNumber, message

    public static void MoveForward(int playerNumber, float distance)
    {
        OnMoveForward?.Invoke(playerNumber, distance);
    }

    public static void Turn(int playerNumber, float degrees, float arcRadius = 0f)
    {
        OnTurn?.Invoke(playerNumber, degrees, arcRadius);
    }

    public static void Boost(int playerNumber)
    {
        OnBoost?.Invoke(playerNumber);
    }

    public static void Fire(int playerNumber)
    {
        OnFire?.Invoke(playerNumber);
    }

    public static void Find(int playerNumber)
    {
        OnFind?.Invoke(playerNumber);
    }

    public static void CommandDone(int playerNumber)
    {
        OnCommandDone?.Invoke(playerNumber);
    }

    // ── Game event dispatchers ──

    public static void RoundStarted(int roundNumber)
    {
        OnRoundStarted?.Invoke(roundNumber);
    }

    public static void RoundEnded(int roundNumber, int winnerPlayerNumber)
    {
        OnRoundEnded?.Invoke(roundNumber, winnerPlayerNumber);
    }

    public static void GameOver(int winnerPlayerNumber)
    {
        OnGameOver?.Invoke(winnerPlayerNumber);
    }

    public static void TankDestroyed(int playerNumber)
    {
        OnTankDestroyed?.Invoke(playerNumber);
    }

    public static void RoundTimerPaused()
    {
        OnRoundTimerPaused?.Invoke();
    }

    public static void RoundTimerResumed()
    {
        OnRoundTimerResumed?.Invoke();
    }

    public static void PlayerSubmitted(int playerNumber)
    {
        OnPlayerSubmitted?.Invoke(playerNumber);
    }

    public static void ReactiveInterval(int playerNumber)
    {
        OnReactiveInterval?.Invoke(playerNumber);
    }

    public static void ScriptError(int playerNumber, string message)
    {
        OnScriptError?.Invoke(playerNumber, message);
    }
}
