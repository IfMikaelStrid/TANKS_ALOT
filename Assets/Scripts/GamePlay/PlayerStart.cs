using UnityEngine;

public class PlayerStart : MonoBehaviour
{
    [Header("Tank")]
    public GameObject tankPrefab;
    public float spawnHeightOffset = 0.1f;

    [Header("InputListener Settings")]
    public int playerNumber = 1;
    public float moveSpeed = 5f;
    public float rotateSpeed = 90f;

    [Header("Colors")]
    public Color tankColor = Color.blue;
    private static readonly Color tracksColor = new Color(0.2f, 0.2f, 0.2f, 1f);

    [Header("Line of Sight")]
    public bool showLineOfSight = true;
    public float losAngle = 90f;
    public float losRange = 50f;
    public Color losColor = new Color(1f, 1f, 0f, 0.25f);

    private GameObject spawnedTank;
    private GameObject losObject;

    void Start()
    {
        Spawn();
    }

    public GameObject Spawn()
    {
        if (tankPrefab == null)
        {
            Debug.LogError("[PlayerStart] No tank prefab assigned.");
            return null;
        }

        if (spawnedTank != null)
        {
            Debug.LogWarning("[PlayerStart] Tank already spawned.");
            return spawnedTank;
        }

        Vector3 spawnPos = transform.position + Vector3.up * spawnHeightOffset;
        spawnedTank = Instantiate(tankPrefab, spawnPos, transform.rotation);
        spawnedTank.name = $"Tank_Player{playerNumber}";

        var listener = spawnedTank.GetComponent<InputListener>();
        if (listener == null)
            listener = spawnedTank.AddComponent<InputListener>();

        listener.playerNumber = playerNumber;
        listener.moveSpeed = moveSpeed;
        listener.rotateSpeed = rotateSpeed;

        ApplyColors(spawnedTank);
        AttachLineOfSight(spawnedTank);

        Debug.Log($"[PlayerStart] Spawned {spawnedTank.name} at {transform.position}");
        return spawnedTank;
    }

    private void AttachLineOfSight(GameObject tank)
    {
        losObject = new GameObject("LineOfSight");
        losObject.transform.SetParent(tank.transform, false);

        var cone = losObject.AddComponent<LineOfSightCone>();
        cone.angle = losAngle;
        cone.range = losRange;
        cone.coneColor = losColor;

        losObject.SetActive(showLineOfSight);
    }

    void Update()
    {
        if (losObject != null && losObject.activeSelf != showLineOfSight)
            losObject.SetActive(showLineOfSight);
    }

    public void ResetTank()
    {
        if (spawnedTank == null) return;

        // Stop all running coroutines on the tank (movement, etc.)
        var listener = spawnedTank.GetComponent<InputListener>();
        if (listener != null)
            listener.StopAllCoroutines();

        // Reset position and rotation
        Vector3 spawnPos = transform.position + Vector3.up * spawnHeightOffset;
        spawnedTank.transform.position = spawnPos;
        spawnedTank.transform.rotation = transform.rotation;

        // Reset health
        var health = spawnedTank.GetComponent<TankHealth>();
        if (health != null)
            health.ResetHealth();

        // Re-enable if it was disabled
        spawnedTank.SetActive(true);

        Debug.Log($"[PlayerStart] Reset {spawnedTank.name} to spawn position.");
    }

    private void ApplyColors(GameObject tank)
    {
        foreach (var renderer in tank.GetComponentsInChildren<Renderer>())
        {
            // Skip the line-of-sight cone — it manages its own material
            if (renderer.GetComponent<LineOfSightCone>() != null)
                continue;

            if (renderer.gameObject.name.ToUpperInvariant().Contains("TRACK"))
                renderer.material.color = tracksColor;
            else
                renderer.material.color = tankColor;
        }
    }
}
