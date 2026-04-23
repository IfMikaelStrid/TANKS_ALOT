using UnityEngine;

public class TankFiring : MonoBehaviour
{
    [Header("Firing")]
    public GameObject bulletPrefab;
    public float launchForce = 20f;

    private Transform fireTransform;

    void Awake()
    {
        fireTransform = transform.Find("FireTransform");
    }

    void OnEnable()
    {
        TankEventBus.OnFire += HandleFire;
    }

    void OnDisable()
    {
        TankEventBus.OnFire -= HandleFire;
    }

    private void HandleFire(int playerNumber)
    {
        var listener = GetComponentInParent<InputListener>();
        if (listener == null || playerNumber != listener.playerNumber) return;

        if (bulletPrefab == null || fireTransform == null)
        {
            Debug.LogWarning($"[TankFiring] {gameObject.name} missing bulletPrefab or fireTransform.");
            TankEventBus.CommandDone(playerNumber);
            return;
        }

        GameObject bullet = Instantiate(bulletPrefab, fireTransform.position, fireTransform.rotation);

        Rigidbody rb = bullet.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = fireTransform.forward * launchForce;
        }

        Debug.Log($"[TankFiring] {gameObject.name} fired a bullet.");
        TankEventBus.CommandDone(playerNumber);
    }
}
