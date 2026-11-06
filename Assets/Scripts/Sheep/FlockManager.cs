using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Gestor del rebaño: genera las ovejas, guarda la referencia al jugador (el "perro pastor")
/// y centraliza todos los parámetros de comportamiento para poder ajustarlos desde un solo sitio.
///
/// Modelo de pastoreo (inspirado en Strömbom et al. 2014 + zonas de fuga de Grandin):
///  · Zona de alerta: la oveja levanta la cabeza y mira al jugador.
///  · Zona de fuga:   la oveja se aleja del jugador y busca el centro de sus vecinas (rebaño egoísta).
///  · El miedo se contagia entre vecinas, así que empujar un flanco mueve a todo el grupo.
///  · Las ovejas tranquilas pastan, se acercan a comer rosas y siguen a las que van a comer.
///  · Las zarzas (NavMeshObstacle con carving) no se pueden atravesar.
/// </summary>
public class FlockManager : MonoBehaviour
{
    [Header("Spawn")]
    public GameObject sheepPrefab;
    public int flockSize = 20;
    [Tooltip("Radio alrededor del FlockManager donde aparecen las ovejas")]
    public float spawnRadius = 6f;
    [Tooltip("Escala aplicada a cada oveja (con una variación aleatoria de ±scaleVariation)")]
    public float sheepScale = 0.35f;
    [Range(0f, 0.5f)] public float scaleVariation = 0.12f;

    [Header("Velocidades (m/s)")]
    public float walkSpeed = 1.6f;
    public float trotSpeed = 4.5f;
    public float runSpeed = 8f;
    [Tooltip("Aceleración máxima (m/s²)")]
    public float acceleration = 22f;
    [Tooltip("Velocidad de giro (grados/s)")]
    public float turnSpeed = 600f;

    [Header("Presión del jugador")]
    [Tooltip("Dentro de este radio la oveja huye")]
    public float flightZoneRadius = 9f;
    [Tooltip("Dentro de este radio la oveja se pone alerta y mira al jugador")]
    public float alertRadius = 15f;
    [Tooltip("Cuánto crece la zona de fuga si el jugador corre hacia la oveja (por m/s)")]
    public float approachSpeedBonus = 0.5f;
    [Tooltip("Velocidad del jugador a partir de la cual se considera que 'carga'")]
    public float playerChargeSpeed = 12f;

    [Header("Miedo")]
    [Tooltip("Velocidad a la que sube el miedo (por segundo)")]
    public float fearRise = 8f;
    [Tooltip("Velocidad a la que baja el miedo (por segundo)")]
    public float fearDecay = 0.55f;
    [Tooltip("Fracción del miedo medio de las vecinas que se contagia")]
    [Range(0f, 1f)] public float fearContagion = 0.7f;
    [Tooltip("Por debajo de este miedo la oveja vuelve a pastar/comer")]
    [Range(0f, 1f)] public float calmThreshold = 0.12f;

    [Header("Vecinas")]
    public float neighborRadius = 9f;
    [Tooltip("Nº de vecinas más cercanas usadas para el centro local (Strömbom)")]
    public int nearestNeighbors = 7;
    [Tooltip("Distancia mínima entre ovejas en múltiplos de su radio corporal")]
    public float personalSpaceFactor = 2.2f;

    [Header("Pesos de steering")]
    public float fleeWeight = 2.6f;
    public float cohesionCalmWeight = 0.25f;
    public float cohesionAfraidWeight = 1.5f;
    public float separationWeight = 3f;
    public float alignmentWeight = 0.9f;
    public float obstacleWeight = 5f;
    [Tooltip("Distancia de anticipación a bordes del NavMesh y zarzas")]
    public float obstacleLookAhead = 2.5f;

    [Header("Pastar")]
    public float wanderRadius = 5f;
    public float grazeTimeMin = 2.5f;
    public float grazeTimeMax = 5f;
    [Tooltip("Si una oveja tranquila se aleja más que esto del rebaño, vuelve")]
    public float maxCalmSpread = 10f;

