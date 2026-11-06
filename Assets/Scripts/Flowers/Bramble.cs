using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Tramo de zarza. Varios tramos seguidos forman una barrera que las ovejas no pueden cruzar:
///  · NavMeshObstacle con carving → abre un hueco en el NavMesh (bloqueo físico real).
///  · Repulsión anticipada en el steering de las ovejas → giran antes de tocarla.
/// </summary>
public class Bramble : MonoBehaviour
{
    public static readonly List<Bramble> All = new List<Bramble>();

    [Tooltip("Longitud del tramo a lo largo de la barrera")]
    public float length = 1.2f;
    [Tooltip("Grosor del tramo")]
    public float thickness = 0.9f;
    public float height = 1.2f;
    [Tooltip("Segundos antes de marchitarse (0 = permanente)")]
    public float lifetime = 30f;

    public Vector3 Axis => transform.right;
    public float HalfLength => length * 0.5f;

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }

    void Start()
    {
        var obstacle = GetComponent<NavMeshObstacle>();
        if (obstacle == null) obstacle = gameObject.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Box;
        // NavMeshObstacle.size está en espacio local → compensar la escala del objeto
        Vector3 s = transform.lossyScale;
        obstacle.size = new Vector3(length / Mathf.Max(s.x, 1e-3f), height / Mathf.Max(s.y, 1e-3f), thickness / Mathf.Max(s.z, 1e-3f));
        obstacle.center = new Vector3(0f, obstacle.size.y * 0.5f, 0f);
        obstacle.carving = true;
        obstacle.carveOnlyStationary = false;
        obstacle.carvingMoveThreshold = 0.05f;

        if (lifetime > 0f) StartCoroutine(WitherAfter(lifetime));
    }

    /// <summary>Punto más cercano del eje de la zarza a una posición (en XZ).</summary>
    public Vector3 ClosestPoint(Vector3 p)
    {
        Vector3 c = transform.position;
        Vector3 axis = Axis; axis.y = 0f; axis.Normalize();
        float t = Mathf.Clamp(Vector3.Dot(p - c, axis), -HalfLength, HalfLength);
        Vector3 q = c + axis * t;
        q.y = p.y;
        return q;
    }

    IEnumerator WitherAfter(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        Vector3 start = transform.localScale;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / 0.8f;
            transform.localScale = Vector3.Lerp(start, Vector3.zero, t);
            yield return null;
        }
        Destroy(gameObject);
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.4f, 0.2f, 0.05f, 0.6f);
        Gizmos.matrix = Matrix4x4.TRS(transform.position + Vector3.up * height * 0.5f, transform.rotation, Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(length, height, thickness));
    }
}
