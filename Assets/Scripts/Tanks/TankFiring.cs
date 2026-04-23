using UnityEngine;

public class TankFiring : MonoBehaviour
{
    [Header("Firing")]
    public GameObject bulletPrefab;
    public float launchForce = 20f;

    [Header("Cooldown")]
    public float shootCooldown = 2f;

    private Transform fireTransform;
    private float _lastFireTime = float.MinValue;

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

        if (Time.time - _lastFireTime < shootCooldown)
        {
            Debug.Log($"[TankFiring] {gameObject.name} shoot on cooldown ({shootCooldown - (Time.time - _lastFireTime):F1}s remaining).");
            TankEventBus.CommandDone(playerNumber);
            return;
        }

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

        _lastFireTime = Time.time;
        Debug.Log($"[TankFiring] {gameObject.name} fired a bullet.");
        TankEventBus.CommandDone(playerNumber);
    }
}
