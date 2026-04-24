using System;

public static class TankEventBus
{
    public static event Action<int, float> OnMoveForward;
    public static event Action<int, float, float> OnTurn;
    public static event Action<int> OnBoost;
    public static event Action<int> OnFire;
    public static event Action<int> OnFind;
    public static event Action<int> OnCommandDone;

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
}
