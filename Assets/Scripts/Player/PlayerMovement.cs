using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Movimiento estándar del jugador (cubo) con Rigidbody.
/// WASD / flechas para moverse (relativo a la cámara), Espacio para saltar.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Movimiento")]
    [SerializeField] private float moveSpeed = 6f;
    [SerializeField] private float acceleration = 10f;
    [SerializeField] private float deceleration = 14f;
    [SerializeField] private float rotationSpeed = 12f;

    [Header("Salto")]
    [SerializeField] private float jumpHeight = 1.2f;
    [SerializeField] private float groundCheckDistance = 0.15f;
    [SerializeField] private LayerMask groundMask = ~0;

    private Rigidbody rb;
    private Collider col;
    private Transform cam;
    private Vector3 inputDir;
    private Vector3 currentVelocity;
    private bool jumpRequested;

    public float MoveSpeed => moveSpeed;

    /// <summary>Permite que el avatar (p.ej. el perro) imponga su propio ajuste de movimiento.</summary>
    public void ApplyTuning(float speed, float accel, float decel, float rotSpeed, float jump)
    {
        moveSpeed = speed;
        acceleration = accel;
        deceleration = decel;
        rotationSpeed = rotSpeed;
        jumpHeight = jump;
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        col = GetComponent<Collider>();
        rb.constraints = RigidbodyConstraints.FreezeRotation;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        if (Camera.main != null) cam = Camera.main.transform;
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) { inputDir = Vector3.zero; return; }

        float h = 0f, v = 0f;
        if (kb.wKey.isPressed || kb.upArrowKey.isPressed) v += 1f;
        if (kb.sKey.isPressed || kb.downArrowKey.isPressed) v -= 1f;
        if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) h += 1f;
        if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) h -= 1f;

        // Dirección relativa a la cámara, proyectada en el plano XZ
        Vector3 forward = Vector3.forward, right = Vector3.right;
        if (cam != null)
        {
            forward = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
            right = Vector3.ProjectOnPlane(cam.right, Vector3.up).normalized;
        }
        inputDir = Vector3.ClampMagnitude(forward * v + right * h, 1f);

        if (kb.spaceKey.wasPressedThisFrame) jumpRequested = true;
    }

    void FixedUpdate()
    {
        Move();
        Rotate();
        Jump();
    }

    void Move()
    {
        float speed = moveSpeed; // sin esprint: siempre a la misma velocidad de carrera
        Vector3 targetVelocity = inputDir * speed;
        float lerp = inputDir.sqrMagnitude > 0.001f ? acceleration : deceleration;
        currentVelocity = Vector3.Lerp(currentVelocity, targetVelocity, Time.fixedDeltaTime * lerp);
        rb.linearVelocity = new Vector3(currentVelocity.x, rb.linearVelocity.y, currentVelocity.z);
    }

    void Rotate()
    {
        if (inputDir.sqrMagnitude < 0.01f) return;
        Quaternion targetRot = Quaternion.LookRotation(inputDir, Vector3.up);
        rb.MoveRotation(Quaternion.Slerp(rb.rotation, targetRot, Time.fixedDeltaTime * rotationSpeed));
    }

    void Jump()
    {
        if (!jumpRequested) return;
        jumpRequested = false;
        if (!IsGrounded()) return;

        float jumpVel = Mathf.Sqrt(2f * Mathf.Abs(Physics.gravity.y) * jumpHeight);
        rb.linearVelocity = new Vector3(rb.linearVelocity.x, jumpVel, rb.linearVelocity.z);
    }

    bool IsGrounded()
    {
        Bounds b = col != null ? col.bounds : new Bounds(transform.position, Vector3.one);
        Vector3 origin = new Vector3(b.center.x, b.min.y + 0.05f, b.center.z);
        foreach (var hit in Physics.RaycastAll(origin, Vector3.down, groundCheckDistance + 0.05f, groundMask, QueryTriggerInteraction.Ignore))
            if (hit.transform != transform && !hit.transform.IsChildOf(transform)) return true;
        return false;
    }
}
