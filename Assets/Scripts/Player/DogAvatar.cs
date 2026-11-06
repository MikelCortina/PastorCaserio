using UnityEngine;

/// <summary>
/// Convierte al jugador en un perro pastor (estilo border collie low-poly) y lo anima por código:
///  · Siempre va corriendo: galope (patas por parejas) en cuanto se mueve; solo trota al arrancar/frenar.
///  · Rebote y cabeceo del cuerpo, inclinación en las curvas.
///  · Cola que menea (mucho cuando está parado, estirada al correr) y orejas que rebotan.
/// El modelo se construye con primitivas como hijos del jugador; desde el Inspector
/// ("Construir perro") queda guardado en la escena. Si falta, se construye al empezar la partida.
/// </summary>
[DisallowMultipleComponent]
public class DogAvatar : MonoBehaviour
{
    [Header("Movimiento del perro (se aplica a PlayerMovement)")]
    public bool applyMovementTuning = true;
    [Tooltip("Velocidad de carrera (m/s). 9.5 > 8 m/s de una oveja huyendo, así siempre puedes adelantarlas")]
    public float runSpeed = 9.5f;
    public float acceleration = 26f;
    public float deceleration = 32f;
    public float rotationSpeed = 14f;
    public float jumpHeight = 0.9f;

    [Header("Animación")]
    [Tooltip("Metros que avanza el perro por cada ciclo de patas al trote")]
    public float trotStride = 1.0f;
    [Tooltip("Metros por ciclo al galope")]
    public float gallopStride = 2.2f;
    [Tooltip("Ángulo máximo de las patas (grados)")]
    public float trotLegAngle = 32f;
    public float gallopLegAngle = 48f;

    [Header("Materiales")]
    public Material furDark;
    public Material furLight;
    public Material noseMat;

    [Header("Piezas (las rellena 'Construir perro')")]
    public Transform model;
    public Transform body;
    public Transform head;
    public Transform tail;
    public Transform earL, earR;
    public Transform legFL, legFR, legBL, legBR;

    private Rigidbody _rb;
    private PlayerMovement _move;
    private float _phase;
    private float _gallopBlend;
    private float _lastYaw;
    private float _lean;
    private float _speedSmoothed;

    // Pose base
    private Vector3 _bodyBasePos;
    private Quaternion _tailBase, _headBase, _earLBase, _earRBase;

    // ───────────────────────── Construcción ─────────────────────────

    public bool IsBuilt => model != null && body != null && legFL != null;

