using System.Collections;
using UnityEngine;

public class InputListener : MonoBehaviour
{
    [Header("Player")]
    public int playerNumber = 1;

    [Header("Movement")]
    public float moveSpeed = 5f;
    public float rotateSpeed = 90f;

    void OnEnable()
    {
        TankEventBus.OnMoveForward += HandleMoveForward;
        TankEventBus.OnTurn += HandleTurn;
    }

    void OnDisable()
    {
        TankEventBus.OnMoveForward -= HandleMoveForward;
        TankEventBus.OnTurn -= HandleTurn;
    }

    private void HandleMoveForward(int playerNumber, float distance)
    {
        if (playerNumber != this.playerNumber) return;
        StartCoroutine(MoveForwardRoutine(distance));
    }

    private void HandleTurn(int playerNumber, float degrees)
    {
        if (playerNumber != this.playerNumber) return;
        StartCoroutine(TurnRoutine(degrees));
    }

    private IEnumerator MoveForwardRoutine(float distance)
    {
        float moved = 0f;
        float direction = Mathf.Sign(distance);
        float total = Mathf.Abs(distance);

        while (moved < total)
        {
            float step = moveSpeed * Time.deltaTime;
            if (moved + step > total) step = total - moved;
            transform.position += transform.forward * step * direction;
            moved += step;
            yield return null;
        }

        Debug.Log($"[InputListener] {gameObject.name} moved forward {distance} units.");
        TankEventBus.CommandDone(playerNumber);
    }

    private IEnumerator TurnRoutine(float degrees)
    {
        float turned = 0f;
        float direction = Mathf.Sign(degrees);
        float total = Mathf.Abs(degrees);

        while (turned < total)
        {
            float step = rotateSpeed * Time.deltaTime;
            if (turned + step > total) step = total - turned;
            transform.Rotate(0f, step * direction, 0f);
            turned += step;
            yield return null;
        }

        Debug.Log($"[InputListener] {gameObject.name} turned {degrees} degrees.");
        TankEventBus.CommandDone(playerNumber);
    }
}
