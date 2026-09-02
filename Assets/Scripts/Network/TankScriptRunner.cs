using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public enum TankLogLevel
{
    Info,
    Good,
    Warning,
    Error
}

/// <summary>
/// Owns script execution for one tank. Clients submit script text; the server validates it,
/// runs it and reports progress back to the owning client's console.
/// </summary>
[DisallowMultipleComponent]
public class TankScriptRunner : NetworkBehaviour
{
    [Tooltip("Pause inserted between commands.")]
    public float delayBetweenCommands = 0.1f;

    [Tooltip("Minimum seconds between accepted submissions from one client.")]
    public float submitCooldown = 0.5f;

    /// <summary>Raised on the owning client (and offline) with console output.</summary>
    public event Action<string, TankLogLevel> OnLog;

    readonly NetworkVariable<bool> m_RunningState = new NetworkVariable<bool>(false);

    InputListener m_Listener;
    Coroutine m_Routine;
    bool m_Running;
    bool m_CommandDone;
    bool m_RoundOver;
    float m_LastSubmitTime = float.MinValue;

    InputListener Listener
    {
        get
        {
            if (m_Listener == null) m_Listener = GetComponent<InputListener>();
            return m_Listener;
        }
    }

    public int PlayerNumber => Listener != null ? Listener.playerNumber : 0;

    public bool IsRunning => IsSpawned ? m_RunningState.Value : m_Running;

    void OnEnable()
    {
        TankEventBus.OnCommandDone += HandleCommandDone;
        TankEventBus.OnRoundEnded += HandleRoundEnded;
        TankEventBus.OnGameOver += HandleGameOver;
    }

    void OnDisable()
    {
        TankEventBus.OnCommandDone -= HandleCommandDone;
        TankEventBus.OnRoundEnded -= HandleRoundEnded;
        TankEventBus.OnGameOver -= HandleGameOver;
    }

