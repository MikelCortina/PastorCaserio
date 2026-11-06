using UnityEngine;
using UnityEngine.AI;

public class WolfSpawner : MonoBehaviour
{
    [Header("References")]
    public WolfAI wolfPrefab;
    public FlockManager flockManager;

    [Header("Spawn Points")]
    public Transform[] spawnPoints;

    [Header("Exit Points")]
    public Transform[] exitPoints;

    [Header("Spawn Settings")]
    public bool spawnOnStart = true;
    public float firstSpawnDelay = 5f;
    public bool repeatSpawns = false;
    public float repeatSpawnInterval = 25f;
    public int maxWolvesAlive = 1;

    [Header("Debug")]
    public bool logDebug = true;
    public KeyCode manualSpawnKey = KeyCode.L;

    private int _wolvesAlive;
    private float _spawnTimer;

    private void Start()
    {
        _spawnTimer = firstSpawnDelay;

        if (logDebug)
        {
            Debug.Log("[WolfSpawner] Start correcto. Esperando " + firstSpawnDelay + " segundos.");
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(manualSpawnKey))
        {
            Debug.Log("[WolfSpawner] Spawn manual con tecla L.");
            TrySpawnWolf();
        }

        if (!spawnOnStart)
        {
            return;
        }

        _spawnTimer -= Time.deltaTime;

        if (_spawnTimer <= 0f)
        {
            TrySpawnWolf();

            if (repeatSpawns)
            {
                _spawnTimer = repeatSpawnInterval;
            }
            else
            {
                spawnOnStart = false;
            }
        }
    }

    public void TrySpawnWolf()
    {
        Debug.Log("[WolfSpawner] Intentando generar lobo...");

        if (wolfPrefab == null)
        {
            Debug.LogWarning("[WolfSpawner] Falta asignar Wolf Prefab.");
            return;
        }

        if (flockManager == null)
        {
            Debug.LogWarning("[WolfSpawner] Falta asignar FlockManager.");
            return;
        }

        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogWarning("[WolfSpawner] No hay Spawn Points asignados.");
            return;
        }

        if (_wolvesAlive >= maxWolvesAlive)
        {
            Debug.Log("[WolfSpawner] No genera lobo porque ya hay demasiados vivos.");
            return;
        }

        Transform spawnPoint = GetBestSpawnPoint();

        if (spawnPoint == null)
        {
            Debug.LogWarning("[WolfSpawner] No hay Spawn Point válido.");
            return;
        }

        if (!NavMesh.SamplePosition(spawnPoint.position, out NavMeshHit hit, 10f, NavMesh.AllAreas))
        {
            Debug.LogWarning("[WolfSpawner] El Spawn Point no está cerca del NavMesh: " + spawnPoint.name);
            return;
        }

        WolfAI wolf = Instantiate(wolfPrefab, hit.position, spawnPoint.rotation);

        wolf.gameObject.name = "Wolf_Runtime";
        wolf.flockManager = flockManager;
        wolf.exitPoint = GetBestExitPoint();
        wolf.activateAutomaticallyForTest = false;

        _wolvesAlive++;

        WolfLifetimeTracker tracker = wolf.gameObject.AddComponent<WolfLifetimeTracker>();
        tracker.Init(this);

        wolf.ActivateWolf();

        Debug.Log("[WolfSpawner] Lobo generado correctamente en: " + hit.position);
    }

    private Transform GetBestSpawnPoint()
    {
        Transform bestPoint = null;
        float bestScore = -Mathf.Infinity;

        foreach (Transform point in spawnPoints)
        {
            if (point == null) continue;

            float distanceToFlock = Vector3.Distance(point.position, flockManager.FlockCentroid);
            float score = distanceToFlock;

            if (score > bestScore)
            {
                bestScore = score;
                bestPoint = point;
            }
        }

        return bestPoint;
    }

    private Transform GetBestExitPoint()
    {
        if (exitPoints == null || exitPoints.Length == 0)
        {
            return null;
        }

        if (flockManager == null)
        {
            return exitPoints[0];
        }

        Transform bestExit = null;
        float bestDistance = -Mathf.Infinity;

        Vector3 flockCenter = flockManager.FlockCentroid;

        foreach (Transform exit in exitPoints)
        {
            if (exit == null) continue;

            float distanceToFlock = Vector3.Distance(exit.position, flockCenter);

            if (distanceToFlock > bestDistance)
            {
                bestDistance = distanceToFlock;
                bestExit = exit;
            }
        }

        return bestExit;
    }

    public void NotifyWolfDestroyed()
    {
        _wolvesAlive = Mathf.Max(0, _wolvesAlive - 1);
    }
}