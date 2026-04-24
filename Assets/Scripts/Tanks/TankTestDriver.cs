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
    public bool loopScript;
    public float delayBetweenCommands = 0.1f;

    private Coroutine runningRoutine;
    private bool commandDone;
    private InputListener cachedListener;

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

        List<TankNode> nodes;
        try
        {
            nodes = TankScriptParser.Parse(script);
        }
        catch (FormatException e)
        {
            Debug.LogError($"[TestDriver] Player {targetPlayerNumber} script error: {e.Message}");
            return;
        }

        runningRoutine = StartCoroutine(ExecuteNodes(nodes));
    }

    private IEnumerator ExecuteNodes(List<TankNode> nodes)
    {
        yield return new WaitForSeconds(0.5f);

        do
        {
            yield return ExecuteBlock(nodes);
        }
        while (loopScript);

        Debug.Log($"[TestDriver] Player {targetPlayerNumber} finished script execution.");
        runningRoutine = null;
    }

    private IEnumerator ExecuteBlock(List<TankNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node is MoveNode move)
            {
                yield return ExecuteCommand(() => TankEventBus.MoveForward(targetPlayerNumber, move.distance));
            }
            else if (node is TurnNode turn)
            {
                yield return ExecuteCommand(() => TankEventBus.Turn(targetPlayerNumber, turn.degrees, turn.arcRadius));
            }
            else if (node is BoostNode)
            {
                yield return ExecuteCommand(() => TankEventBus.Boost(targetPlayerNumber));
            }
            else if (node is FireNode)
            {
                yield return ExecuteCommand(() => TankEventBus.Fire(targetPlayerNumber));
            }
            else if (node is WaitNode wait)
            {
                yield return new WaitForSeconds(wait.seconds);
            }
            else if (node is FindNode find)
            {
                yield return ExecuteCommand(() => TankEventBus.Find(targetPlayerNumber));
            }
            else if (node is ForNode forNode)
            {
                for (int i = 0; i < forNode.count; i++)
                    yield return ExecuteBlock(forNode.body);
            }
            else if (node is IfNode ifNode)
            {
                bool result = GetListener()?.EvaluateCondition(ifNode.condition) ?? false;
                if (result)
                    yield return ExecuteBlock(ifNode.body);
                else if (ifNode.elseBody.Count > 0)
                    yield return ExecuteBlock(ifNode.elseBody);
            }
        }
    }

    private IEnumerator ExecuteCommand(Action dispatch)
    {
        commandDone = false;
        dispatch();

        while (!commandDone)
            yield return null;

        if (delayBetweenCommands > 0f)
            yield return new WaitForSeconds(delayBetweenCommands);
    }

    private InputListener GetListener()
    {
        if (cachedListener != null) return cachedListener;

        foreach (var listener in FindObjectsByType<InputListener>(FindObjectsSortMode.None))
        {
            if (listener.playerNumber == targetPlayerNumber)
            {
                cachedListener = listener;
                return cachedListener;
            }
        }
        return null;
    }

    private void HandleCommandDone(int playerNumber)
    {
        if (playerNumber != targetPlayerNumber) return;
        commandDone = true;
    }
}
