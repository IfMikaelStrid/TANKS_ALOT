using System;

public static class TankEventBus
{
    public static event Action<int, float> OnMoveForward;
    public static event Action<int, float> OnTurn;
    public static event Action<int> OnCommandDone;

    public static void MoveForward(int playerNumber, float distance)
    {
        OnMoveForward?.Invoke(playerNumber, distance);
    }

    public static void Turn(int playerNumber, float degrees)
    {
        OnTurn?.Invoke(playerNumber, degrees);
    }

    public static void CommandDone(int playerNumber)
    {
        OnCommandDone?.Invoke(playerNumber);
    }
}
