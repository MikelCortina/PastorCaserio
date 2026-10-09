using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class FlockManager : MonoBehaviour
{
    [Header("Spawn")]
    public GameObject sheepPrefab;
    public int flockSize = 20;
    public Vector3 spawnBounds = new Vector3(10f, 0f, 10f);

    [Header("Boids Weights")]
    public float separationWeight = 1.2f;
    public float alignmentWeight = 1.0f;
    public float cohesionWeight = 1.8f;
    public float fleeWeight = 2.5f;

    [Header("Boids Ranges")]
    public float separationRadius = 2f;
    public float perceptionRadiusMin = 6f;   // en calma
    public float perceptionRadiusMax = 10f;  // en pánico total

    [Header("Movement")]
    public float minSpeed = 1.5f;
    public float maxSpeed = 4.5f;

    [Header("Arousal")]
    public float arousalDecayTime = 25f;  // segundos en calmarse
    public float arousalRiseSpeed = 5f;   // sube 5x más rápido de lo que baja

    [Header("State Thresholds")]
    public float fleeRadius = 6f;
    public float flockRadius = 12f;

    [HideInInspector] public Transform playerTransform;
    [HideInInspector] public List<SheepAgent> allSheep = new();

    void Start()
    {
        var player = GameObject.FindGameObjectWithTag("Player");
        if (player == null)
        {
            Debug.LogError("[FlockManager] No se encontró ningún objeto con tag 'Player'.");
            return;
        }
        playerTransform = player.transform;

        int spawned = 0;
        int attempts = 0;
        int maxAttempts = flockSize * 5;

        while (spawned < flockSize && attempts < maxAttempts)
        {
            attempts++;
            Vector3 rawPos = transform.position + new Vector3(
                Random.Range(-spawnBounds.x, spawnBounds.x),
                0f,
                Random.Range(-spawnBounds.z, spawnBounds.z));

            if (NavMesh.SamplePosition(rawPos, out NavMeshHit hit, 5f, NavMesh.AllAreas))
            {
                var go = Instantiate(sheepPrefab, hit.position, Quaternion.identity);
                var agent = go.GetComponent<SheepAgent>();
                if (agent != null)
                {
                    // Variación visual de escala
                    float scale = Random.Range(0.85f, 1.15f);
                    go.transform.localScale = Vector3.one * scale;

                    allSheep.Add(agent);
                    agent.Init(this);
                    spawned++;
                }
            }
        }

        if (spawned < flockSize)
            Debug.LogWarning($"[FlockManager] Solo se spawnearon {spawned}/{flockSize} ovejas. " +
                             "Amplía el NavMesh o mueve el FlockManager a una zona navegable.");
    }
}