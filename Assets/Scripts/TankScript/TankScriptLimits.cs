using System;
using System.Collections.Generic;

/// <summary>
/// Guard rails for client-submitted scripts. Script text arrives from an untrusted peer,
/// so the server must never execute a node list that has not passed through here.
/// </summary>
public static class TankScriptLimits
{
    public const int MaxCharacters = 2048;
    public const int MaxLines = 200;
    public const int MaxNodes = 400;
    public const int MaxNestingDepth = 6;
    public const int MaxLoopCount = 100;

    /// <summary>Upper bound on commands a single pass may issue.</summary>
    public const long MaxWork = 20000;

    public const float MaxMoveDistance = 1000f;
    public const float MaxTurnDegrees = 3600f;
    public const float MaxArcRadius = 100f;
    public const float MaxWaitSeconds = 60f;

    public static bool TryCompile(string script, out List<TankNode> nodes, out string error)
    {
        nodes = null;
        error = null;

        if (string.IsNullOrWhiteSpace(script))
        {
            error = "Script is empty.";
            return false;
        }

        if (script.Length > MaxCharacters)
        {
            error = $"Script too long ({script.Length} chars, max {MaxCharacters}).";
            return false;
        }

        int lineCount = 1;
        for (int i = 0; i < script.Length; i++)
        {
            if (script[i] == '\n') lineCount++;
        }

        if (lineCount > MaxLines)
        {
            error = $"Script has too many lines ({lineCount}, max {MaxLines}).";
            return false;
        }

        List<TankNode> parsed;
        try
        {
            parsed = TankScriptParser.Parse(script);
        }
        catch (FormatException e)
        {
            error = "Parse error: " + e.Message;
            return false;
        }
        catch (OverflowException)
        {
            error = "Parse error: numeric value out of range.";
            return false;
        }
        catch (ArgumentException e)
        {
            error = "Parse error: " + e.Message;
            return false;
        }

        if (parsed.Count == 0)
        {
            error = "Script produced no commands.";
            return false;
        }

        int nodeCount = 0;
        try
        {
            Inspect(parsed, 1, ref nodeCount);
        }
        catch (FormatException e)
        {
            error = e.Message;
            return false;
        }

        nodes = parsed;
        return true;
    }

    /// <summary>Returns the number of commands one pass over the block issues.</summary>
    static long Inspect(List<TankNode> nodes, int depth, ref int nodeCount)
    {
        if (depth > MaxNestingDepth)
            throw new FormatException($"Script nested too deeply (max {MaxNestingDepth}).");

        long work = 0;

        foreach (var node in nodes)
        {
            nodeCount++;
            if (nodeCount > MaxNodes)
                throw new FormatException($"Script has too many commands (max {MaxNodes}).");

            switch (node)
            {
                case MoveNode move:
                    RequireFinite(move.distance, "MOVE distance");
                    RequireRange(Math.Abs(move.distance), MaxMoveDistance, "MOVE distance");
                    work++;
                    break;

                case TurnNode turn:
                    RequireFinite(turn.degrees, "TURN degrees");
                    RequireFinite(turn.arcRadius, "TURN radius");
                    RequireRange(Math.Abs(turn.degrees), MaxTurnDegrees, "TURN degrees");
                    RequireRange(Math.Abs(turn.arcRadius), MaxArcRadius, "TURN radius");
                    work++;
                    break;

                case WaitNode wait:
                    RequireFinite(wait.seconds, "WAIT seconds");
                    if (wait.seconds < 0f)
                        throw new FormatException("WAIT seconds cannot be negative.");
                    RequireRange(wait.seconds, MaxWaitSeconds, "WAIT seconds");
                    work++;
                    break;

                case ForNode loop:
                    if (loop.count < 0 || loop.count > MaxLoopCount)
                        throw new FormatException($"FOR count must be between 0 and {MaxLoopCount}.");

                    long bodyWork = Inspect(loop.body, depth + 1, ref nodeCount);
                    work += 1 + loop.count * bodyWork;
                    break;

                case IfNode branch:
                    long thenWork = Inspect(branch.body, depth + 1, ref nodeCount);
                    long elseWork = Inspect(branch.elseBody, depth + 1, ref nodeCount);
                    work += 1 + Math.Max(thenWork, elseWork);
                    break;

                default:
                    work++;
                    break;
            }

            if (work > MaxWork)
                throw new FormatException($"Script does too much work in one pass (max {MaxWork} commands).");
        }

        return work;
    }

    static void RequireFinite(float value, string label)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            throw new FormatException($"{label} is not a finite number.");
    }

    static void RequireRange(float value, float max, string label)
    {
        if (value > max)
            throw new FormatException($"{label} exceeds the maximum of {max}.");
    }
}
