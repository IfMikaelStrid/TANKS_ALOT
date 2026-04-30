using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative tank controller. Movement runs on the server; clients see results via NetworkTransform.
/// Owners submit scripts via <see cref="SubmitScriptServerRpc"/>.
/// </summary>
public class InputListener : NetworkBehaviour
{
    [Header("Player")]
    public int playerNumber = 1;

    [Header("Visuals (synced)")]
    public Color tankColor = Color.blue;

    [Header("Movement")]
    public float moveSpeed = 5f;
    public float rotateSpeed = 90f;
    public float boostDistance = 8f;
    public float boostSpeed = 40f;

    [Header("Cooldown")]
    public float boostCooldown = 2f;
    public float findCooldown = 5f;

    [Header("Script Execution")]
    public float delayBetweenCommands = 0.1f;

    // Synced state
    readonly NetworkVariable<int> _netPlayerNumber = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    readonly NetworkVariable<Color> _netColor = new NetworkVariable<Color>(Color.blue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public int PlayerNumber => IsSpawned ? _netPlayerNumber.Value : playerNumber;

    float _lastBoostTime = float.MinValue;
    float _lastFindTime = float.MinValue;
    LineOfSightCone _cachedLoS;
    Coroutine _scriptRoutine;
    bool _commandDone;
    bool _stopRequested;
    bool _offlineSubscribed;

    static bool NetworkActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

    // ════════════════════════════════════════════════════════════════
    //  Lifecycle
    // ════════════════════════════════════════════════════════════════

    void Awake() { TankEventBus.OnCommandDone += OnCommandDoneStatic; }
    void OnDestroy() { TankEventBus.OnCommandDone -= OnCommandDoneStatic; }

    public override void OnNetworkSpawn()
    {
        UnsubscribeOffline();

        if (IsServer)
        {
            _netPlayerNumber.Value = playerNumber;
            _netColor.Value = tankColor;
            TankEventBus.OnMoveForward += HandleMoveForward;
            TankEventBus.OnTurn += HandleTurn;
            TankEventBus.OnBoost += HandleBoost;
            TankEventBus.OnFind += HandleFind;
        }

        playerNumber = _netPlayerNumber.Value;
        tankColor = _netColor.Value;
        _netColor.OnValueChanged += (_, c) => { tankColor = c; ApplyColor(); };
        ApplyColor();
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
        {
            TankEventBus.OnMoveForward -= HandleMoveForward;
            TankEventBus.OnTurn -= HandleTurn;
            TankEventBus.OnBoost -= HandleBoost;
            TankEventBus.OnFind -= HandleFind;
        }
    }

    // Offline fallback (NetworkManager not running): subscribe locally so existing tools work.
    void OnEnable()
    {
        if (!NetworkActive)
        {
            SubscribeOffline();
        }
    }

    void OnDisable()
    {
        UnsubscribeOffline();
    }

    void SubscribeOffline()
    {
        if (_offlineSubscribed) return;
        TankEventBus.OnMoveForward += HandleMoveForward;
        TankEventBus.OnTurn += HandleTurn;
        TankEventBus.OnBoost += HandleBoost;
        TankEventBus.OnFind += HandleFind;
        _offlineSubscribed = true;
    }

    void UnsubscribeOffline()
    {
        if (!_offlineSubscribed) return;
        TankEventBus.OnMoveForward -= HandleMoveForward;
        TankEventBus.OnTurn -= HandleTurn;
        TankEventBus.OnBoost -= HandleBoost;
        TankEventBus.OnFind -= HandleFind;
        _offlineSubscribed = false;
    }

    void ApplyColor()
    {
        var tracks = new Color(0.2f, 0.2f, 0.2f, 1f);
        foreach (var renderer in GetComponentsInChildren<Renderer>())
        {
            if (renderer.GetComponent<LineOfSightCone>() != null) continue;
            if (renderer.gameObject.name.ToUpperInvariant().Contains("TRACK"))
                renderer.material.color = tracks;
            else
                renderer.material.color = tankColor;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  Script submission (owner → server)
    // ════════════════════════════════════════════════════════════════

    [Rpc(SendTo.Server)]
    public void SubmitScriptServerRpc(string script, RpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        if (sender != OwnerClientId)
        {
            Debug.LogWarning($"[InputListener] Rejected script from non-owner {sender} (owner={OwnerClientId}).");
            return;
        }

        List<TankNode> nodes;
        try { nodes = TankScriptParser.Parse(script); }
        catch (FormatException e)
        {
            ScriptErrorClientRpc(e.Message, RpcTarget.Single(sender, RpcTargetUse.Temp));
            return;
        }

        if (nodes.Count == 0) return;

        StopScriptInternal();
        _stopRequested = false;
        _scriptRoutine = StartCoroutine(RunPassiveLoop(nodes));
    }

    [Rpc(SendTo.Server)]
    public void StopScriptServerRpc(RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId) return;
        StopScriptInternal();
    }

    [Rpc(SendTo.SpecifiedInParams)]
    void ScriptErrorClientRpc(string message, RpcParams rpcParams)
    {
        TankEventBus.ScriptError(playerNumber, message);
    }

    void StopScriptInternal()
    {
        _stopRequested = true;
        if (_scriptRoutine != null) { StopCoroutine(_scriptRoutine); _scriptRoutine = null; }
    }

    IEnumerator RunPassiveLoop(List<TankNode> nodes)
    {
        yield return new WaitForSeconds(0.1f);
        while (!_stopRequested && gameObject.activeInHierarchy && IsRoundActiveOrDev())
        {
            yield return ExecuteBlock(nodes);
        }
        _scriptRoutine = null;
    }

    bool IsRoundActiveOrDev()
    {
        var gm = GameManager.Instance;
        if (gm == null) return true;
        if (gm.gameMode == GameMode.Dev) return true;
        return gm.RoundActive;
    }

    IEnumerator ExecuteBlock(List<TankNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (_stopRequested) yield break;

            switch (node)
            {
                case MoveNode m:   yield return RunCommand(() => HandleMoveForward(playerNumber, m.distance)); break;
                case TurnNode t:   yield return RunCommand(() => HandleTurn(playerNumber, t.degrees, t.arcRadius)); break;
                case BoostNode:    yield return RunCommand(() => HandleBoost(playerNumber)); break;
                case FireNode:     yield return RunCommand(() => TankEventBus.Fire(playerNumber)); break;
                case FindNode:     yield return RunCommand(() => HandleFind(playerNumber)); break;
                case WaitNode w:   yield return new WaitForSeconds(w.seconds); break;
                case ForNode f:
                    for (int i = 0; i < f.count && !_stopRequested; i++)
                        yield return ExecuteBlock(f.body);
                    break;
                case IfNode ifn:
                    bool result = EvaluateCondition(ifn.condition);
                    if (result) yield return ExecuteBlock(ifn.body);
                    else if (ifn.elseBody.Count > 0) yield return ExecuteBlock(ifn.elseBody);
                    break;
            }
        }
    }

    IEnumerator RunCommand(Action dispatch)
    {
        _commandDone = false;
        dispatch();
        float t = 0f;
        const float timeout = 30f;
        while (!_commandDone && !_stopRequested && t < timeout) { t += Time.deltaTime; yield return null; }
        if (delayBetweenCommands > 0f) yield return new WaitForSeconds(delayBetweenCommands);
    }

    void OnCommandDoneStatic(int pn) { if (pn == playerNumber) _commandDone = true; }

    // ════════════════════════════════════════════════════════════════
    //  Command handlers (server-side movement primitives)
    // ════════════════════════════════════════════════════════════════

    bool ShouldHandle(int pn) => pn == playerNumber && (!NetworkActive || IsServer);

    void HandleMoveForward(int pn, float distance)
    {
        if (!ShouldHandle(pn)) return;
        StartCoroutine(MoveForwardRoutine(distance));
    }

    void HandleTurn(int pn, float degrees, float arcRadius)
    {
        if (!ShouldHandle(pn)) return;
        if (arcRadius > 0f) StartCoroutine(ArcTurnRoutine(degrees, arcRadius));
        else StartCoroutine(TurnRoutine(degrees));
    }

    void HandleBoost(int pn)
    {
        if (!ShouldHandle(pn)) return;
        if (Time.time - _lastBoostTime < boostCooldown) { TankEventBus.CommandDone(playerNumber); return; }
        _lastBoostTime = Time.time;
        StartCoroutine(BoostRoutine());
    }

    void HandleFind(int pn)
    {
        if (!ShouldHandle(pn)) return;
        if (Time.time - _lastFindTime < findCooldown) { TankEventBus.CommandDone(playerNumber); return; }
        _lastFindTime = Time.time;
        StartCoroutine(FindEnemyRoutine());
    }

    IEnumerator MoveForwardRoutine(float distance)
    {
        float moved = 0f;
        float dir = Mathf.Sign(distance);
        float total = Mathf.Abs(distance);
        while (moved < total)
        {
            float step = moveSpeed * Time.deltaTime;
            if (moved + step > total) step = total - moved;
            transform.position += transform.forward * step * dir;
            moved += step;
            yield return null;
        }
        TankEventBus.CommandDone(playerNumber);
    }

    IEnumerator TurnRoutine(float degrees)
    {
        float turned = 0f;
        float dir = Mathf.Sign(degrees);
        float total = Mathf.Abs(degrees);
        while (turned < total)
        {
            float step = rotateSpeed * Time.deltaTime;
            if (turned + step > total) step = total - turned;
            transform.Rotate(0f, step * dir, 0f);
            turned += step;
            yield return null;
        }
        TankEventBus.CommandDone(playerNumber);
    }

    IEnumerator ArcTurnRoutine(float degrees, float radius)
    {
        float turned = 0f;
        float dir = Mathf.Sign(degrees);
        float total = Mathf.Abs(degrees);
        Vector3 pivotOffset = transform.right * radius * dir;
        while (turned < total)
        {
            float step = rotateSpeed * Time.deltaTime;
            if (turned + step > total) step = total - turned;
            Vector3 pivot = transform.position + pivotOffset;
            transform.RotateAround(pivot, Vector3.up, step * dir);
            pivotOffset = transform.right * radius * dir;
            turned += step;
            yield return null;
        }
        TankEventBus.CommandDone(playerNumber);
    }

    IEnumerator BoostRoutine()
    {
        float moved = 0f;
        while (moved < boostDistance)
        {
            float t = moved / boostDistance;
            float speed = Mathf.Lerp(boostSpeed, moveSpeed, t * t);
            float step = speed * Time.deltaTime;
            if (moved + step > boostDistance) step = boostDistance - moved;
            transform.position += transform.forward * step;
            moved += step;
            yield return null;
        }
        TankEventBus.CommandDone(playerNumber);
    }

    IEnumerator FindEnemyRoutine()
    {
        var los = GetLineOfSight();
        float searchRange = los != null ? los.range : float.MaxValue;
        InputListener nearest = null;
        float nearestDist = float.MaxValue;

        foreach (var c in FindObjectsByType<InputListener>(FindObjectsSortMode.None))
        {
            if (c.PlayerNumber == playerNumber) continue;
            float d = Vector3.Distance(transform.position, c.transform.position);
            if (d <= searchRange && d < nearestDist) { nearest = c; nearestDist = d; }
        }
        if (nearest == null) { TankEventBus.CommandDone(playerNumber); yield break; }

        Vector3 toTarget = nearest.transform.position - transform.position;
        toTarget.y = 0f;
        float angle = Vector3.SignedAngle(transform.forward, toTarget, Vector3.up);
        yield return TurnRoutine(angle);
    }

    public bool EvaluateCondition(TankCondition condition)
    {
        var los = GetLineOfSight();
        if (los == null) return condition == TankCondition.NotSpotted;
        bool spotted = los.VisibleTanks.Count > 0;
        return condition == TankCondition.Spotted ? spotted : !spotted;
    }

    LineOfSightCone GetLineOfSight()
    {
        if (_cachedLoS != null) return _cachedLoS;
        _cachedLoS = GetComponentInChildren<LineOfSightCone>();
        return _cachedLoS;
    }
}
