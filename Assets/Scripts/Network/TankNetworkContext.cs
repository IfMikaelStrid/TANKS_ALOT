using Unity.Netcode;

/// <summary>
/// Single place to ask "should this peer be simulating tanks?".
/// Offline play simulates locally; once a session is live only the server does.
/// </summary>
public static class TankNetworkContext
{
    /// <summary>True while a host, server or client session is running.</summary>
    public static bool SessionActive
    {
        get
        {
            var manager = NetworkManager.Singleton;
            return manager != null && manager.IsListening;
        }
    }

    /// <summary>True on the authority that is allowed to move tanks and resolve commands.</summary>
    public static bool SimulatesTanks
    {
        get
        {
            var manager = NetworkManager.Singleton;
            return manager == null || !manager.IsListening || manager.IsServer;
        }
    }
}
