using UnityEngine;

/// <summary>
/// Proyectil de flor. Se mueve de forma cinemática siguiendo una parábola exacta
/// (sin colisiones físicas entre proyectiles, que eran las que los dispersaban)
/// y al aterrizar genera la flor visual + FoodSource que atrae a las ovejas.
/// </summary>
public class FlowerProjectile : MonoBehaviour
{
    [Header("Flor visual al impacto")]
    public GameObject flowerVisualPrefab;

    [Header("Trail")]
    public Color trailStartColor = new Color(1f, 0.4f, 0.7f, 1f);
    public Color trailEndColor = new Color(1f, 0.8f, 0.2f, 0f);
    public float trailTime = 0.4f;
    public float trailStartWidth = 0.08f;

    [Header("Atracción de ovejas")]
    [Tooltip("Radio de detección de la FoodSource que genera la flor al aterrizar")]
    public float flowerAttractionRadius = 10f;
    [Tooltip("Cuántas ovejas puede atraer esta flor a la vez")]
    public int flowerMaxOccupants = 8;
    [Tooltip("Prioridad de atracción (mayor que comederos fijos para guiar al rebaño)")]
    public float flowerAttractionPriority = 3f;
    [Tooltip("Segundos que una oveja debe estar encima para consumir la flor")]
    public float flowerConsumeTime = 5f;
    [Tooltip("Segundos que dura la rosa en el suelo si nadie se la come")]
    public float roseLifetime = 15f;

    [HideInInspector] public float gravityScale = 0.3f;

    // Zarza (click derecho)
    private bool isBramble;
    private Quaternion brambleRotation = Quaternion.identity;
    private float brambleLength = 1.2f;
    private float brambleLifetime = 30f;

    /// <summary>Convierte este proyectil en un tramo de barrera de zarza al aterrizar.</summary>
    public void SetAsBramble(Quaternion rotation, float length, float lifetime)
    {
        isBramble = true;
        brambleRotation = rotation;
        brambleLength = length;
        brambleLifetime = lifetime;
    }

    private Vector3 startPos;
    private Vector3 velocity;
    private Vector3 gravity;
    private float flightTime;
    private Vector3 landingPoint;
    private Transform ignoreRoot;
    private float t;
    private bool launched;
    private bool hasLanded;
    private TrailRenderer trail;

    /// <summary>Lanza el proyectil por la parábola p(t) = start + v·t + ½·g·t².</summary>
    public void Launch(Vector3 start, Vector3 initialVelocity, Vector3 g, float time, Vector3 target, Transform ignore)
    {
        startPos = start;
        velocity = initialVelocity;
        gravity = g;
        flightTime = time;
        landingPoint = target;
        ignoreRoot = ignore;
        t = 0f;
        launched = true;
        transform.position = start;
    }

    void Awake()
    {
        // Movimiento 100% cinemático: los proyectiles no se empujan entre sí ni con nada.
        var rb = GetComponent<Rigidbody>();
        if (rb != null) { rb.useGravity = false; rb.isKinematic = true; }
        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
    }

    void Start()
    {
        trail = gameObject.AddComponent<TrailRenderer>();
        trail.time = trailTime;
        trail.startWidth = trailStartWidth;
        trail.endWidth = 0f;
        trail.minVertexDistance = 0.05f;
        trail.material = new Material(Shader.Find("Sprites/Default"));
        trail.startColor = trailStartColor;
        trail.endColor = trailEndColor;

        Destroy(gameObject, Mathf.Max(8f, flightTime + 2f));
    }

    void Update()
    {
        if (!launched || hasLanded) return;

        Vector3 prev = transform.position;
        t += Time.deltaTime;

        if (t >= flightTime)
        {
            transform.position = landingPoint;
            Land(landingPoint);
            return;
        }

        Vector3 next = startPos + velocity * t + 0.5f * t * t * gravity;

        // Si algo se interpone antes de llegar (árbol, roca...), aterriza ahí.
        if (Cast(prev, next, out RaycastHit hit))
        {
            transform.position = hit.point;
            Land(hit.point);
            return;
        }
        transform.position = next;
    }

    bool Cast(Vector3 from, Vector3 to, out RaycastHit best)
    {
        best = default;
        Vector3 d = to - from;
        float len = d.magnitude;
        if (len < 1e-5f) return false;
        bool found = false;
        float bestDist = float.MaxValue;
        foreach (var h in Physics.RaycastAll(from, d / len, len, ~0, QueryTriggerInteraction.Ignore))
        {
            if (ignoreRoot != null && h.transform.IsChildOf(ignoreRoot)) continue;
            if (h.collider.GetComponentInParent<FlowerProjectile>() != null) continue;
            if (h.collider.GetComponentInParent<SheepAgent>() != null) continue;
            if (h.distance < bestDist) { bestDist = h.distance; best = h; found = true; }
        }
        return found;
    }

    void SpawnBramble(Vector3 pos)
    {
        var root = new GameObject("Bramble");
        root.transform.SetPositionAndRotation(pos, brambleRotation);
        var bramble = root.AddComponent<Bramble>();
        bramble.length = brambleLength;
        bramble.lifetime = brambleLifetime;

        if (flowerVisualPrefab != null)
        {
            // Dos matas por tramo para que la barrera se vea tupida
            for (int k = 0; k < 2; k++)
            {
                float offset = (k - 0.5f) * brambleLength * 0.5f;
                var v = Instantiate(flowerVisualPrefab, root.transform);
                v.transform.localPosition = new Vector3(offset, 0f, Random.Range(-0.15f, 0.15f));
                v.transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                foreach (var c in v.GetComponentsInChildren<Collider>()) c.enabled = false;
                var grow = v.GetComponent<FlowerGrow>();
                if (grow == null) grow = v.AddComponent<FlowerGrow>();
                grow.lifetime = 99999f;
            }
        }
    }

    void Land(Vector3 point)
    {
        if (hasLanded) return;
        hasLanded = true;

        Vector3 spawnPos = point;
        if (Physics.Raycast(point + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 5f, ~0, QueryTriggerInteraction.Ignore)
            && (ignoreRoot == null || !hit.transform.IsChildOf(ignoreRoot)))
            spawnPos = hit.point;

        if (isBramble)
        {
            SpawnBramble(spawnPos);
        }
        else if (flowerVisualPrefab != null)
        {
            GameObject flowerVisual = Instantiate(flowerVisualPrefab, spawnPos, Quaternion.identity);

            // FlowerGrow: solo la animación de crecimiento, sin lifetime automático
            FlowerGrow grow = flowerVisual.GetComponent<FlowerGrow>();
            if (grow == null) grow = flowerVisual.AddComponent<FlowerGrow>();
            grow.lifetime = 99999f; // no se destruye por tiempo, solo por consumo

            // FoodSource configurada como flor temporal
            FoodSource fs = flowerVisual.AddComponent<FoodSource>();
            fs.detectionRadius = flowerAttractionRadius;
            fs.maxOccupants = flowerMaxOccupants;
            fs.attractionPriority = flowerAttractionPriority;
            fs.SetAsFlower(flowerConsumeTime);
            fs.lifetime = roseLifetime;

        }

        if (trail != null)
        {
            trail.transform.SetParent(null);
            Destroy(trail.gameObject, trailTime + 0.1f);
        }

        Destroy(gameObject);
    }
}
