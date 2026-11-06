using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Cañón de flores tipo tirachinas: arrastra con el ratón (izq/der) y suelta para disparar.
/// Todas las flores vuelan durante el MISMO tiempo y la velocidad de cada una se calcula
/// analíticamente para que caiga exactamente en su punto dentro de un círculo de radio
/// <see cref="landingRadius"/> alrededor del punto de impacto central.
/// </summary>
public class FlowerCannon : MonoBehaviour
{
    [Header("Referencias")]
    public Transform spawnPoint;
    public LineRenderer trajectoryLine;

    [Header("Flor tipo 1 (click izquierdo)")]
    public GameObject flowerProjectilePrefab1;
    public Color previewTrailColor1 = new Color(1f, 0.6f, 0.8f, 0.4f);

    [Header("Zarzas (click derecho)")]
    public GameObject flowerProjectilePrefab2;
    public Color previewTrailColor2 = new Color(0.4f, 0.8f, 1f, 0.4f);

    [Header("Configuración de disparo")]
    public int flowerCount = 5;
    [Tooltip("Radio del círculo en el que caen las flores alrededor del punto central")]
    public float landingRadius = 2f;
    public float maxForce = 30f;
    public float minForce = 5f;
    public float maxDragDistance = 200f;
    [Tooltip("Altura extra sobre el spawnPoint desde la que salen las flores")]
    public float spawnHeightOffset = 0.3f;

    [Header("Barrera de zarzas (click derecho)")]
    [Tooltip("Nº de tramos de zarza que forman la barrera")]
    public int brambleCount = 7;
    [Tooltip("Longitud total de la barrera (perpendicular a la dirección del disparo)")]
    public float barrierLength = 7f;
    [Tooltip("Segundos que dura la barrera (0 = permanente)")]
    public float brambleLifetime = 12f;

    [Header("Munición (para que no se pueda usar a lo loco)")]
    public int roseMaxCharges = 3;
    [Tooltip("Segundos para recuperar una carga de rosas")]
    public float roseRechargeTime = 5f;
    public int brambleMaxCharges = 2;
    [Tooltip("Segundos para recuperar una carga de zarzas")]
    public float brambleRechargeTime = 9f;
    [Tooltip("Tiempo mínimo entre dos disparos")]
    public float shotCooldown = 0.6f;

    public int RoseCharges { get; private set; }
    public int BrambleCharges { get; private set; }
    /// <summary>Progreso 0..1 de la siguiente carga.</summary>
    public float RoseRecharge01 => RoseCharges >= roseMaxCharges ? 1f : _roseTimer / roseRechargeTime;
    public float BrambleRecharge01 => BrambleCharges >= brambleMaxCharges ? 1f : _brambleTimer / brambleRechargeTime;
    private float _roseTimer, _brambleTimer, _cooldownTimer;

    [Header("Física")]
    public float gravityScale = 0.3f;

    [Header("Preview trails")]
    public float previewTrailWidth = 0.04f;
    public int previewSegments = 40;

    private struct Shot
    {
        public Vector3 velocity;
        public Vector3 target;
        public Quaternion rotation;
    }

    private Vector2 dragStartPos;
    private bool isDragging;
    private float currentForce;
    private Vector3 currentLaunchDir;
    private int currentMouseButton;

    private readonly List<LineRenderer> previewLines = new List<LineRenderer>();
    private readonly List<Shot> shots = new List<Shot>();
    private Vector3 mainVelocity;
    private int currentMode;
    private float flightTime;
    private Transform shooterRoot;

    Vector3 Gravity => Physics.gravity * gravityScale;
    Vector3 LaunchPos => spawnPoint.position + Vector3.up * spawnHeightOffset;

    void Start()
    {
        if (spawnPoint == null) spawnPoint = transform;
        RoseCharges = roseMaxCharges;
        BrambleCharges = brambleMaxCharges;
        shooterRoot = transform.root;

        var mat = new Material(Shader.Find("Sprites/Default"));
        int lines = Mathf.Max(flowerCount, brambleCount);
        for (int i = 0; i < lines; i++)
        {
            var go = new GameObject($"PreviewLine_{i}");
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = mat;
            lr.startWidth = previewTrailWidth;
            lr.endWidth = 0f;
            lr.positionCount = 0;
            lr.useWorldSpace = true;
            previewLines.Add(lr);
        }
    }

