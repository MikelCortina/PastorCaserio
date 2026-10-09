using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class CarController : MonoBehaviour
{
    [Header("Movimiento")]
    public float moveSpeed = 10f;
    public float rotateSpeed = 8f;   // Velocidad de giro visual del modelo
    public float acceleration = 8f;
    public float deceleration = 12f;

    private Rigidbody _rb;
    private Vector3 _inputDir;
    private Vector3 _currentVelocity;

    void Start()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.constraints = RigidbodyConstraints.FreezeRotationX
                        | RigidbodyConstraints.FreezeRotationZ
                        | RigidbodyConstraints.FreezePositionY;
    }

    void Update()
    {
        // Dirección en espacio mundo (cámara siempre al norte)
        float h = Input.GetAxisRaw("Horizontal"); // A/D
        float v = Input.GetAxisRaw("Vertical");   // W/S
        _inputDir = new Vector3(h, 0f, v).normalized;
    }

    void FixedUpdate()
    {
        Move();
        RotateTowardMovement();
 
    }

    void Move()
    {
        Vector3 targetVelocity = _inputDir * moveSpeed;
        float lerpSpeed = _inputDir.magnitude > 0 ? acceleration : deceleration;

        _currentVelocity = Vector3.Lerp(_currentVelocity, targetVelocity,
                                         Time.fixedDeltaTime * lerpSpeed);
        _rb.linearVelocity = new Vector3(_currentVelocity.x, _rb.linearVelocity.y,
                                          _currentVelocity.z);
    }

    void RotateTowardMovement()
    {
        if (_inputDir.sqrMagnitude < 0.01f) return;

        // El modelo rota hacia la dirección de movimiento
        Quaternion targetRot = Quaternion.LookRotation(_inputDir, Vector3.up);
        _rb.rotation = Quaternion.Slerp(_rb.rotation, targetRot,
                                         Time.fixedDeltaTime * rotateSpeed);
    }
}