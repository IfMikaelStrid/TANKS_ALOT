using UnityEngine;

public class TankHealth : MonoBehaviour
{
    public int maxHealth = 3;
    private int currentHealth;

    void Awake()
    {
        currentHealth = maxHealth;
    }

    public void TakeDamage(int amount)
    {
        if (currentHealth <= 0) return;

        currentHealth -= amount;
        Debug.Log($"[TankHealth] {gameObject.name} took {amount} damage. Health: {currentHealth}/{maxHealth}");

        if (currentHealth <= 0)
        {
            currentHealth = 0;
            Debug.Log($"[TankHealth] {gameObject.name} destroyed!");

            var listener = GetComponent<InputListener>();
            if (listener != null)
                TankEventBus.TankDestroyed(listener.playerNumber);

            gameObject.SetActive(false);
        }
    }

    public void ResetHealth()
    {
        currentHealth = maxHealth;
    }

    public int CurrentHealth => currentHealth;
    public bool IsAlive => currentHealth > 0;
}
