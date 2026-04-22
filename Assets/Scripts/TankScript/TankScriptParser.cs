using System;
using System.Collections.Generic;
using System.Globalization;

public enum TankCommandType
{
    Move,
    Turn
}

public struct TankCommand
{
    public TankCommandType type;
    public float value;

    public TankCommand(TankCommandType type, float value)
    {
        this.type = type;
        this.value = value;
    }
}

public static class TankScriptParser
{
    public static List<TankCommand> Parse(string script)
    {
        var commands = new List<TankCommand>();
        if (string.IsNullOrWhiteSpace(script))
            return commands;

        var lines = script.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        ParseBlock(lines, 0, lines.Length, commands);
        return commands;
    }

    private static int ParseBlock(string[] lines, int start, int end, List<TankCommand> commands)
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
                    commands.Add(new TankCommand(TankCommandType.Move, ParseFloat(tokens, line)));
                    i++;
                    break;

                case "TURN":
                case "ROTATE":
                    commands.Add(new TankCommand(TankCommandType.Turn, ParseFloat(tokens, line)));
                    i++;
                    break;

                case "FOR":
                    i = HandleFor(lines, tokens, i, end, commands, line);
                    break;

                case "END":
                    return i + 1;

                default:
                    throw new FormatException($"Unknown command '{tokens[0]}' on line: {line}");
            }
        }
        return i;
    }

    private static int HandleFor(string[] lines, string[] tokens, int forLine, int end, List<TankCommand> commands, string rawLine)
    {
        if (tokens.Length < 2)
            throw new FormatException($"FOR requires an iteration count: {rawLine}");

        int count = int.Parse(tokens[1], CultureInfo.InvariantCulture);
        if (count < 0 || count > 1000)
            throw new FormatException($"FOR count must be between 0 and 1000: {rawLine}");

        int bodyStart = forLine + 1;

        // Find the matching END
        int depth = 1;
        int bodyEnd = bodyStart;
        while (bodyEnd < end && depth > 0)
        {
            var kw = lines[bodyEnd].Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (kw.Length > 0)
            {
                var upper = kw[0].ToUpperInvariant();
                if (upper == "FOR") depth++;
                else if (upper == "END") depth--;
            }
            if (depth > 0) bodyEnd++;
        }

        if (depth != 0)
            throw new FormatException($"Missing END for FOR on line: {rawLine}");

        for (int n = 0; n < count; n++)
        {
            ParseBlock(lines, bodyStart, bodyEnd, commands);
        }

        return bodyEnd + 1; // skip past the END line
    }

    private static float ParseFloat(string[] tokens, string rawLine)
    {
        if (tokens.Length < 2)
            throw new FormatException($"Command requires a numeric value: {rawLine}");
        return float.Parse(tokens[1], CultureInfo.InvariantCulture);
    }
}
