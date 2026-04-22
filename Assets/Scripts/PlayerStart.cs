using UnityEngine;

public class PlayerStart : MonoBehaviour
{
    [Header("Tank")]
    public GameObject tankPrefab;

    [Header("InputListener Settings")]
    public int playerNumber = 1;
    public float moveSpeed = 5f;
    public float rotateSpeed = 90f;

    [Header("Colors")]
    public Color tankColor = Color.blue;
    private static readonly Color tracksColor = new Color(0.2f, 0.2f, 0.2f, 1f);

    private GameObject spawnedTank;

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

        spawnedTank = Instantiate(tankPrefab, transform.position, transform.rotation);
        spawnedTank.name = $"Tank_Player{playerNumber}";

        var listener = spawnedTank.GetComponent<InputListener>();
        if (listener == null)
            listener = spawnedTank.AddComponent<InputListener>();

        listener.playerNumber = playerNumber;
        listener.moveSpeed = moveSpeed;
        listener.rotateSpeed = rotateSpeed;

        ApplyColors(spawnedTank);

        Debug.Log($"[PlayerStart] Spawned {spawnedTank.name} at {transform.position}");
        return spawnedTank;
    }

    private void ApplyColors(GameObject tank)
    {
        foreach (var renderer in tank.GetComponentsInChildren<Renderer>())
        {
            if (renderer.gameObject.name.ToUpperInvariant().Contains("TRACK"))
                renderer.material.color = tracksColor;
            else
                renderer.material.color = tankColor;
        }
    }
}
