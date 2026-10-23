using UnityEngine;

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

    [HideInInspector] public float gravityScale = 0.3f;

    private bool hasLanded = false;
    private TrailRenderer trail;
    private Rigidbody rb;
    private float spawnTime;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        spawnTime = Time.time;

        trail = gameObject.AddComponent<TrailRenderer>();
        trail.time = trailTime;
        trail.startWidth = trailStartWidth;
        trail.endWidth = 0f;
        trail.minVertexDistance = 0.05f;
        trail.material = new Material(Shader.Find("Sprites/Default"));
        trail.startColor = trailStartColor;
        trail.endColor = trailEndColor;

        Destroy(gameObject, 8f);
    }

    void FixedUpdate()
    {
        if (!hasLanded)
            rb.AddForce(Physics.gravity * gravityScale, ForceMode.Acceleration);
    }

    void OnCollisionEnter(Collision collision)
    {
        if (Time.time - spawnTime < 0.1f) return;
        if (hasLanded) return;
        hasLanded = true;

        Vector3 spawnPos;
        if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, 5f))
            spawnPos = hit.point;
        else
            spawnPos = collision.contacts[0].point;

        if (flowerVisualPrefab != null)
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
            fs.SetAsFlower(flowerConsumeTime); // se destruye tras 5s con oveja encima

            // Registrar en FlockManager
            FlockManager fm = Object.FindFirstObjectByType<FlockManager>();
            if (fm != null) fm.foodSources.Add(fs);
        }

        if (trail != null)
        {
            trail.transform.SetParent(null);
            Destroy(trail.gameObject, trailTime + 0.1f);
        }

        Destroy(gameObject);
    }
}