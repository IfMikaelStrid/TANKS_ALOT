using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TankTestDriver : MonoBehaviour
{
    [Header("Target")]
    public int targetPlayerNumber = 1;

    [Header("Script")]
    [TextArea(5, 15)]
    public string tankScript = "FOR 4\nMOVE 5\nTURN 90\nEND";
    public bool autoRun = true;
    public float delayBetweenCommands = 0.1f;

    private Coroutine runningRoutine;
    private bool commandDone;

    void OnEnable()
    {
        TankEventBus.OnCommandDone += HandleCommandDone;
    }

    void OnDisable()
    {
        TankEventBus.OnCommandDone -= HandleCommandDone;
    }

    void Start()
    {
        if (autoRun)
            RunScript(tankScript);
    }

    public void RunScript(string script)
    {
        if (runningRoutine != null)
            StopCoroutine(runningRoutine);

        List<TankCommand> commands;
        try
        {
            commands = TankScriptParser.Parse(script);
        }
        catch (FormatException e)
        {
            Debug.LogError($"[TestDriver] Player {targetPlayerNumber} script error: {e.Message}");
            return;
        }

        runningRoutine = StartCoroutine(ExecuteCommands(commands));
    }

    private IEnumerator ExecuteCommands(List<TankCommand> commands)
    {
        yield return new WaitForSeconds(0.5f);

        foreach (var cmd in commands)
        {
            commandDone = false;

            switch (cmd.type)
            {
                case TankCommandType.Move:
                    TankEventBus.MoveForward(targetPlayerNumber, cmd.value);
                    break;
                case TankCommandType.Turn:
                    TankEventBus.Turn(targetPlayerNumber, cmd.value, cmd.arcRadius);
                    break;
            }

            while (!commandDone)
                yield return null;

            if (delayBetweenCommands > 0f)
                yield return new WaitForSeconds(delayBetweenCommands);
        }

        Debug.Log($"[TestDriver] Player {targetPlayerNumber} finished script execution.");
        runningRoutine = null;
    }

    private void HandleCommandDone(int playerNumber)
    {
        if (playerNumber != targetPlayerNumber) return;
        commandDone = true;
    }
}
