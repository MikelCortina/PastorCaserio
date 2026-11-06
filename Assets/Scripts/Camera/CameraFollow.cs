using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Cámara cenital/angulada que sigue al jugador.
/// </summary>
public class CameraFollow : MonoBehaviour
{
    [Header("Objetivo")]
    [FormerlySerializedAs("carTarget")]
    public Transform target;

    [Header("Posición de la cámara")]
    public float height = 18f;          // Altura sobre el objetivo
    public float distance = 10f;        // Distancia hacia atrás (crea el ángulo)
    public float tiltAngle = 65f;       // Inclinación (90 = cenital)
    public float followSmooth = 8f;     // Suavidad de seguimiento

    [Header("Rotación")]
    [Tooltip("True = la cámara rota con el objetivo. False = siempre apunta al norte.")]
    [FormerlySerializedAs("rotateWithCar")]
    public bool rotateWithTarget = false;
    public float rotateSmooth = 5f;

    private Vector3 _vel;
    private float _currentYaw;

    void Start()
    {
        if (target == null)
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) target = player.transform;
        }
    }

    void LateUpdate()
    {
        if (target == null) return;

        float targetYaw = rotateWithTarget ? target.eulerAngles.y : 0f;
        _currentYaw = Mathf.LerpAngle(_currentYaw, targetYaw, Time.deltaTime * rotateSmooth);

        Quaternion yawRot = Quaternion.Euler(0f, _currentYaw, 0f);
        Vector3 targetPos = target.position + yawRot * Vector3.back * distance + Vector3.up * height;
        transform.position = Vector3.SmoothDamp(transform.position, targetPos, ref _vel, 1f / followSmooth);

        Quaternion targetRot = Quaternion.Euler(tiltAngle, _currentYaw, 0f);
        transform.rotation = Quaternion.Lerp(transform.rotation, targetRot, Time.deltaTime * rotateSmooth);
    }

    public void SetTarget(Transform newTarget) => target = newTarget;

    /// <summary>Coloca la cámara directamente en su posición final (sin suavizado).</summary>
    public void SnapToTarget()
    {
        if (target == null) return;
        _currentYaw = rotateWithTarget ? target.eulerAngles.y : 0f;
        Quaternion yawRot = Quaternion.Euler(0f, _currentYaw, 0f);
        transform.position = target.position + yawRot * Vector3.back * distance + Vector3.up * height;
        transform.rotation = Quaternion.Euler(tiltAngle, _currentYaw, 0f);
        _vel = Vector3.zero;
    }
}
