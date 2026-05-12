using System;
using System.Collections.Generic;
using UnityEngine;

// ═══════════════════════════════════════════════════════════════
//  Envelope — every message is wrapped in this
// ═══════════════════════════════════════════════════════════════

[Serializable]
public class NetworkEnvelope
{
    public string type;
    public string payload; // JSON string of the specific message
}

// ═══════════════════════════════════════════════════════════════
//  Client → Server messages
// ═══════════════════════════════════════════════════════════════

[Serializable]
public class JoinRequest
{
    public string roomCode;
    public string playerName;
}

[Serializable]
public class UpdateSettingsRequest
{
    public float colorR;
    public float colorG;
    public float colorB;
    public string defaultScript;
}

[Serializable]
public class SubmitScriptRequest
{
    public string script;
}

[Serializable]
public class HeartbeatMessage
{
    public long timestamp;
}

// ═══════════════════════════════════════════════════════════════
//  Server → Client messages
// ═══════════════════════════════════════════════════════════════

[Serializable]
public class JoinResponse
{
    public bool success;
    public int playerNumber;
    public string error;
}

[Serializable]
public class GameStateMessage
{
    public int roundNumber;
    public float timeRemaining;
    public int health;
    public int maxHealth;
    public bool roundActive;
    public bool timerPaused;
    public int alivePlayerCount;
    public string gameMode;
}

[Serializable]
public class RoundStartedMessage
{
    public int roundNumber;
}

[Serializable]
public class RoundEndedMessage
{
    public int roundNumber;
    public int winnerPlayerNumber;
}

[Serializable]
public class GameOverMessage
{
    public int winnerPlayerNumber;
    public List<PlayerWins> roundWins;
}

[Serializable]
public class PlayerWins
{
    public int playerNumber;
    public int wins;
}

[Serializable]
public class ReactiveIntervalMessage
{
    public int playerNumber;
}

[Serializable]
public class ScriptErrorMessage
{
    public string message;
    public int line;
}

[Serializable]
public class PlayerListMessage
{
    public List<PlayerInfo> players;
}

[Serializable]
public class PlayerInfo
{
    public int playerNumber;
    public string name;
    public float colorR;
    public float colorG;
    public float colorB;
    public bool alive;
    public bool connected;
}

[Serializable]
public class CommandLogMessage
{
    public string text;
    public string level; // "info", "success", "warning", "error"
}

[Serializable]
public class TankDestroyedMessage
{
    public int playerNumber;
}

// ═══════════════════════════════════════════════════════════════
//  Helper for serialization
// ═══════════════════════════════════════════════════════════════

public static class NetworkMessageHelper
{
    public static string Wrap<T>(string type, T payload)
    {
        string payloadJson = JsonUtility.ToJson(payload);
        var envelope = new NetworkEnvelope { type = type, payload = payloadJson };
        return JsonUtility.ToJson(envelope);
    }

    public static NetworkEnvelope Unwrap(string json)
    {
        return JsonUtility.FromJson<NetworkEnvelope>(json);
    }

    public static T ParsePayload<T>(string payloadJson)
    {
        return JsonUtility.FromJson<T>(payloadJson);
    }
}
