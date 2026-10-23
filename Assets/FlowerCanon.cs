using System.Collections.Generic;
using UnityEngine;

public class FlowerCannon : MonoBehaviour
{
    [Header("Referencias")]
    public Transform spawnPoint;
    public LineRenderer trajectoryLine;

    [Header("Flor tipo 1 (click izquierdo)")]
    public GameObject flowerProjectilePrefab1;
    public Color previewTrailColor1 = new Color(1f, 0.6f, 0.8f, 0.4f);

    [Header("Flor tipo 2 (click derecho)")]
    public GameObject flowerProjectilePrefab2;
    public Color previewTrailColor2 = new Color(0.4f, 0.8f, 1f, 0.4f);

    [Header("Configuración de disparo")]
    public int flowerCount = 5;
    public float landingRadius = 2f;
    public float maxForce = 30f;
    public float minForce = 5f;
    public float maxDragDistance = 200f;

    [Header("Física")]
    public float gravityScale = 0.3f;

    [Header("Preview trails")]
    public float previewTrailWidth = 0.04f;

    private Vector2 dragStartPos;
    private bool isDragging = false;
    private float currentForce = 0f;
    private Vector3 currentLaunchDir;
    private int currentMouseButton = 0;

    private List<LineRenderer> previewLines = new List<LineRenderer>();

    void Start()
    {
        for (int i = 0; i < flowerCount; i++)
        {
            GameObject go = new GameObject($"PreviewLine_{i}");
            go.transform.SetParent(transform);
            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.startWidth = previewTrailWidth;
            lr.endWidth = 0f;
            lr.positionCount = 0;
            lr.useWorldSpace = true;
            previewLines.Add(lr);
        }
    }

    void Update()
    {
        if (!isDragging)
        {
            if (Input.GetMouseButtonDown(0)) StartDrag(0);
            else if (Input.GetMouseButtonDown(1)) StartDrag(1);
        }

        if (isDragging && Input.GetMouseButton(currentMouseButton))
        {
            Vector2 dragDelta = (Vector2)Input.mousePosition - dragStartPos;
            float dragDistance = Mathf.Clamp(dragDelta.magnitude, 0f, maxDragDistance);
            float t = dragDistance / maxDragDistance;
            currentForce = Mathf.Lerp(minForce, maxForce, t);
            currentLaunchDir = GetSlingshotDirection(dragDelta);

            UpdatePreviewColors(currentMouseButton);
            DrawMainTrajectory(currentLaunchDir, currentForce);
            DrawAllPreviewTrails(currentLaunchDir, currentForce);
        }

        if (isDragging && Input.GetMouseButtonUp(currentMouseButton))
        {
            isDragging = false;
            trajectoryLine.positionCount = 0;
            foreach (var lr in previewLines) lr.positionCount = 0;

            if (currentForce > minForce * 0.5f)
                FireFlowers(currentLaunchDir, currentForce, currentMouseButton);

            currentForce = 0f;
        }
    }

    void StartDrag(int mouseButton)
    {
        dragStartPos = Input.mousePosition;
        isDragging = true;
        currentMouseButton = mouseButton;
    }

    void UpdatePreviewColors(int mouseButton)
    {
        Color baseColor = mouseButton == 0 ? previewTrailColor1 : previewTrailColor2;
        Color fadeColor = new Color(baseColor.r, baseColor.g, baseColor.b, 0f);
        trajectoryLine.startColor = baseColor;
        trajectoryLine.endColor = fadeColor;
        foreach (var lr in previewLines) { lr.startColor = baseColor; lr.endColor = fadeColor; }
    }

    Vector3 GetSlingshotDirection(Vector2 dragDelta)
    {
        Vector2 invertedDelta = -dragDelta.normalized;
        Vector3 camRight = Camera.main.transform.right;
        Vector3 camForward = Camera.main.transform.forward;
        camRight.y = 0f; camForward.y = 0f;
        camRight = camRight.normalized; camForward = camForward.normalized;
        return (camRight * invertedDelta.x + camForward * invertedDelta.y).normalized;
    }

    Vector3 GetLandingPoint(Vector3 direction, float force, Vector3 startPos)
    {
        Vector3 velocity = direction * force;
        Vector3 pos = startPos;
        float dt = Time.fixedDeltaTime;
        Vector3 scaledGravity = Physics.gravity * gravityScale;

        for (int i = 0; i < 500; i++)
        {
            Vector3 nextPos = pos + velocity * dt;
            velocity += scaledGravity * dt;
            if (Physics.Linecast(pos, nextPos, out RaycastHit hit)) return hit.point;
            pos = nextPos;
        }
        return pos;
    }

