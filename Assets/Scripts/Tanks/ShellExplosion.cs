using UnityEngine;

public class ShellExplosion : MonoBehaviour
{
    public GameObject explosionEffectPrefab;
    public GameObject smokeRingEffectPrefab;
    public float effectDuration = 2f;
    public float explosionForce = 500f;
    public float explosionRadius = 5f;
    public int damage = 1;

    void OnCollisionEnter(Collision collision)
    {
        Vector3 explosionPos = transform.position;

        Collider[] hits = Physics.OverlapSphere(explosionPos, explosionRadius);
        foreach (Collider hit in hits)
        {
            if (hit is BoxCollider && hit.GetComponent<Terrain>() == null)
            {
                Debug.Log($"[ShellExplosion] Hit BoxCollider on {hit.gameObject.name}");
            }

            TankHealth health = hit.GetComponentInParent<TankHealth>();
            if (health != null)
            {
                Debug.Log($"TAKING DAMAGE: {hit.gameObject.name} (Health: {health.CurrentHealth})");

                health.TakeDamage(damage);
            }

            Rigidbody rb = hit.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.AddExplosionForce(explosionForce, explosionPos, explosionRadius);
            }
        }

        if (explosionEffectPrefab != null )
        {
            GameObject effect = Instantiate(explosionEffectPrefab, explosionPos, Quaternion.identity);
            Destroy(effect, effectDuration);
        }

        if (smokeRingEffectPrefab != null) 
        {
            GameObject smokeEffect = Instantiate(smokeRingEffectPrefab, explosionPos, Quaternion.identity);
            Destroy(smokeEffect, effectDuration * 1.5f);
        }


        Destroy(gameObject);
    }
}