    [Header("Rosas")]
    [Tooltip("Por encima de este miedo las rosas se ignoran")]
    [Range(0f, 1f)] public float roseMaxFear = 0.35f;
    [Tooltip("Las ovejas siguen a vecinas que van a comer dentro de este radio")]
    public float followEaterRadius = 10f;

    public Vector3 FlockCentroid { get; private set; }
    public float FlockSpreadRadius { get; private set; }
    public Vector3 FlockVelocity { get; private set; }

    [HideInInspector] public Transform playerTransform;
    [HideInInspector] public List<SheepAgent> allSheep = new List<SheepAgent>();

    public Vector3 PlayerVelocity { get; private set; }
    private Rigidbody _playerRb;
    private Vector3 _lastPlayerPos;

    public void SetPlayerTarget(Transform newTarget)
    {
        playerTransform = newTarget;
        _playerRb = newTarget != null ? newTarget.GetComponent<Rigidbody>() : null;
        if (newTarget != null) _lastPlayerPos = newTarget.position;
    }

    void Start()
    {
        var player = GameObject.FindGameObjectWithTag("Player");
        if (player == null) Debug.LogError("[FlockManager] No hay ningún objeto con tag 'Player'.");
        else SetPlayerTarget(player.transform);

        SpawnFlock();
    }

    void SpawnFlock()
    {
        if (sheepPrefab == null) { Debug.LogError("[FlockManager] Falta sheepPrefab."); return; }

        int spawned = 0, attempts = 0;
        while (spawned < flockSize && attempts < flockSize * 10)
        {
            attempts++;
            Vector2 r = Random.insideUnitCircle * spawnRadius;
            Vector3 raw = transform.position + new Vector3(r.x, 0f, r.y);
            if (!NavMesh.SamplePosition(raw, out NavMeshHit hit, 5f, NavMesh.AllAreas)) continue;

            var go = Instantiate(sheepPrefab, hit.position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), transform);
            go.name = $"Sheep_{spawned:00}";
            go.transform.localScale = Vector3.one * sheepScale * Random.Range(1f - scaleVariation, 1f + scaleVariation);

            var sheep = go.GetComponent<SheepAgent>();
            if (sheep == null) sheep = go.AddComponent<SheepAgent>();
            if (go.GetComponent<SheepDebugInfo>() == null) go.AddComponent<SheepDebugInfo>();
            allSheep.Add(sheep);
            sheep.Init(this);
            spawned++;
        }

        if (spawned < flockSize)
            Debug.LogWarning($"[FlockManager] Solo aparecieron {spawned}/{flockSize} ovejas. Mueve el FlockManager a una zona con NavMesh.");
    }

    void Update()
    {
        allSheep.RemoveAll(s => s == null);

        if (playerTransform != null)
        {
            if (_playerRb != null) PlayerVelocity = _playerRb.linearVelocity;
            else if (Time.deltaTime > 0f) PlayerVelocity = (playerTransform.position - _lastPlayerPos) / Time.deltaTime;
            _lastPlayerPos = playerTransform.position;
        }

        if (allSheep.Count == 0) return;

        Vector3 sum = Vector3.zero, vel = Vector3.zero;
        foreach (var s in allSheep) { sum += s.transform.position; vel += s.Velocity; }
        FlockCentroid = sum / allSheep.Count;
        FlockVelocity = vel / allSheep.Count;

        float spread = 0f;
        foreach (var s in allSheep) spread += Vector3.Distance(s.transform.position, FlockCentroid);
        FlockSpreadRadius = spread / allSheep.Count;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.3f, 1f, 0.3f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, spawnRadius);
        if (!Application.isPlaying) return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(FlockCentroid, 0.4f);
        Gizmos.DrawWireSphere(FlockCentroid, FlockSpreadRadius);
        if (playerTransform != null)
        {
            Gizmos.color = new Color(1f, 0f, 0f, 0.6f);
            Gizmos.DrawWireSphere(playerTransform.position, flightZoneRadius);
            Gizmos.color = new Color(1f, 0.6f, 0f, 0.4f);
            Gizmos.DrawWireSphere(playerTransform.position, alertRadius);
        }
    }
}
