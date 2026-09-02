using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Replicates which player slot a tank belongs to, so every peer colours, names and
/// wires up the same tank identically. The server assigns the slot; clients react.
/// </summary>
[DisallowMultipleComponent]
public class TankNetworkIdentity : NetworkBehaviour
{
    [Header("Line of Sight")]
    public bool showLineOfSight = true;
    public float losAngle = 90f;
    public float losRange = 50f;

    static readonly Color[] SlotColors =
    {
        new Color(0.20f, 0.40f, 0.90f, 1f),
        new Color(0.85f, 0.25f, 0.20f, 1f),
        new Color(0.25f, 0.70f, 0.30f, 1f),
        new Color(0.90f, 0.75f, 0.20f, 1f),
    };

    readonly NetworkVariable<int> m_PlayerNumber = new NetworkVariable<int>(0);
    readonly NetworkVariable<bool> m_Spotting = new NetworkVariable<bool>(false);
    readonly NetworkVariable<bool> m_Alive = new NetworkVariable<bool>(true);

    LineOfSightCone m_Cone;

    public int PlayerNumber => m_PlayerNumber.Value;

    public bool IsAlive => m_Alive.Value;

    /// <summary>True when this tank belongs to the local player.</summary>
    public bool IsLocalPlayerTank => IsSpawned && IsOwner;

    public static Color ColorForSlot(int playerNumber)
    {
        if (playerNumber < 1) return Color.grey;
        return SlotColors[(playerNumber - 1) % SlotColors.Length];
    }

    /// <summary>Server only. Must be called after the NetworkObject is spawned.</summary>
    public void AssignPlayerNumber(int playerNumber)
    {
        if (!IsServer)
        {
            Debug.LogError("[TankNetworkIdentity] Only the server may assign a player number.");
            return;
        }

        m_PlayerNumber.Value = playerNumber;
    }

    /// <summary>
    /// Server only. Deactivating a spawned NetworkObject breaks replication, so a "dead"
    /// tank is hidden and disabled instead of being turned off.
    /// </summary>
    public void SetAlive(bool value)
    {
        if (!IsServer) return;

        m_Alive.Value = value;
    }

    public override void OnNetworkSpawn()
    {
        m_PlayerNumber.OnValueChanged += HandlePlayerNumberChanged;
        m_Spotting.OnValueChanged += HandleSpottingChanged;
        m_Alive.OnValueChanged += HandleAliveChanged;

        Apply(m_PlayerNumber.Value);
        ApplyAlive(m_Alive.Value);
    }

    public override void OnNetworkDespawn()
    {
        m_PlayerNumber.OnValueChanged -= HandlePlayerNumberChanged;
        m_Spotting.OnValueChanged -= HandleSpottingChanged;
        m_Alive.OnValueChanged -= HandleAliveChanged;
    }

    void Update()
    {
        if (!IsSpawned || !IsServer || m_Cone == null) return;

        if (m_Spotting.Value != m_Cone.Alerted)
            m_Spotting.Value = m_Cone.Alerted;
    }

    void HandlePlayerNumberChanged(int previous, int current)
    {
        Apply(current);
    }

    void HandleSpottingChanged(bool previous, bool current)
    {
        if (m_Cone != null)
            m_Cone.SetAlerted(current);
    }

    void HandleAliveChanged(bool previous, bool current)
    {
        ApplyAlive(current);
    }

    void Apply(int playerNumber)
    {
        if (playerNumber < 1) return;

        gameObject.name = $"Tank_Player{playerNumber}";

        var listener = GetComponent<InputListener>();
        if (listener != null)
            listener.playerNumber = playerNumber;

        Color color = ColorForSlot(playerNumber);
        PlayerStart.ApplyTankColors(gameObject, color);
        EnsureLineOfSight();
    }

    void EnsureLineOfSight()
    {
        var cone = GetComponentInChildren<LineOfSightCone>(true);
        if (cone == null)
        {
            var losObject = new GameObject("LineOfSight");
            losObject.transform.SetParent(transform, false);
            cone = losObject.AddComponent<LineOfSightCone>();
        }

        cone.angle = losAngle;
        cone.range = losRange;
        cone.coneColor = new Color(1f, 1f, 0f, 0.25f);

        // Only the server raycasts; other peers just render what it reports.
        cone.runDetection = IsServer;
        if (!IsServer)
            cone.SetAlerted(m_Spotting.Value);

        cone.gameObject.SetActive(showLineOfSight && m_Alive.Value);
        m_Cone = cone;
    }

    void ApplyAlive(bool alive)
    {
        Transform coneRoot = m_Cone != null ? m_Cone.transform : null;

        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (coneRoot != null && renderer.transform.IsChildOf(coneRoot)) continue;
            renderer.enabled = alive;
        }

        foreach (var collider in GetComponentsInChildren<Collider>(true))
            collider.enabled = alive;

        if (m_Cone != null)
            m_Cone.gameObject.SetActive(showLineOfSight && alive);
    }
}
