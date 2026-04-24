using UnityEngine;

public class TankUprightCorrector : MonoBehaviour
{
    [Tooltip("Dot product threshold below which the tank is considered tipped (0.5 ≈ 60°).")]
    public float tiltThreshold = 0.5f;

    [Tooltip("How fast the tank rotates back upright (degrees per second).")]
    public float correctionSpeed = 180f;

    [Tooltip("Seconds to wait before starting correction (lets physics settle).")]
    public float correctionDelay = 0.5f;

    private float _tiltTimer;

    void Update()
    {
        float upDot = Vector3.Dot(transform.up, Vector3.up);

        if (upDot >= tiltThreshold)
        {
            _tiltTimer = 0f;
            return;
        }

        _tiltTimer += Time.deltaTime;
        if (_tiltTimer < correctionDelay)
            return;

        Quaternion target = Quaternion.FromToRotation(transform.up, Vector3.up) * transform.rotation;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, target, correctionSpeed * Time.deltaTime);
    }
}