    void Update()
    {
        var mouse = Mouse.current;
        if (mouse == null) { IsAiming = false; return; }
        bool shift = ShiftHeld();

        // Para lanzar hay que mantener Shift + click (izq = rosas, der = zarzas)
        Recharge(Time.deltaTime);

        if (!isDragging && shift && _cooldownTimer <= 0f)
        {
            if (mouse.leftButton.wasPressedThisFrame && RoseCharges > 0) StartDrag(0, mouse);
            else if (mouse.rightButton.wasPressedThisFrame && BrambleCharges > 0) StartDrag(1, mouse);
        }
        IsAiming = isDragging;
        if (!isDragging) return;

        var button = currentMouseButton == 0 ? mouse.leftButton : mouse.rightButton;

        // Soltar Shift antes que el ratón cancela el disparo
        if (!shift)
        {
            isDragging = false;
            IsAiming = false;
            ClearPreview();
            currentForce = 0f;
            return;
        }

        if (button.isPressed)
        {
            Vector2 dragDelta = mouse.position.ReadValue() - dragStartPos;
            float t = Mathf.Clamp(dragDelta.magnitude, 0f, maxDragDistance) / maxDragDistance;
            currentForce = Mathf.Lerp(minForce, maxForce, t);
            currentLaunchDir = GetSlingshotDirection(dragDelta);

            ComputeShots(currentLaunchDir, currentForce, currentMouseButton);
            UpdatePreviewColors(currentMouseButton);
            DrawPreview();
        }
        else
        {
            isDragging = false;
            ClearPreview();

            if (currentForce > minForce * 0.5f)
            {
                ComputeShots(currentLaunchDir, currentForce, currentMouseButton);
                FireFlowers(currentMouseButton);
                if (currentMouseButton == 0) RoseCharges--; else BrambleCharges--;
                _cooldownTimer = shotCooldown;
            }
            currentForce = 0f;
        }
    }

    /// <summary>True mientras el jugador está apuntando (Shift + click mantenidos).</summary>
    public static bool IsAiming { get; private set; }

