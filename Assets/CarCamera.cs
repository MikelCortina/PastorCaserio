using UnityEngine;

public class CarCamera : MonoBehaviour
{
    [Header("Target")]
    public Transform carTarget;

    [Header("Posición de la cámara")]
    public float height = 18f;          // Altura sobre el coche
    public float distance = 10f;        // Distancia hacia atrás (crea el ángulo)
    public float tiltAngle = 65f;       // Inclinación vertical (90 = cenital, ~55-70 = angulado)
    public float followSmooth = 8f;     // Suavidad de seguimiento

    [Header("Rotación con el coche")]
    [Tooltip("True = la cámara rota con el coche (GTA 1 style).\n" +
             "False = siempre apunta al norte.")]
    public bool rotateWithCar = false;
    public float rotateSmooth = 5f;

    private Vector3 _vel;
    private float _currentYaw = 0f;

    void LateUpdate()
    {
        if (carTarget == null) return;

        // Yaw objetivo: el del coche o fijo al norte
        float targetYaw = rotateWithCar ? carTarget.eulerAngles.y : 0f;
        _currentYaw = Mathf.LerpAngle(_currentYaw, targetYaw, Time.deltaTime * rotateSmooth);

        // Calcular offset de posición: hacia atrás y arriba según el yaw actual
        Quaternion yawRot = Quaternion.Euler(0f, _currentYaw, 0f);
        Vector3 backOffset = yawRot * Vector3.back * distance;
        Vector3 upOffset = Vector3.up * height;

        Vector3 targetPos = carTarget.position + backOffset + upOffset;
        transform.position = Vector3.SmoothDamp(transform.position, targetPos,
                                                ref _vel, 1f / followSmooth);

        // La cámara siempre mira al coche con el ángulo de inclinación deseado
        Quaternion targetRot = Quaternion.Euler(tiltAngle, _currentYaw, 0f);
        transform.rotation = Quaternion.Lerp(transform.rotation, targetRot,
                                             Time.deltaTime * rotateSmooth);
    }
}