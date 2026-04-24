using System;
using System.Collections.Generic;
using System.Globalization;

// --- Node types ---

public abstract class TankNode { }

public class MoveNode : TankNode
{
    public float distance;
}

public class TurnNode : TankNode
{
    public float degrees;
    public float arcRadius;
}

public class ForNode : TankNode
{
    public int count;
    public List<TankNode> body = new List<TankNode>();
}

public enum TankCondition
{
    Spotted,
    NotSpotted
}

public class IfNode : TankNode
{
    public TankCondition condition;
    public List<TankNode> body = new List<TankNode>();
    public List<TankNode> elseBody = new List<TankNode>();
}

public class BoostNode : TankNode { }

public class FireNode : TankNode { }

public class WaitNode : TankNode
{
    public float seconds;
}

public enum FindTargetType
{
    Enemy
}

public class FindNode : TankNode
{
    public FindTargetType target;
}

// --- Parser ---

public static class TankScriptParser
{
    public static List<TankNode> Parse(string script)
    {
        var nodes = new List<TankNode>();
        if (string.IsNullOrWhiteSpace(script))
            return nodes;

        var lines = script.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        ParseBlock(lines, 0, lines.Length, nodes);
        return nodes;
    }

    private static int ParseBlock(string[] lines, int start, int end, List<TankNode> nodes)
    {
        int i = start;
        while (i < end)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("//"))
            {
                i++;
                continue;
            }

            var tokens = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var keyword = tokens[0].ToUpperInvariant();

            switch (keyword)
            {
                case "MOVE":
                case "FORWARD":
                    nodes.Add(new MoveNode { distance = ParseFloat(tokens, line) });
                    i++;
                    break;

                case "TURN":
                case "ROTATE":
                {
                    float degrees = ParseFloat(tokens, line);
                    float radius = tokens.Length >= 3
                        ? float.Parse(tokens[2], CultureInfo.InvariantCulture)
                        : 0f;
                    nodes.Add(new TurnNode { degrees = degrees, arcRadius = radius });
                    i++;
                    break;
                }

                case "FOR":
                    i = HandleFor(lines, tokens, i, end, nodes, line);
                    break;

                case "IF":
                    i = HandleIf(lines, tokens, i, end, nodes, line);
                    break;

                case "BOOST":
                    nodes.Add(new BoostNode());
                    i++;
                    break;

                case "FIRE":
                case "SHOOT":
                    nodes.Add(new FireNode());
                    i++;
                    break;

                case "WAIT":
                    nodes.Add(new WaitNode { seconds = ParseFloat(tokens, line) });
                    i++;
                    break;

                case "SCAN":
                case "RADAR":
                case "FIND":
                {
                    if (tokens.Length < 2)
                        throw new FormatException($"FIND requires a target argument (E): {line}");
                    var targetArg = tokens[1].ToUpperInvariant();
                    FindTargetType findTarget;
                    switch (targetArg)
                    {
                        case "E": findTarget = FindTargetType.Enemy; break;
                        default: throw new FormatException($"Unknown FIND target '{tokens[1]}' on line: {line}");
                    }
                    nodes.Add(new FindNode { target = findTarget });
                    i++;
                    break;
                }

                case "ELSE":
                case "END":
                    return i;

                default:
                    throw new FormatException($"Unknown command '{tokens[0]}' on line: {line}");
            }
        }
        return i;
    }

    private static int HandleFor(string[] lines, string[] tokens, int forLine, int end, List<TankNode> nodes, string rawLine)
    {
        if (tokens.Length < 2)
            throw new FormatException($"FOR requires an iteration count: {rawLine}");

        int count = int.Parse(tokens[1], CultureInfo.InvariantCulture);
        if (count < 0 || count > 1000)
            throw new FormatException($"FOR count must be between 0 and 1000: {rawLine}");

        var forNode = new ForNode { count = count };

        int bodyStart = forLine + 1;
        int bodyEnd = ParseBlock(lines, bodyStart, end, forNode.body);

        ExpectEnd(lines, bodyEnd, end, rawLine);
        nodes.Add(forNode);
        return bodyEnd + 1;
    }

    private static int HandleIf(string[] lines, string[] tokens, int ifLine, int end, List<TankNode> nodes, string rawLine)
    {
        if (tokens.Length < 2)
            throw new FormatException($"IF requires a condition: {rawLine}");

        var condStr = tokens[1].ToUpperInvariant();
        TankCondition condition;
        switch (condStr)
        {
            case "SPOTTED":
                condition = TankCondition.Spotted;
                break;
            case "NOT_SPOTTED":
                condition = TankCondition.NotSpotted;
                break;
            default:
                throw new FormatException($"Unknown condition '{tokens[1]}' on line: {rawLine}");
        }

        var ifNode = new IfNode { condition = condition };

        int pos = ParseBlock(lines, ifLine + 1, end, ifNode.body);

        // Check for ELSE block
        if (pos < end)
        {
            var elseTokens = lines[pos].Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (elseTokens.Length > 0 && elseTokens[0].ToUpperInvariant() == "ELSE")
            {
                pos = ParseBlock(lines, pos + 1, end, ifNode.elseBody);
            }
        }

        ExpectEnd(lines, pos, end, rawLine);
        nodes.Add(ifNode);
        return pos + 1;
    }

    private static void ExpectEnd(string[] lines, int pos, int end, string openLine)
    {
        if (pos >= end)
            throw new FormatException($"Missing END for: {openLine}");

        var kw = lines[pos].Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (kw.Length == 0 || kw[0].ToUpperInvariant() != "END")
            throw new FormatException($"Expected END, got '{lines[pos].Trim()}' for: {openLine}");
    }

    private static float ParseFloat(string[] tokens, string rawLine)
    {
        if (tokens.Length < 2)
            throw new FormatException($"Command requires a numeric value: {rawLine}");
        return float.Parse(tokens[1], CultureInfo.InvariantCulture);
    }
}
