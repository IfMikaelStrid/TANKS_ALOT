using UnityEngine;

/// <summary>
/// Marker placed in the gameplay scene that defines a spawn slot for one networked tank.
/// The actual tank prefab is instantiated and ownership-assigned by <see cref="NetworkPlayerSpawner"/>.
/// </summary>
public class PlayerStart : MonoBehaviour
{
    [Header("Slot")]
    public int playerNumber = 1;
    public float spawnHeightOffset = 0.1f;

    [Header("Tank Settings (applied by spawner)")]
    public float moveSpeed = 5f;
    public float rotateSpeed = 90f;
    public Color tankColor = Color.blue;

    [Header("Line of Sight (visual hint only)")]
    public bool showLineOfSight = true;
    public float losAngle = 90f;
    public float losRange = 50f;
    public Color losColor = new Color(1f, 1f, 0f, 0.25f);

    /// <summary>Resets the tank assigned to this slot (server only). Looks up the tank by player number.</summary>
    public void ResetTank()
    {
        InputListener target = null;
        foreach (var l in FindObjectsByType<InputListener>(FindObjectsSortMode.None))
            if (l.PlayerNumber == playerNumber) { target = l; break; }
        if (target == null) return;

        target.StopAllCoroutines();
        Vector3 spawnPos = transform.position + Vector3.up * spawnHeightOffset;
        target.transform.position = spawnPos;
        target.transform.rotation = transform.rotation;

        var health = target.GetComponent<TankHealth>();
        if (health != null) health.ResetHealth();

        target.gameObject.SetActive(true);
    }
}
