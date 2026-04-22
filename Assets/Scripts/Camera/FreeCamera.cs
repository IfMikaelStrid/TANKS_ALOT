using UnityEngine;
using UnityEngine.InputSystem;

public class FreeCamera : MonoBehaviour
{
    public float moveSpeed = 10f;
    public float fastSpeed = 25f;
    public float mouseSensitivity = 0.1f;

    private float rotationX = 0f;
    private float rotationY = 0f;

    void Update()
    {
        // Only control camera while RMB is held
        if (Mouse.current.rightButton.isPressed)
        {
            LockCursor(true);
            Look();
            Move();
        }
        else
        {
            LockCursor(false);
        }
    }

    void Look()
    {
        Vector2 mouseDelta = Mouse.current.delta.ReadValue();

        rotationX -= mouseDelta.y * mouseSensitivity;
        rotationY += mouseDelta.x * mouseSensitivity;

        rotationX = Mathf.Clamp(rotationX, -90f, 90f);

        transform.rotation = Quaternion.Euler(rotationX, rotationY, 0f);
    }

    void Move()
    {
        float speed = Keyboard.current.leftShiftKey.isPressed ? fastSpeed : moveSpeed;

        float x = 0f;
        float y = 0f;
        float z = 0f;

        if (Keyboard.current.aKey.isPressed) x -= 1f;
        if (Keyboard.current.dKey.isPressed) x += 1f;

        if (Keyboard.current.wKey.isPressed) z += 1f;
        if (Keyboard.current.sKey.isPressed) z -= 1f;

        if (Keyboard.current.eKey.isPressed) y += 1f;
        if (Keyboard.current.qKey.isPressed) y -= 1f;

        Vector3 move = transform.right * x + transform.forward * z + transform.up * y;

        transform.position += move * speed * Time.deltaTime;
    }

    void LockCursor(bool locked)
    {
        if (locked)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        else
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}