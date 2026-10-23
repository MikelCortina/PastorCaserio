using UnityEngine;

public class FoodSource : MonoBehaviour
{
    [Tooltip("Radio en el que las ovejas pueden detectar esta fuente de comida")]
    public float detectionRadius = 8f;
    [Tooltip("Cuántas ovejas pueden comer aquí a la vez")]
    public int maxOccupants = 3;
    [Tooltip("Segundos que tarda en regenerarse tras ser consumida (solo comederos fijos)")]
    public float respawnTime = 15f;
    [Tooltip("Prioridad de atracción (las flores tienen más que comederos fijos)")]
    public float attractionPriority = 1f;
    [Tooltip("Si está vacía no atrae ovejas")]
    public bool IsAvailable => _available && _occupants < maxOccupants;

    // ?? Consumo por tiempo (solo flores) ?????????????????????????
    [Tooltip("Si > 0, la FoodSource se destruye tras acumular este tiempo con ovejas encima")]
    public float consumeAfterSeconds = 0f;
    private float _occupiedTimer = 0f;
    private bool _isFlower = false; // true = destruirse al consumirse, false = regenerarse

    private bool _available = true;
    private int _occupants = 0;
    private float _respawnTimer = 0f;

    public void SetAsFlower(float consumeTime)
    {
        _isFlower = true;
        consumeAfterSeconds = consumeTime;
    }

    void Update()
    {
        if (_isFlower)
        {
            // Acumular tiempo solo mientras haya ovejas encima
            if (_occupants > 0)
            {
                _occupiedTimer += Time.deltaTime;
                if (_occupiedTimer >= consumeAfterSeconds)
                {
                    // Avisar a FlockManager antes de destruirse
                    var fm = Object.FindFirstObjectByType<FlockManager>();
                    if (fm != null) fm.foodSources.Remove(this);
                    Destroy(gameObject);
                }
            }
            // Si no hay ovejas, el timer no avanza (no se destruye sola)
        }
        else
        {
            // Comedero fijo: regeneración normal
            if (!_available)
            {
                _respawnTimer -= Time.deltaTime;
                if (_respawnTimer <= 0f)
                {
                    _available = true;
                    _occupants = 0;
                }
            }
        }
    }

    public bool TryOccupy()
    {
        if (!IsAvailable) return false;
        _occupants++;
        if (!_isFlower && _occupants >= maxOccupants)
        {
            _available = false;
            _respawnTimer = respawnTime;
        }
        return true;
    }

    public void Release()
    {
        _occupants = Mathf.Max(0, _occupants - 1);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = IsAvailable ? new Color(0f, 1f, 0f, 0.3f) : new Color(1f, 0f, 0f, 0.3f);
        Gizmos.DrawSphere(transform.position, detectionRadius);
        Gizmos.color = Color.white;
        Gizmos.DrawWireSphere(transform.position, 0.5f);
    }
}