    public override void OnNetworkDespawn()
    {
        if (m_Routine != null)
        {
            StopCoroutine(m_Routine);
            m_Routine = null;
        }

        m_Running = false;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Submission
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Called by the local console. Routes to the server when networked.</summary>
    public void Submit(string script, bool loop)
    {
        if (string.IsNullOrWhiteSpace(script)) return;

        if (IsSpawned && !IsServer)
        {
            if (script.Length > TankScriptLimits.MaxCharacters)
            {
                Report($"Script too long (max {TankScriptLimits.MaxCharacters} chars).", TankLogLevel.Error);
                return;
            }

            SubmitScriptRpc(new FixedString4096Bytes(script), loop);
            return;
        }

        Accept(script, loop);
    }

    public void Stop()
    {
        if (IsSpawned && !IsServer)
        {
            StopScriptRpc();
            return;
        }

        StopExecution();
        Report("Stopped.", TankLogLevel.Warning);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    void SubmitScriptRpc(FixedString4096Bytes script, bool loop)
    {
        if (Time.time - m_LastSubmitTime < submitCooldown)
        {
            Report("Submitting too fast.", TankLogLevel.Warning);
            return;
        }
        m_LastSubmitTime = Time.time;

        var health = GetComponent<TankHealth>();
        if (health != null && !health.IsAlive)
        {
            Report("Tank is destroyed.", TankLogLevel.Warning);
            return;
        }

        Accept(script.ToString(), loop);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    void StopScriptRpc()
    {
        StopExecution();
        Report("Stopped.", TankLogLevel.Warning);
    }

    void Accept(string script, bool loop)
    {
        if (!TankScriptLimits.TryCompile(script, out List<TankNode> nodes, out string error))
        {
            Report(error, TankLogLevel.Error);
            return;
        }

        // The mode comes from the authority, never from the submitting client.
        GameMode mode = GameManager.Instance != null ? GameManager.Instance.Mode : GameMode.Dev;

        if (mode != GameMode.Dev)
            TankEventBus.PlayerSubmitted(PlayerNumber);

        StopExecution();

        m_RoundOver = false;
        SetRunning(true);
        Report("Executing...", TankLogLevel.Good);
        m_Routine = StartCoroutine(RunRoutine(nodes, mode, loop));
    }

    void StopExecution()
    {
        if (m_Routine != null)
        {
            StopCoroutine(m_Routine);
            m_Routine = null;
        }

        SetRunning(false);
    }

    void SetRunning(bool value)
    {
        m_Running = value;

        if (IsSpawned && IsServer)
            m_RunningState.Value = value;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Execution
    // ═══════════════════════════════════════════════════════════════

    IEnumerator RunRoutine(List<TankNode> nodes, GameMode mode, bool loop)
    {
        yield return new WaitForSeconds(0.1f);

        bool repeat = mode == GameMode.Passive
                   || mode == GameMode.Reactive
                   || (mode == GameMode.Dev && loop);

        do
        {
            yield return ExecuteBlock(nodes);

            if (repeat && m_Running && !m_RoundOver)
                Report("Looping...", TankLogLevel.Info);
        }
        while (repeat && m_Running && !m_RoundOver);

        if (m_Running)
            Report("Done.", TankLogLevel.Good);

        m_Routine = null;
        SetRunning(false);
    }

    IEnumerator ExecuteBlock(List<TankNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (!m_Running || m_RoundOver) yield break;

            switch (node)
            {
                case MoveNode move:
                    Report($"MOVE {move.distance}", TankLogLevel.Info);
                    yield return RunCommand(() => TankEventBus.MoveForward(PlayerNumber, move.distance));
                    break;

                case TurnNode turn:
                    Report($"TURN {turn.degrees}{(turn.arcRadius > 0f ? $" (arc {turn.arcRadius})" : "")}", TankLogLevel.Info);
                    yield return RunCommand(() => TankEventBus.Turn(PlayerNumber, turn.degrees, turn.arcRadius));
                    break;

                case BoostNode:
                    Report("BOOST", TankLogLevel.Info);
                    yield return RunCommand(() => TankEventBus.Boost(PlayerNumber));
                    break;

                case FireNode:
                    Report("FIRE", TankLogLevel.Info);
                    yield return RunCommand(() => TankEventBus.Fire(PlayerNumber));
                    break;

                case FindNode:
                    Report("FIND", TankLogLevel.Info);
                    yield return RunCommand(() => TankEventBus.Find(PlayerNumber));
                    break;

                case WaitNode wait:
                    Report($"WAIT {wait.seconds}s", TankLogLevel.Info);
                    yield return new WaitForSeconds(wait.seconds);
                    break;

                case ForNode loop:
                    Report($"FOR {loop.count}", TankLogLevel.Info);
                    for (int i = 0; i < loop.count && m_Running && !m_RoundOver; i++)
                        yield return ExecuteBlock(loop.body);
                    break;

                case IfNode branch:
                {
                    bool result = Listener != null && Listener.EvaluateCondition(branch.condition);
                    Report($"IF {branch.condition} -> {result}", TankLogLevel.Info);

                    if (result)
                        yield return ExecuteBlock(branch.body);
                    else if (branch.elseBody.Count > 0)
                        yield return ExecuteBlock(branch.elseBody);
                    break;
                }
            }
        }
    }

    IEnumerator RunCommand(Action dispatch)
    {
        m_CommandDone = false;
        dispatch();

        while (!m_CommandDone && m_Running && !m_RoundOver)
            yield return null;

        if (delayBetweenCommands > 0f)
            yield return new WaitForSeconds(delayBetweenCommands);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Events
    // ═══════════════════════════════════════════════════════════════

    void HandleCommandDone(int playerNumber)
    {
        if (playerNumber == PlayerNumber)
            m_CommandDone = true;
    }

    void HandleRoundEnded(int roundNumber, int winner)
    {
        m_RoundOver = true;
        StopExecution();
    }

    void HandleGameOver(int winner)
    {
        m_RoundOver = true;
        StopExecution();
    }

    // ═══════════════════════════════════════════════════════════════
    //  Console output
    // ═══════════════════════════════════════════════════════════════

    void Report(string message, TankLogLevel level)
    {
        if (IsSpawned && IsServer)
        {
            LogRpc(Truncate(message), (int)level);
            return;
        }

        OnLog?.Invoke(message, level);
    }

    [Rpc(SendTo.Owner)]
    void LogRpc(FixedString512Bytes message, int level)
    {
        OnLog?.Invoke(message.ToString(), (TankLogLevel)level);
    }

    static FixedString512Bytes Truncate(string message)
    {
        if (string.IsNullOrEmpty(message)) return default;

        // FixedString512Bytes holds 509 bytes of UTF-8; stay well clear of multi-byte edge cases.
        const int limit = 160;
        if (message.Length > limit)
            message = message.Substring(0, limit);

        return new FixedString512Bytes(message);
    }
}