    Vector3 DirectionToLandAt(Vector3 targetPoint, float force, Vector3 startPos)
    {
        Vector3 toTarget = targetPoint - startPos;
        Vector3 horizontal = new Vector3(toTarget.x, 0f, toTarget.z).normalized;
        float g = Mathf.Abs(Physics.gravity.y) * gravityScale;
        float v = force;
        float dx = new Vector2(toTarget.x, toTarget.z).magnitude;
        float dy = toTarget.y;
        float v2 = v * v;
        float disc = v2 * v2 - g * (g * dx * dx + 2f * dy * v2);
        float angle = disc < 0f
            ? 45f * Mathf.Deg2Rad
            : Mathf.Atan((v2 - Mathf.Sqrt(disc)) / (g * dx));
        return (horizontal * Mathf.Cos(angle) + Vector3.up * Mathf.Sin(angle)).normalized;
    }

    Vector3[] SimulateTrajectory(Vector3 direction, float force, Vector3 startPos)
    {
        List<Vector3> points = new List<Vector3>();
        Vector3 velocity = direction * force;
        Vector3 pos = startPos;
        float dt = Time.fixedDeltaTime;
        Vector3 scaledGravity = Physics.gravity * gravityScale;

        for (int i = 0; i < 500; i++)
        {
            points.Add(pos);
            Vector3 nextPos = pos + velocity * dt;
            velocity += scaledGravity * dt;
            if (Physics.Linecast(pos, nextPos, out RaycastHit hit)) { points.Add(hit.point); break; }
            pos = nextPos;
        }
        return points.ToArray();
    }

    void DrawMainTrajectory(Vector3 direction, float force)
    {
        Vector3[] points = SimulateTrajectory(direction, force, spawnPoint.position);
        trajectoryLine.positionCount = points.Length;
        trajectoryLine.SetPositions(points);
    }

    void DrawAllPreviewTrails(Vector3 baseDir, float force)
    {
        Vector3 landingCenter = GetLandingPoint(baseDir, force, spawnPoint.position);
        for (int i = 0; i < flowerCount; i++)
        {
            Vector3 targetPoint = GetCirclePoint(landingCenter, i);
            Vector3 spreadDir = DirectionToLandAt(targetPoint, force, spawnPoint.position);
            Vector3[] points = SimulateTrajectory(spreadDir, force, spawnPoint.position);
            previewLines[i].positionCount = points.Length;
            previewLines[i].SetPositions(points);
        }
    }

    void FireFlowers(Vector3 baseDir, float force, int mouseButton)
    {
        GameObject prefabToUse = mouseButton == 0 ? flowerProjectilePrefab1 : flowerProjectilePrefab2;
        Vector3 landingCenter = GetLandingPoint(baseDir, force, spawnPoint.position);
        for (int i = 0; i < flowerCount; i++)
        {
            Vector3 targetPoint = GetCirclePoint(landingCenter, i);
            Vector3 spreadDir = DirectionToLandAt(targetPoint, force, spawnPoint.position);
            SpawnFlower(spreadDir, force, prefabToUse);
        }
    }

    void SpawnFlower(Vector3 direction, float force, GameObject prefab)
    {
        Vector3 safeSpawnPos = spawnPoint.position + Vector3.up * 0.3f;
        GameObject projectile = Instantiate(prefab, safeSpawnPos, Quaternion.identity);
        Rigidbody rb = projectile.GetComponent<Rigidbody>();
        if (rb != null) { rb.useGravity = false; rb.isKinematic = false; rb.linearVelocity = direction * force; }
        FlowerProjectile fp = projectile.GetComponent<FlowerProjectile>();
        if (fp != null) fp.gravityScale = gravityScale;
    }

    Vector3 GetCirclePoint(Vector3 center, int index)
    {
        if (flowerCount <= 1) return center;
        float goldenAngle = 137.5f * Mathf.Deg2Rad;
        float angle = index * goldenAngle;
        float r = landingRadius * Mathf.Sqrt(index / (float)(flowerCount - 1) + 0.001f);
        return center + new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r);
    }
}