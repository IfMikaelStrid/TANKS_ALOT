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

    public int PlayerNumber => m_PlayerNumber.Value;

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

    public override void OnNetworkSpawn()
    {
        m_PlayerNumber.OnValueChanged += HandlePlayerNumberChanged;
        Apply(m_PlayerNumber.Value);
    }

    public override void OnNetworkDespawn()
    {
        m_PlayerNumber.OnValueChanged -= HandlePlayerNumberChanged;
    }

    void HandlePlayerNumberChanged(int previous, int current)
    {
        Apply(current);
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
        cone.gameObject.SetActive(showLineOfSight);
    }
}
