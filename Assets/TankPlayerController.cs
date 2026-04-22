using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
public class TankPlayerController : NetworkBehaviour
{
    [System.Serializable]
    public class TankInstruction
    {
        public InstructionType type;
        public float value;
    }

    public enum InstructionType
    {
        MoveForward,
        Rotate,
        Wait
    }

    public const int MaxLoopIterations = 128;
    public const int MaxLoopNesting = 8;
    public const float MaxMoveDistance = 100f;
    public const float MaxTurnDegrees = 1080f;
    public const float MaxWaitSeconds = 30f;

    [Header("Routine")]
    public bool autoRunOnSpawn = true;
    public bool loopRoutine;
    public List<TankInstruction> instructions = new List<TankInstruction>();

    [Header("Movement")]
    public float moveSpeed = 5f;
    public float rotateSpeed = 90f;
    public int maxInstructionsPerRoutine = 128;
    public int maxScriptCharacters = 4096;

    public static event Action<bool, string> LocalRoutineValidationResult;

    private readonly NetworkVariable<Vector3> authoritativePosition =
        new NetworkVariable<Vector3>(
            Vector3.zero,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<Quaternion> authoritativeRotation =
        new NetworkVariable<Quaternion>(
            Quaternion.identity,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

    private Coroutine runningRoutine;

    public override void OnNetworkSpawn()
    {
        EnsureTankPhysics();
        ApplyOwnerColor();

        if (IsServer)
        {
            PublishState(transform.position, transform.rotation);
        }

        if (IsOwner && autoRunOnSpawn && instructions.Count > 0)
        {
            SubmitRoutine(instructions, loopRoutine);
        }
    }

    void Update()
    {
        if (IsServer)
        {
            return;
        }

        transform.SetPositionAndRotation(authoritativePosition.Value, authoritativeRotation.Value);
    }

    public void SetAuthoritativeState(Vector3 position, Quaternion rotation)
    {
        if (!IsServer)
        {
            return;
        }

        transform.SetPositionAndRotation(position, rotation);
        PublishState(position, rotation);
    }

    public void SubmitRoutine(IReadOnlyList<TankInstruction> routine, bool shouldLoop)
    {
        if (!IsOwner || routine == null || routine.Count == 0)
        {
            return;
        }

        if (!ValidateCompiledRoutine(routine, maxInstructionsPerRoutine, out string validationError))
        {
            Debug.LogWarning(validationError);
            return;
        }

        NetworkInstruction[] payload = BuildPayload(routine, maxInstructionsPerRoutine);
        if (payload.Length == 0)
        {
            return;
        }

        SubmitCompiledRoutineServerRpc(payload, shouldLoop);
    }

    public bool SubmitScript(string scriptSource, bool shouldLoop, out string message)
    {
        if (!IsOwner)
        {
            message = "Only the owning client can submit routines.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(scriptSource))
        {
            message = "Script is empty.";
            return false;
        }

        if (scriptSource.Length > maxScriptCharacters)
        {
            message = $"Script is too large. Limit is {maxScriptCharacters} characters.";
            return false;
        }

        if (!TryCompileScript(scriptSource, maxInstructionsPerRoutine, out List<TankInstruction> compiled, out string compileMessage))
        {
            message = compileMessage;
            return false;
        }

        message = $"Compiled {compiled.Count} instruction(s). Sent to host for validation.";
        SubmitScriptServerRpc(scriptSource, shouldLoop);
        return true;
    }

    [ServerRpc]
    void SubmitCompiledRoutineServerRpc(NetworkInstruction[] routine, bool shouldLoop, ServerRpcParams rpcParams = default)
    {
        if (!ValidateNetworkRoutine(routine, maxInstructionsPerRoutine, out string validationError))
        {
            SendValidationResult(rpcParams.Receive.SenderClientId, false, validationError);
            return;
        }

        if (runningRoutine != null)
        {
            StopCoroutine(runningRoutine);
        }

        runningRoutine = StartCoroutine(RunRoutine(routine, shouldLoop));
        SendValidationResult(rpcParams.Receive.SenderClientId, true, $"Host accepted routine ({routine.Length} instruction(s)).");
    }

    [ServerRpc]
    void SubmitScriptServerRpc(string scriptSource, bool shouldLoop, ServerRpcParams rpcParams = default)
    {
        ulong senderClientId = rpcParams.Receive.SenderClientId;

        if (string.IsNullOrWhiteSpace(scriptSource))
        {
            SendValidationResult(senderClientId, false, "Script is empty.");
            return;
        }

        if (scriptSource.Length > maxScriptCharacters)
        {
            SendValidationResult(senderClientId, false, $"Script exceeds host limit ({maxScriptCharacters} chars).");
            return;
        }

        if (!TryCompileScript(scriptSource, maxInstructionsPerRoutine, out List<TankInstruction> compiled, out string compileError))
        {
            SendValidationResult(senderClientId, false, $"Host compile failed: {compileError}");
            return;
        }

        NetworkInstruction[] payload = BuildPayload(compiled, maxInstructionsPerRoutine);
        if (!ValidateNetworkRoutine(payload, maxInstructionsPerRoutine, out string validationError))
        {
            SendValidationResult(senderClientId, false, validationError);
            return;
        }

        if (runningRoutine != null)
        {
            StopCoroutine(runningRoutine);
        }

        runningRoutine = StartCoroutine(RunRoutine(payload, shouldLoop));
        SendValidationResult(senderClientId, true, $"Host accepted routine ({payload.Length} instruction(s)).");
    }

    IEnumerator RunRoutine(IReadOnlyList<NetworkInstruction> routine, bool shouldLoop)
    {
        do
        {
            for (int i = 0; i < routine.Count; i++)
            {
                var instruction = routine[i];
                switch (instruction.type)
                {
                    case (byte)InstructionType.MoveForward:
                        yield return MoveForward(instruction.value);
                        break;
                    case (byte)InstructionType.Rotate:
                        yield return Rotate(instruction.value);
                        break;
                    case (byte)InstructionType.Wait:
                        yield return Wait(instruction.value);
                        break;
                }
            }
        }
        while (shouldLoop);
    }

    IEnumerator MoveForward(float distance)
    {
        float distanceToTravel = Mathf.Abs(distance);
        float direction = Mathf.Sign(distance == 0f ? 1f : distance);
        Vector3 movementDirection = transform.forward * direction;
        Vector3 startPosition = transform.position;
        Vector3 targetPosition = startPosition + movementDirection * distanceToTravel;

        float travelled = 0f;
        while (travelled < distanceToTravel)
        {
            float step = moveSpeed * Time.deltaTime;
            transform.position += movementDirection * step;

            travelled = Vector3.Distance(startPosition, transform.position);
            PublishState(transform.position, transform.rotation);
            yield return null;
        }

        transform.position = targetPosition;
        PublishState(transform.position, transform.rotation);
    }

    IEnumerator Rotate(float degrees)
    {
        float rotated = 0f;
        float direction = Mathf.Sign(degrees == 0f ? 1f : degrees);

        while (Mathf.Abs(rotated) < Mathf.Abs(degrees))
        {
            float step = rotateSpeed * Time.deltaTime * direction;
            transform.Rotate(0f, step, 0f);

            rotated += step;
            PublishState(transform.position, transform.rotation);
            yield return null;
        }

        transform.Rotate(0f, degrees - rotated, 0f);
        PublishState(transform.position, transform.rotation);
    }

    IEnumerator Wait(float seconds)
    {
        float remaining = Mathf.Max(0f, seconds);
        while (remaining > 0f)
        {
            remaining -= Time.deltaTime;
            yield return null;
        }

        PublishState(transform.position, transform.rotation);
    }

    void PublishState(Vector3 position, Quaternion rotation)
    {
        authoritativePosition.Value = position;
        authoritativeRotation.Value = rotation;
    }

    static NetworkInstruction[] BuildPayload(IReadOnlyList<TankInstruction> source, int maxCount)
    {
        int count = Mathf.Min(maxCount, source.Count);
        NetworkInstruction[] payload = new NetworkInstruction[count];
        for (int i = 0; i < count; i++)
        {
            payload[i] = new NetworkInstruction
            {
                type = (byte)source[i].type,
                value = source[i].value
            };
        }

        return payload;
    }

    static bool ValidateCompiledRoutine(IReadOnlyList<TankInstruction> routine, int maxInstructions, out string error)
    {
        if (routine == null || routine.Count == 0)
        {
            error = "Routine is empty.";
            return false;
        }

        if (routine.Count > Mathf.Max(1, maxInstructions))
        {
            error = $"Routine exceeds max instruction count ({maxInstructions}).";
            return false;
        }

        for (int i = 0; i < routine.Count; i++)
        {
            TankInstruction instruction = routine[i];
            if (!ValidateInstructionValue(instruction.type, instruction.value, out error))
            {
                error = $"Instruction #{i + 1}: {error}";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    static bool ValidateNetworkRoutine(IReadOnlyList<NetworkInstruction> routine, int maxInstructions, out string error)
    {
        if (routine == null || routine.Count == 0)
        {
            error = "Routine is empty.";
            return false;
        }

        if (routine.Count > Mathf.Max(1, maxInstructions))
        {
            error = $"Routine exceeds max instruction count ({maxInstructions}).";
            return false;
        }

        for (int i = 0; i < routine.Count; i++)
        {
            NetworkInstruction instruction = routine[i];
            if (!Enum.IsDefined(typeof(InstructionType), (int)instruction.type))
            {
                error = $"Instruction #{i + 1}: invalid command id '{instruction.type}'.";
                return false;
            }

            InstructionType type = (InstructionType)instruction.type;
            if (!ValidateInstructionValue(type, instruction.value, out error))
            {
                error = $"Instruction #{i + 1}: {error}";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    static bool ValidateInstructionValue(InstructionType type, float value, out string error)
    {
        if (!IsFinite(value))
        {
            error = "value must be finite.";
            return false;
        }

        switch (type)
        {
            case InstructionType.MoveForward:
                if (Mathf.Abs(value) > MaxMoveDistance)
                {
                    error = $"MOVE must be between {-MaxMoveDistance} and {MaxMoveDistance}.";
                    return false;
                }
                break;
            case InstructionType.Rotate:
                if (Mathf.Abs(value) > MaxTurnDegrees)
                {
                    error = $"TURN must be between {-MaxTurnDegrees} and {MaxTurnDegrees}.";
                    return false;
                }
                break;
            case InstructionType.Wait:
                if (value < 0f || value > MaxWaitSeconds)
                {
                    error = $"WAIT must be between 0 and {MaxWaitSeconds}.";
                    return false;
                }
                break;
            default:
                error = $"Unknown command type '{type}'.";
                return false;
        }

        error = string.Empty;
        return true;
    }

    static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    void SendValidationResult(ulong targetClientId, bool accepted, string message)
    {
        ClientRpcParams rpcParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { targetClientId }
            }
        };

        RoutineValidationResultClientRpc(accepted, message, rpcParams);
    }

    [ClientRpc]
    void RoutineValidationResultClientRpc(bool accepted, string message, ClientRpcParams clientRpcParams = default)
    {
        if (!IsOwner)
        {
            return;
        }

        LocalRoutineValidationResult?.Invoke(accepted, message);
    }

    public static bool TryCompileScript(string source, int maxInstructions, out List<TankInstruction> compiledInstructions, out string message)
    {
        return TankScriptCompiler.TryCompile(source, maxInstructions, out compiledInstructions, out message);
    }

    public static string FormatInstruction(TankInstruction instruction)
    {
        string keyword;
        switch (instruction.type)
        {
            case InstructionType.MoveForward:
                keyword = "MOVE";
                break;
            case InstructionType.Rotate:
                keyword = "TURN";
                break;
            case InstructionType.Wait:
                keyword = "WAIT";
                break;
            default:
                keyword = instruction.type.ToString().ToUpperInvariant();
                break;
        }

        return string.Format(CultureInfo.InvariantCulture, "{0} {1:0.###}", keyword, instruction.value);
    }

    static class TankScriptCompiler
    {
        struct ScriptLine
        {
            public int lineNumber;
            public string content;
        }

        public static bool TryCompile(string source, int maxInstructions, out List<TankInstruction> compiledInstructions, out string message)
        {
            compiledInstructions = new List<TankInstruction>();
            maxInstructions = Mathf.Max(1, maxInstructions);

            List<ScriptLine> lines = ParseLines(source);
            if (lines.Count == 0)
            {
                message = "Script is empty.";
                return false;
            }

            int index = 0;
            if (!ParseBlock(lines, ref index, compiledInstructions, maxInstructions, -1, 0, out message))
            {
                return false;
            }

            if (compiledInstructions.Count == 0)
            {
                message = "Script produced no executable commands.";
                return false;
            }

            message = $"Compiled {compiledInstructions.Count} instruction(s).";
            return true;
        }

        static List<ScriptLine> ParseLines(string source)
        {
            List<ScriptLine> lines = new List<ScriptLine>();
            if (string.IsNullOrEmpty(source))
            {
                return lines;
            }

            string[] rawLines = source.Split('\n');
            for (int i = 0; i < rawLines.Length; i++)
            {
                string raw = rawLines[i].Replace("\r", string.Empty);
                raw = StripComment(raw).Trim();
                if (raw.Length == 0)
                {
                    continue;
                }

                lines.Add(new ScriptLine
                {
                    lineNumber = i + 1,
                    content = raw
                });
            }

            return lines;
        }

        static string StripComment(string raw)
        {
            int slashComment = raw.IndexOf("//", StringComparison.Ordinal);
            int hashComment = raw.IndexOf('#');

            int commentIndex = -1;
            if (slashComment >= 0 && hashComment >= 0)
            {
                commentIndex = Mathf.Min(slashComment, hashComment);
            }
            else if (slashComment >= 0)
            {
                commentIndex = slashComment;
            }
            else if (hashComment >= 0)
            {
                commentIndex = hashComment;
            }

            return commentIndex >= 0 ? raw.Substring(0, commentIndex) : raw;
        }

        static bool ParseBlock(
            IReadOnlyList<ScriptLine> lines,
            ref int index,
            List<TankInstruction> output,
            int maxInstructions,
            int loopStartLine,
            int depth,
            out string error)
        {
            while (index < lines.Count)
            {
                ScriptLine line = lines[index];
                string[] tokens = line.content.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length == 0)
                {
                    index++;
                    continue;
                }

                string command = tokens[0].ToUpperInvariant();
                if (command == "END")
                {
                    if (loopStartLine < 0)
                    {
                        error = $"Line {line.lineNumber}: END without matching LOOP.";
                        return false;
                    }

                    index++;
                    error = string.Empty;
                    return true;
                }

                if (command == "LOOP")
                {
                    if (depth >= MaxLoopNesting)
                    {
                        error = $"Line {line.lineNumber}: max loop nesting is {MaxLoopNesting}.";
                        return false;
                    }

                    if (tokens.Length != 2)
                    {
                        error = $"Line {line.lineNumber}: LOOP requires exactly one integer value.";
                        return false;
                    }

                    if (!int.TryParse(tokens[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int iterations))
                    {
                        error = $"Line {line.lineNumber}: invalid loop count '{tokens[1]}'.";
                        return false;
                    }

                    if (iterations < 1 || iterations > MaxLoopIterations)
                    {
                        error = $"Line {line.lineNumber}: loop count must be between 1 and {MaxLoopIterations}.";
                        return false;
                    }

                    index++;
                    List<TankInstruction> loopBody = new List<TankInstruction>();
                    if (!ParseBlock(lines, ref index, loopBody, maxInstructions, line.lineNumber, depth + 1, out error))
                    {
                        return false;
                    }

                    if (loopBody.Count == 0)
                    {
                        error = $"Line {line.lineNumber}: LOOP body is empty.";
                        return false;
                    }

                    for (int repeat = 0; repeat < iterations; repeat++)
                    {
                        for (int bodyIndex = 0; bodyIndex < loopBody.Count; bodyIndex++)
                        {
                            if (output.Count >= maxInstructions)
                            {
                                error = $"Line {line.lineNumber}: routine exceeds max {maxInstructions} instructions.";
                                return false;
                            }

                            output.Add(loopBody[bodyIndex]);
                        }
                    }

                    continue;
                }

                if (tokens.Length != 2)
                {
                    error = $"Line {line.lineNumber}: expected '<COMMAND> <value>'.";
                    return false;
                }

                if (!float.TryParse(tokens[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                {
                    error = $"Line {line.lineNumber}: invalid numeric value '{tokens[1]}'.";
                    return false;
                }

                if (!TryBuildInstruction(command, value, line.lineNumber, out TankInstruction instruction, out error))
                {
                    return false;
                }

                if (output.Count >= maxInstructions)
                {
                    error = $"Line {line.lineNumber}: routine exceeds max {maxInstructions} instructions.";
                    return false;
                }

                output.Add(instruction);
                index++;
            }

            if (loopStartLine >= 0)
            {
                error = $"Missing END for LOOP started on line {loopStartLine}.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        static bool TryBuildInstruction(string command, float value, int lineNumber, out TankInstruction instruction, out string error)
        {
            instruction = null;

            switch (command)
            {
                case "MOVE":
                    if (!ValidateInstructionValue(InstructionType.MoveForward, value, out error))
                    {
                        error = $"Line {lineNumber}: {error}";
                        return false;
                    }

                    instruction = new TankInstruction { type = InstructionType.MoveForward, value = value };
                    break;
                case "TURN":
                    if (!ValidateInstructionValue(InstructionType.Rotate, value, out error))
                    {
                        error = $"Line {lineNumber}: {error}";
                        return false;
                    }

                    instruction = new TankInstruction { type = InstructionType.Rotate, value = value };
                    break;
                case "WAIT":
                    if (!ValidateInstructionValue(InstructionType.Wait, value, out error))
                    {
                        error = $"Line {lineNumber}: {error}";
                        return false;
                    }

                    instruction = new TankInstruction { type = InstructionType.Wait, value = value };
                    break;
                default:
                    error = $"Line {lineNumber}: unknown command '{command}'. Use MOVE, TURN, WAIT, LOOP, END.";
                    return false;
            }

            error = string.Empty;
            return true;
        }
    }

    void EnsureTankPhysics()
    {
        if (!GetComponentInChildren<Collider>())
        {
            MeshFilter meshFilter = GetComponentInChildren<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                MeshCollider meshCollider = gameObject.AddComponent<MeshCollider>();
                meshCollider.sharedMesh = meshFilter.sharedMesh;
                meshCollider.convex = true;
            }
            else
            {
                gameObject.AddComponent<BoxCollider>();
            }
        }

        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
        }

        rb.useGravity = true;
    }

    void ApplyOwnerColor()
    {
        Renderer renderer = GetComponentInChildren<Renderer>();
        if (renderer == null)
        {
            return;
        }

        renderer.material = new Material(renderer.material);
        float hue = (OwnerClientId * 0.27f) % 1f;
        renderer.material.color = Color.HSVToRGB(hue, 0.8f, 0.9f);
    }

    public struct NetworkInstruction : INetworkSerializable
    {
        public byte type;
        public float value;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref type);
            serializer.SerializeValue(ref value);
        }
    }
}
