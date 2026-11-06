using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Modo depuración en juego: pulsa TAB para ver el estado de cada oveja con un color.
///  · Tiñe el cuerpo de la oveja con el color del estado.
///  · Muestra una esfera flotante encima de la oveja (más grande cuanto más miedo tiene).
/// La leyenda de colores la dibuja el HUD de HerdingGame.
/// </summary>
[RequireComponent(typeof(SheepAgent))]
public class SheepDebugInfo : MonoBehaviour
{
    /// <summary>Visible para todas las ovejas a la vez (se alterna con TAB).</summary>
    public static bool Visible { get; private set; }
    private static int _lastToggleFrame = -1;

    public static readonly (string label, Color color)[] Legend =
    {
        ("Pastando", new Color(0.2f, 0.85f, 0.25f)),
        ("Alerta", new Color(1f, 0.8f, 0.1f)),
        ("Huyendo", new Color(1f, 0.15f, 0.1f)),
        ("Va a comer", new Color(0.85f, 0.25f, 1f)),
        ("Comiendo", new Color(1f, 0.5f, 0.75f)),
        ("En el corral", new Color(0.2f, 0.55f, 1f)),
        ("Atrapada", new Color(0.1f, 0.1f, 0.1f)),
    };

    [Tooltip("Cuánto se tiñe el cuerpo (0 = nada, 1 = color puro)")]
    [Range(0f, 1f)] public float tintStrength = 0.75f;
    public float markerSize = 0.45f;

    private SheepAgent _sheep;
    private Renderer[] _renderers;
    private MaterialPropertyBlock _mpb;
    private GameObject _marker;
    private Renderer _markerRenderer;
    private bool _applied;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static Material _markerMaterial;

    void Awake()
    {
        _sheep = GetComponent<SheepAgent>();
        _renderers = GetComponentsInChildren<Renderer>();
        _mpb = new MaterialPropertyBlock();
    }

    void Update()
    {
        // Solo una oveja procesa la tecla por frame (todas comparten el estado)
        var kb = Keyboard.current;
        if (kb != null && kb.tabKey.wasPressedThisFrame && _lastToggleFrame != Time.frameCount)
        {
            _lastToggleFrame = Time.frameCount;
            Visible = !Visible;
        }
    }

    void LateUpdate()
    {
        if (!Visible)
        {
            if (_applied) Clear();
            return;
        }

        Color c = StateColor();
        foreach (var r in _renderers)
        {
            if (r == null || r == _markerRenderer) continue;
            r.GetPropertyBlock(_mpb);
            Color tint = Color.Lerp(Color.white, c, tintStrength);
            _mpb.SetColor(BaseColorId, tint);
            _mpb.SetColor(ColorId, tint);
            r.SetPropertyBlock(_mpb);
        }

        EnsureMarker();
        _marker.SetActive(true);
        float top = transform.position.y + 1f;
        foreach (var r in _renderers) if (r != null && r != _markerRenderer) top = Mathf.Max(top, r.bounds.max.y);
        _marker.transform.position = new Vector3(transform.position.x, top + 0.5f + Mathf.Sin(Time.time * 4f + GetInstanceID()) * 0.08f, transform.position.z);
        _marker.transform.localScale = Vector3.one * markerSize * (1f + _sheep.Fear * 0.8f);
        _markerRenderer.GetPropertyBlock(_mpb);
        _mpb.SetColor(BaseColorId, c);
        _mpb.SetColor(ColorId, c);
        _markerRenderer.SetPropertyBlock(_mpb);
        _applied = true;
    }

    Color StateColor()
    {
        switch (_sheep.CurrentState)
        {
            case SheepAgent.SheepState.Caught: return Legend[6].color;
            case SheepAgent.SheepState.Fleeing: return Legend[2].color;
            case SheepAgent.SheepState.Alert: return _sheep.IsPenned ? Legend[5].color : Legend[1].color;
            case SheepAgent.SheepState.GoingToFood: return Legend[3].color;
            case SheepAgent.SheepState.Eating: return Legend[4].color;
            default: return _sheep.IsPenned ? Legend[5].color : Legend[0].color;
        }
    }

    void EnsureMarker()
    {
        if (_marker != null) return;
        _marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _marker.name = $"{name}_Estado";
        Destroy(_marker.GetComponent<Collider>());
        _markerRenderer = _marker.GetComponent<Renderer>();
        _markerRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        if (_markerMaterial == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            _markerMaterial = new Material(shader);
        }
        _markerRenderer.sharedMaterial = _markerMaterial;
    }

    void Clear()
    {
        foreach (var r in _renderers)
            if (r != null && r != _markerRenderer) r.SetPropertyBlock(null);
        if (_marker != null) _marker.SetActive(false);
        _applied = false;
    }

    void OnDisable() { if (_applied) Clear(); }

    void OnDestroy() { if (_marker != null) Destroy(_marker); }

    // Gizmos en el editor (siguen disponibles en la vista Scene)
    void OnDrawGizmosSelected()
    {
        if (_sheep == null || !Application.isPlaying) return;
        Gizmos.color = StateColor();
        Gizmos.DrawWireSphere(transform.position, 1f);
#if UNITY_EDITOR
        UnityEditor.Handles.Label(transform.position + Vector3.up * 2.5f, $"{_sheep.CurrentState}  miedo {_sheep.Fear:0.00}");
#endif
    }
}