    /// <summary>Crea el modelo del perro como hijo del jugador y ajusta collider y render del cubo.</summary>
    public void Build()
    {
        if (model != null) DestroySafe(model.gameObject);

        Material dark = furDark != null ? furDark : RuntimeMat(new Color(0.08f, 0.08f, 0.09f));
        Material light = furLight != null ? furLight : RuntimeMat(new Color(0.93f, 0.92f, 0.88f));
        Material nose = noseMat != null ? noseMat : RuntimeMat(new Color(0.25f, 0.12f, 0.12f));

        // El pivote del jugador está en el centro del antiguo cubo de 1 m: el suelo está en y = -0.5
        model = new GameObject("Perro").transform;
        model.SetParent(transform, false);
        model.localPosition = new Vector3(0f, -0.5f, 0f);

        // Cuerpo
        body = Pivot("Cuerpo", model, new Vector3(0f, 0.47f, 0f));
        Part("Torso", body, new Vector3(0f, 0f, 0f), new Vector3(0.3f, 0.28f, 0.68f), dark);
        Part("Pecho", body, new Vector3(0f, -0.03f, 0.27f), new Vector3(0.28f, 0.26f, 0.2f), light);
        Part("Collar", body, new Vector3(0f, 0.05f, 0.34f), new Vector3(0.32f, 0.2f, 0.06f), light);
        Part("Vientre", body, new Vector3(0f, -0.12f, 0.02f), new Vector3(0.22f, 0.06f, 0.5f), light);

        // Cabeza
        head = Pivot("Cabeza", body, new Vector3(0f, 0.17f, 0.36f));
        Part("Craneo", head, new Vector3(0f, 0.04f, 0.04f), new Vector3(0.24f, 0.22f, 0.24f), dark);
        Part("Lista", head, new Vector3(0f, 0.08f, 0.165f), new Vector3(0.06f, 0.14f, 0.02f), light);
        Part("Hocico", head, new Vector3(0f, -0.02f, 0.2f), new Vector3(0.14f, 0.11f, 0.17f), light);
        Part("Nariz", head, new Vector3(0f, 0.015f, 0.29f), new Vector3(0.06f, 0.05f, 0.03f), nose);
        Part("OjoL", head, new Vector3(-0.07f, 0.07f, 0.16f), new Vector3(0.035f, 0.035f, 0.02f), nose);
        Part("OjoR", head, new Vector3(0.07f, 0.07f, 0.16f), new Vector3(0.035f, 0.035f, 0.02f), nose);
        earL = Pivot("OrejaL", head, new Vector3(-0.08f, 0.14f, 0f));
        earL.localRotation = Quaternion.Euler(-10f, 0f, -18f);
        Part("Oreja", earL, new Vector3(0f, 0.06f, 0f), new Vector3(0.07f, 0.13f, 0.035f), dark);
        earR = Pivot("OrejaR", head, new Vector3(0.08f, 0.14f, 0f));
        earR.localRotation = Quaternion.Euler(-10f, 0f, 18f);
        Part("Oreja", earR, new Vector3(0f, 0.06f, 0f), new Vector3(0.07f, 0.13f, 0.035f), dark);

        // Cola
        tail = Pivot("Cola", body, new Vector3(0f, 0.08f, -0.33f));
        tail.localRotation = Quaternion.Euler(-35f, 0f, 0f);
        Part("Cola", tail, new Vector3(0f, 0f, -0.14f), new Vector3(0.06f, 0.06f, 0.28f), dark);
        Part("Punta", tail, new Vector3(0f, 0f, -0.3f), new Vector3(0.065f, 0.065f, 0.07f), light);

        // Patas (pivote en la cadera, cuelgan hasta el suelo)
        legFL = Leg("PataDelL", new Vector3(-0.1f, 0.38f, 0.25f), light, light);
        legFR = Leg("PataDelR", new Vector3(0.1f, 0.38f, 0.25f), light, light);
        legBL = Leg("PataTrasL", new Vector3(-0.1f, 0.38f, -0.25f), dark, light);
        legBR = Leg("PataTrasR", new Vector3(0.1f, 0.38f, -0.25f), dark, light);

        // Collider ajustado al perro (base en el suelo) y cubo original oculto
        var box = GetComponent<BoxCollider>();
        if (box != null)
        {
            box.size = new Vector3(0.42f, 0.75f, 0.95f);
            box.center = new Vector3(0f, -0.125f, 0f);
        }
        var mr = GetComponent<MeshRenderer>();
        if (mr != null) mr.enabled = false;

        CachePose();
    }

    Transform Leg(string name, Vector3 hip, Material upper, Material paw)
    {
        var p = Pivot(name, model, hip);
        Part("Muslo", p, new Vector3(0f, -0.14f, 0f), new Vector3(0.09f, 0.28f, 0.1f), upper);
        Part("Pata", p, new Vector3(0f, -0.3f, 0.005f), new Vector3(0.075f, 0.12f, 0.08f), paw);
        Part("Pie", p, new Vector3(0f, -0.36f, 0.025f), new Vector3(0.09f, 0.04f, 0.12f), paw);
        return p;
    }

