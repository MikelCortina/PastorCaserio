using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Comida que atrae a las ovejas (las rosas que lanza el jugador).
/// Las ovejas tranquilas dentro de <see cref="detectionRadius"/> vienen a comer;
/// la rosa se va encogiendo mientras se la comen y desaparece al terminarse.
/// </summary>
public class FoodSource : MonoBehaviour
{
    public static readonly List<FoodSource> All = new List<FoodSource>();

    [Tooltip("Radio en el que las ovejas detectan esta comida")]
    public float detectionRadius = 12f;
    [Tooltip("Cuántas ovejas pueden comer a la vez")]
    public int maxOccupants = 4;
    [Tooltip("Prioridad de atracción (más alto = más apetecible)")]
    public float attractionPriority = 1f;
    [Tooltip("Segundos de comida que aporta con UNA oveja comiendo")]
    public float consumeAfterSeconds = 5f;
    [Tooltip("Segundos antes de marchitarse si nadie se la come (0 = nunca)")]
    public float lifetime = 15f;

    private float _age;

    public float Remaining01 => consumeAfterSeconds <= 0f ? 1f : Mathf.Clamp01(_remaining / consumeAfterSeconds);
    public bool IsAvailable => _remaining > 0f && _occupants < maxOccupants && (lifetime <= 0f || _age < lifetime);
    public int Occupants => _occupants;

    private int _occupants;
    private int _eaters;
    private float _remaining;
    private Vector3 _baseScale;
    private bool _scaleCaptured;

    /// <summary>Compatibilidad: configura la rosa con un tiempo de consumo.</summary>
    public void SetAsFlower(float consumeTime) { consumeAfterSeconds = consumeTime; _remaining = consumeTime; }

    void Awake() { _remaining = consumeAfterSeconds; }
    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }

    /// <summary>Reserva un hueco para venir a comer.</summary>
    public bool TryOccupy()
    {
        if (!IsAvailable) return false;
        _occupants++;
        return true;
    }

    public void Release() { _occupants = Mathf.Max(0, _occupants - 1); }

    public void StartEating() { _eaters++; }
    public void StopEating() { _eaters = Mathf.Max(0, _eaters - 1); }

    /// <summary>Punto alrededor de la rosa para que varias ovejas coman sin solaparse.</summary>
    public Vector3 GetEatPoint(int slot, float distance)
    {
        float a = slot * (360f / Mathf.Max(1, maxOccupants)) * Mathf.Deg2Rad;
        return transform.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * distance;
    }

    void Update()
    {
        if (!_scaleCaptured && _age > 1f) { _baseScale = transform.localScale; _scaleCaptured = true; }

        // Se marchita sola si nadie viene a comérsela (evita sembrar el mapa de rosas)
        _age += Time.deltaTime;
        if (lifetime > 0f && _eaters <= 0 && _age > lifetime)
        {
            transform.localScale *= Mathf.Clamp01(1f - Time.deltaTime * 3f);
            if (transform.localScale.x < 0.02f) Destroy(gameObject);
            return;
        }

        if (_eaters <= 0 || !_scaleCaptured) return;
        _remaining -= _eaters * Time.deltaTime;
        transform.localScale = _baseScale * Mathf.Lerp(0.35f, 1f, Remaining01);

        if (_remaining <= 0f) Destroy(gameObject);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.3f, 0.5f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, detectionRadius);
    }
}