    static bool ShiftHeld()
    {
        var kb = Keyboard.current;
        return kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);
    }

    void OnDisable()
    {
        IsAiming = false;
        isDragging = false;
        ClearPreview();
    }

    void Recharge(float dt)
    {
        _cooldownTimer -= dt;
        if (RoseCharges < roseMaxCharges && (_roseTimer += dt) >= roseRechargeTime) { RoseCharges++; _roseTimer = 0f; }
        if (BrambleCharges < brambleMaxCharges && (_brambleTimer += dt) >= brambleRechargeTime) { BrambleCharges++; _brambleTimer = 0f; }
    }

    void StartDrag(int mouseButton, Mouse mouse)
    {
        dragStartPos = mouse.position.ReadValue();
        isDragging = true;
        currentMouseButton = mouseButton;
    }

    Vector3 GetSlingshotDirection(Vector2 dragDelta)
    {
        Vector2 inv = -dragDelta.normalized;
        Transform cam = Camera.main.transform;
        Vector3 right = Vector3.ProjectOnPlane(cam.right, Vector3.up).normalized;
        Vector3 forward = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
        return (right * inv.x + forward * inv.y).normalized;
    }

    // ───────────────────────── Cálculo de disparos ─────────────────────────

    /// <summary>
    /// Calcula el punto de impacto central y su tiempo de vuelo T; después, para cada flor,
    /// la velocidad exacta para llegar a su punto del círculo en ese mismo tiempo T.
    /// </summary>
    void ComputeShots(Vector3 baseDir, float force, int mode)
    {
        shots.Clear();
        currentMode = mode;
        Vector3 start = LaunchPos;
        Vector3 g = Gravity;

        mainVelocity = baseDir * force;
        GetLanding(start, mainVelocity, g, out Vector3 center, out flightTime);
        flightTime = Mathf.Max(flightTime, 0.05f);
        float T = flightTime;

        bool barrier = mode == 1;
        int count = barrier ? brambleCount : flowerCount;
        Vector3 fwd = Vector3.ProjectOnPlane(baseDir, Vector3.up);
        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
        fwd.Normalize();
        Vector3 side = Vector3.Cross(Vector3.up, fwd); // eje de la barrera
        Quaternion barrierRot = Quaternion.LookRotation(fwd, Vector3.up); // transform.right == side

        for (int i = 0; i < count; i++)
        {
            Vector3 p = barrier ? GetBarrierPoint(center, side, i, count) : GetCirclePoint(center, i);
            Vector3 target = SnapToGround(p, center.y);
            // p(T) = start + v·T + ½·g·T²  →  v = (target − start − ½·g·T²) / T
            Vector3 v = (target - start - 0.5f * T * T * g) / T;
            shots.Add(new Shot { velocity = v, target = target, rotation = barrier ? barrierRot : Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) });
        }
    }

    void GetLanding(Vector3 start, Vector3 velocity, Vector3 g, out Vector3 point, out float time)
    {
        const float dt = 0.02f;
        const int maxSteps = 1000;
        Vector3 pos = start;
        for (int i = 0; i < maxSteps; i++)
        {
            float t1 = (i + 1) * dt;
            Vector3 next = start + velocity * t1 + 0.5f * t1 * t1 * g;
            Vector3 seg = next - pos;
            float len = seg.magnitude;
            if (Cast(pos, seg, len, out RaycastHit hit))
            {
                point = hit.point;
                time = i * dt + dt * (hit.distance / Mathf.Max(len, 1e-5f));
                return;
            }
            pos = next;
        }
        point = pos;
        time = maxSteps * dt;
    }

    Vector3 SnapToGround(Vector3 p, float referenceY)
    {
        Vector3 from = new Vector3(p.x, referenceY + 20f, p.z);
        if (Cast(from, Vector3.down, 60f, out RaycastHit hit)) return hit.point;
        return new Vector3(p.x, referenceY, p.z);
    }

    /// <summary>Raycast que ignora al propio jugador/cañón, a los proyectiles y a los triggers.</summary>
    bool Cast(Vector3 origin, Vector3 dir, float dist, out RaycastHit best)
    {
        best = default;
        if (dist <= 1e-5f) return false;
        var hits = Physics.RaycastAll(origin, dir.normalized, dist, ~0, QueryTriggerInteraction.Ignore);
        float bestDist = float.MaxValue;
        bool found = false;
        foreach (var h in hits)
        {
            if (shooterRoot != null && h.transform.IsChildOf(shooterRoot)) continue;
            if (h.collider.GetComponentInParent<FlowerProjectile>() != null) continue;
            if (h.distance < bestDist) { bestDist = h.distance; best = h; found = true; }
        }
        return found;
    }

    /// <summary>Puntos equiespaciados en una línea centrada en el impacto: forman una barrera continua.</summary>
    Vector3 GetBarrierPoint(Vector3 center, Vector3 side, int index, int count)
    {
        if (count <= 1) return center;
        float t = index / (float)(count - 1) - 0.5f;
        return center + side * (t * barrierLength);
    }

    Vector3 GetCirclePoint(Vector3 center, int index)
    {
        if (flowerCount <= 1) return center;
        // Distribución "girasol" (ángulo áureo): reparto uniforme dentro del círculo
        float angle = index * 137.508f * Mathf.Deg2Rad;
        float r = landingRadius * Mathf.Sqrt((index + 0.5f) / flowerCount);
        return center + new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r);
    }

    // ───────────────────────── Disparo ─────────────────────────

    void FireFlowers(int mouseButton)
    {
        GameObject prefab = mouseButton == 0 ? flowerProjectilePrefab1 : flowerProjectilePrefab2;
        if (prefab == null) return;

        Vector3 start = LaunchPos;
        foreach (var s in shots)
        {
            GameObject go = Instantiate(prefab, start, Quaternion.identity);
            var fp = go.GetComponent<FlowerProjectile>();
            if (fp == null) fp = go.AddComponent<FlowerProjectile>();
            fp.gravityScale = gravityScale;
            fp.Launch(start, s.velocity, Gravity, flightTime, s.target, shooterRoot);
            if (mouseButton == 1)
            {
                // Cada tramo un poco más largo que el hueco entre tramos → barrera sin agujeros
                float segment = brambleCount > 1 ? barrierLength / (brambleCount - 1) : barrierLength;
                fp.SetAsBramble(s.rotation, segment * 1.25f, brambleLifetime);
            }
        }
    }

    // ───────────────────────── Preview ─────────────────────────

    void UpdatePreviewColors(int mouseButton)
    {
        Color c = mouseButton == 0 ? previewTrailColor1 : previewTrailColor2;
        Color fade = new Color(c.r, c.g, c.b, 0f);
        if (trajectoryLine != null) { trajectoryLine.startColor = c; trajectoryLine.endColor = fade; }
        foreach (var lr in previewLines) { lr.startColor = c; lr.endColor = fade; }
    }

    void DrawPreview()
    {
        Vector3 start = LaunchPos;
        Vector3 g = Gravity;

        if (trajectoryLine != null) SetArc(trajectoryLine, start, mainVelocity, g, flightTime);

        for (int i = 0; i < previewLines.Count; i++)
        {
            if (i < shots.Count) SetArc(previewLines[i], start, shots[i].velocity, g, flightTime);
            else previewLines[i].positionCount = 0;
        }
    }

    void SetArc(LineRenderer lr, Vector3 start, Vector3 v, Vector3 g, float T)
    {
        int n = Mathf.Max(2, previewSegments);
        lr.positionCount = n + 1;
        for (int k = 0; k <= n; k++)
        {
            float t = T * k / n;
            lr.SetPosition(k, start + v * t + 0.5f * t * t * g);
        }
    }

    void ClearPreview()
    {
        if (trajectoryLine != null) trajectoryLine.positionCount = 0;
        foreach (var lr in previewLines) lr.positionCount = 0;
    }
}