    static Transform Pivot(string name, Transform parent, Vector3 localPos)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.localPosition = localPos;
        return t;
    }

    static void Part(string name, Transform parent, Vector3 localPos, Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        DestroySafe(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
    }

    static void DestroySafe(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
    }

    static Material RuntimeMat(Color c)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var m = new Material(shader) { color = c };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.1f);
        return m;
    }

    void CachePose()
    {
        if (body != null) _bodyBasePos = body.localPosition;
        if (tail != null) _tailBase = tail.localRotation;
        if (head != null) _headBase = head.localRotation;
        if (earL != null) _earLBase = earL.localRotation;
        if (earR != null) _earRBase = earR.localRotation;
    }

    // ───────────────────────── Ciclo de vida ─────────────────────────

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _move = GetComponent<PlayerMovement>();
        if (!Application.isPlaying) return;

        if (!IsBuilt) Build();
        else
        {
            var mr = GetComponent<MeshRenderer>();
            if (mr != null) mr.enabled = false;
        }
        CachePose();

        if (applyMovementTuning && _move != null)
            _move.ApplyTuning(runSpeed, acceleration, deceleration, rotationSpeed, jumpHeight);

        _lastYaw = transform.eulerAngles.y;
    }

    // ───────────────────────── Animación ─────────────────────────

    void LateUpdate()
    {
        if (!IsBuilt) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        Vector3 v = _rb != null ? _rb.linearVelocity : Vector3.zero;
        bool grounded = _rb == null || Mathf.Abs(v.y) < 1.5f;
        v.y = 0f;
        float speed = v.magnitude;
        _speedSmoothed = Mathf.Lerp(_speedSmoothed, speed, 12f * dt);

        float run = _move != null ? _move.MoveSpeed : runSpeed;
        float moving = Mathf.Clamp01(_speedSmoothed / 0.6f);              // 0 parado → 1 moviéndose
        float targetGallop = Mathf.InverseLerp(run * 0.3f, run * 0.6f, _speedSmoothed);
        _gallopBlend = Mathf.MoveTowards(_gallopBlend, targetGallop, 4f * dt);

        // Avance del ciclo de patas proporcional a la distancia recorrida
        float stride = Mathf.Lerp(trotStride, gallopStride, _gallopBlend);
        _phase += (_speedSmoothed / stride) * Mathf.PI * 2f * dt;
        if (moving < 0.05f) _phase = Mathf.Lerp(_phase, Mathf.Round(_phase / Mathf.PI) * Mathf.PI, 6f * dt);

        float amp = Mathf.Lerp(trotLegAngle, gallopLegAngle, _gallopBlend) * moving;
        if (!grounded) amp *= 0.3f;

        // Trote: diagonales (DI+TD / DD+TI). Galope: delanteras juntas, traseras desfasadas
        float p = _phase;
        const float fl = 0f;
        float fr = Mathf.Lerp(Mathf.PI, 0.35f, _gallopBlend);
        float bl = Mathf.Lerp(Mathf.PI, Mathf.PI, _gallopBlend);
        float br = Mathf.Lerp(0f, Mathf.PI + 0.35f, _gallopBlend);
        SetLeg(legFL, Mathf.Sin(p + fl) * amp);
        SetLeg(legFR, Mathf.Sin(p + fr) * amp);
        SetLeg(legBL, Mathf.Sin(p + bl) * amp * 0.9f);
        SetLeg(legBR, Mathf.Sin(p + br) * amp * 0.9f);

        // Rebote del cuerpo (2 por ciclo al trote, 1 grande al galope) y cabeceo al galope
        float trotBob = Mathf.Abs(Mathf.Sin(p)) * 0.035f;
        float gallopBob = (Mathf.Sin(p - 0.6f) * 0.5f + 0.5f) * 0.07f;
        float bob = Mathf.Lerp(trotBob, gallopBob, _gallopBlend) * moving;
        float breathe = Mathf.Sin(Time.time * 2.2f) * 0.006f * (1f - moving);
        float pitch = Mathf.Sin(p) * 7f * _gallopBlend * moving;

        // Inclinación en curvas
        float yaw = transform.eulerAngles.y;
        float yawRate = Mathf.DeltaAngle(_lastYaw, yaw) / dt;
        _lastYaw = yaw;
        _lean = Mathf.Lerp(_lean, Mathf.Clamp(-yawRate * 0.04f * moving, -12f, 12f), 8f * dt);

        body.localPosition = _bodyBasePos + Vector3.up * (bob + breathe);
        body.localRotation = Quaternion.Euler(pitch, 0f, _lean);

        // Cabeza: compensa el cabeceo, baja un poco al correr (postura de pastor) y olfatea parada
        float sniff = (1f - moving) * Mathf.Sin(Time.time * 0.7f) * 12f;
        head.localRotation = _headBase * Quaternion.Euler(-pitch * 0.8f + 10f * moving * (0.5f + _gallopBlend * 0.5f) + Mathf.Sin(p * 2f) * 3f * moving, sniff, 0f);

        // Cola: meneo alegre parado, estirada y con poco meneo al correr
        float wagSpeed = Mathf.Lerp(14f, 8f, moving);
        float wagAmp = Mathf.Lerp(35f, 10f, moving);
        float tailLift = Mathf.Lerp(0f, 25f, moving);
        tail.localRotation = _tailBase * Quaternion.Euler(tailLift, Mathf.Sin(Time.time * wagSpeed) * wagAmp, 0f);

        // Orejas: rebotan con el paso
        float earFlop = bob * 400f + (moving * 10f);
        earL.localRotation = _earLBase * Quaternion.Euler(earFlop, 0f, -bob * 150f);
        earR.localRotation = _earRBase * Quaternion.Euler(earFlop, 0f, bob * 150f);
    }

    static void SetLeg(Transform leg, float angle)
    {
        if (leg != null) leg.localRotation = Quaternion.Euler(angle, 0f, 0f);
    }
}
