using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Reglas de la partida de pastoreo:
///  · Usa el mapa vallado y el corral circular que hay instanciados en la escena (hijos de <see cref="levelRoot"/>).
///    Se generan/regeneran desde el Inspector con el botón "Generar vallas en la escena".
///  · Coloca al rebaño y al jugador.
///  · El jugador tiene <see cref="timeLimit"/> segundos para meter TODAS las ovejas en el corral.
///  · Si el tiempo llega a 0 la partida se reinicia; si lo consigue, se muestra el tiempo empleado.
/// </summary>
public class HerdingGame : MonoBehaviour
{
    public enum GameState { Playing, Won, Lost }

    [Header("Referencias")]
    public FlockManager flockManager;
    public Transform player;
    public FlowerCannon cannon;

    [Header("Partida")]
    public float timeLimit = 60f;
    [Tooltip("Segundos que se muestra el mensaje de derrota antes de reiniciar")]
    public float restartDelay = 2f;

    [Header("Mapa (centrado en este objeto)")]
    [Tooltip("Mitad del lado del mapa cuadrado")]
    public float mapHalfSize = 32f;
    public float wallHeight = 1.4f;

    [Header("Corral")]
    public float penRadius = 7f;
    [Tooltip("Dirección de la entrada del corral en grados (0 = +Z / norte)")]
    public float gateAngle = 0f;
    [Tooltip("Anchura de la entrada del corral")]
    public float gateWidth = 5f;
    [Tooltip("Longitud de las alas en embudo a los lados de la entrada")]
    public float wingLength = 5f;
    [Tooltip("Apertura de las alas respecto a la dirección de la entrada (grados)")]
    public float wingAngle = 35f;
    public float fenceHeight = 1.1f;
    [Tooltip("Margen: la oveja cuenta como dentro si su centro está a esta distancia de la valla")]
    public float insideMargin = 0.4f;

    [Header("Colocación inicial")]
    [Tooltip("Distancia del rebaño al centro del corral")]
    public float flockSpawnDistance = 20f;
    [Tooltip("Distancia del jugador por delante de la entrada")]
    public float playerOffsetFromGate = 4f;
    [Tooltip("El rebaño aparece en un lado aleatorio en cada partida")]
    public bool randomizeFlockSide = true;

    [Header("Nivel instanciado en la escena")]
    [Tooltip("Raíz con las vallas generadas. Se rellena con el botón del Inspector")]
    public Transform levelRoot;
    public Material fenceMaterial;
    public Material wallMaterial;
    public Material penGroundMaterial;

    public GameState State { get; private set; } = GameState.Playing;
    public float TimeLeft { get; private set; }
    public float Elapsed => timeLimit - TimeLeft;
    public int SheepInside { get; private set; }
    public int SheepTotal { get; private set; }

    // ── Acceso estático para las ovejas ──
    private static HerdingGame _instance;
    private Vector3 _penCenter;

    public static bool TryGetPen(out Vector3 center, out float radius)
    {
        if (_instance == null) { center = Vector3.zero; radius = 0f; return false; }
        center = _instance._penCenter;
        radius = _instance.penRadius;
        return true;
    }

    public static bool IsInsidePen(Vector3 p, float margin)
    {
        if (_instance == null) return false;
        Vector3 d = p - _instance._penCenter; d.y = 0f;
        float r = _instance.penRadius - margin;
        return d.sqrMagnitude < r * r;
    }

    private const string BestTimeKey = "PastorCaserio.BestTime";
    private float _lostTimer;
    private PlayerMovement _playerMovement;

    // ───────────────────────── Montaje ─────────────────────────

    void Awake()
    {
        _instance = this;
        Time.timeScale = 1f;
        TimeLeft = timeLimit;

        if (flockManager == null) flockManager = FindFirstObjectByType<FlockManager>();
        if (cannon == null) cannon = FindFirstObjectByType<FlowerCannon>();
        if (player == null) { var p = GameObject.FindGameObjectWithTag("Player"); if (p != null) player = p.transform; }
        if (player != null)
        {
            _playerMovement = player.GetComponent<PlayerMovement>();
            // El jugador es un perro pastor; si aún no se ha construido en el editor, se crea ahora
            if (player.GetComponent<DogAvatar>() == null) player.gameObject.AddComponent<DogAvatar>();
        }

        Vector3 c = transform.position;
        _penCenter = new Vector3(c.x, GroundY(c.x, c.z, c.y), c.z);

        // Las vallas viven en la escena; solo si faltan se generan en tiempo de ejecución
        if (levelRoot == null)
        {
            Debug.LogWarning("[HerdingGame] No hay vallas en la escena: generándolas temporalmente. Usa el botón «Generar vallas en la escena» del Inspector para instanciarlas.");
            BuildLevel();
        }

        Vector3 gateDir = Dir(gateAngle);
        // El rebaño aparece en un lado aleatorio, evitando quedar justo delante de la entrada
        float flockAngle = randomizeFlockSide ? gateAngle + Random.Range(60f, 300f) : gateAngle + 180f;

        // Rebaño
        if (flockManager != null)
        {
            Vector3 fp = _penCenter + Dir(flockAngle) * flockSpawnDistance;
            if (NavMesh.SamplePosition(fp, out NavMeshHit hit, 10f, NavMesh.AllAreas)) fp = hit.position;
            flockManager.transform.position = fp;
        }

        // Jugador delante de la entrada
        if (player != null)
        {
            Vector3 pp = _penCenter + gateDir * (penRadius + playerOffsetFromGate);
            pp.y = GroundY(pp.x, pp.z, pp.y) + 1f;
            var rb = player.GetComponent<Rigidbody>();
            player.SetPositionAndRotation(pp, Quaternion.LookRotation(gateDir));
            if (rb != null) { rb.position = pp; rb.linearVelocity = Vector3.zero; }
        }

        SetupHud();
    }

    void Start()
    {
        var cam = FindFirstObjectByType<CameraFollow>();
        if (cam != null && player != null) { cam.SetTarget(player); cam.SnapToTarget(); }
    }

    /// <summary>
    /// Crea (o recrea) las vallas del mapa y el corral como hijos de un nuevo <see cref="levelRoot"/>.
    /// Funciona también en modo edición: lo llama la herramienta del Inspector.
    /// </summary>
    public Transform BuildLevel()
    {
        if (levelRoot != null)
        {
            if (Application.isPlaying) Destroy(levelRoot.gameObject); else DestroyImmediate(levelRoot.gameObject);
        }
        Vector3 c = transform.position;
        _penCenter = new Vector3(c.x, GroundY(c.x, c.z, c.y), c.z);

        levelRoot = new GameObject("Nivel (vallas)").transform;
        levelRoot.position = _penCenter;
        BuildBoundary(levelRoot);
        BuildPen(levelRoot, gateAngle);
        return levelRoot;
    }

    static Vector3 Dir(float deg) => new Vector3(Mathf.Sin(deg * Mathf.Deg2Rad), 0f, Mathf.Cos(deg * Mathf.Deg2Rad));

    float GroundY(float x, float z, float fallback)
    {
        foreach (var h in Physics.RaycastAll(new Vector3(x, fallback + 300f, z), Vector3.down, 1000f, ~0, QueryTriggerInteraction.Ignore))
            if (h.collider is TerrainCollider) return h.point.y;
        var t = Terrain.activeTerrain;
        if (t != null) return t.SampleHeight(new Vector3(x, 0f, z)) + t.transform.position.y;
        if (NavMesh.SamplePosition(new Vector3(x, fallback, z), out NavMeshHit hit, 50f, NavMesh.AllAreas)) return hit.position.y;
        return fallback;
    }

    void BuildBoundary(Transform root)
    {
        var parent = new GameObject("Limites del mapa").transform;
        parent.SetParent(root, false);
        Material mat = wallMaterial != null ? wallMaterial : MakeMaterial(new Color(0.32f, 0.22f, 0.12f));
        float s = mapHalfSize;
        Vector3 c = _penCenter;
        Vector3[] corners =
        {
            c + new Vector3(-s, 0, -s), c + new Vector3(s, 0, -s),
            c + new Vector3(s, 0, s), c + new Vector3(-s, 0, s),
        };
        for (int i = 0; i < 4; i++) BuildFenceLine(parent, corners[i], corners[(i + 1) % 4], 2.5f, wallHeight, mat);
    }

    void BuildPen(Transform root, float gateAngle)
    {
        var parent = new GameObject("Corral").transform;
        parent.SetParent(root, false);
        Material mat = fenceMaterial != null ? fenceMaterial : MakeMaterial(new Color(0.55f, 0.38f, 0.2f));

        float halfGap = Mathf.Clamp(gateWidth * 0.5f / penRadius, 0.05f, Mathf.PI * 0.45f);
        float gate = gateAngle * Mathf.Deg2Rad;
        float arc = Mathf.PI * 2f - halfGap * 2f;
        int n = Mathf.Max(8, Mathf.CeilToInt(arc * penRadius / 1.6f));

        Vector3 Pt(float a) => _penCenter + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * penRadius;
        for (int i = 0; i < n; i++)
        {
            float a0 = gate + halfGap + arc * i / n;
            float a1 = gate + halfGap + arc * (i + 1) / n;
            BuildFenceSegment(parent, Pt(a0), Pt(a1), fenceHeight, mat, i == n - 1);
        }

        // Alas en embudo: guían al rebaño hacia la entrada
        // Cada ala sale del borde de la entrada y se abre hacia fuera (su mismo lado)
        for (int k = -1; k <= 1; k += 2)
        {
            Vector3 start = Pt(gate + halfGap * k);
            Vector3 dir = Dir(gateAngle + wingAngle * k);
            BuildFenceLine(parent, start, start + dir * wingLength, 1.6f, fenceHeight, mat);
        }

        // Marca visual de la entrada
        var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        marker.name = "Centro corral";
        DestroyImmediate(marker.GetComponent<Collider>());
        marker.transform.SetParent(parent, false);
        marker.transform.position = _penCenter + Vector3.up * 0.02f;
        marker.transform.localScale = new Vector3(penRadius * 2f - 0.6f, 0.01f, penRadius * 2f - 0.6f);
        marker.GetComponent<Renderer>().sharedMaterial = penGroundMaterial != null ? penGroundMaterial : MakeMaterial(new Color(0.45f, 0.6f, 0.25f));
    }

    void BuildFenceLine(Transform parent, Vector3 a, Vector3 b, float segLen, float height, Material mat)
    {
        float len = Vector3.Distance(new Vector3(a.x, 0, a.z), new Vector3(b.x, 0, b.z));
        int n = Mathf.Max(1, Mathf.CeilToInt(len / segLen));
        for (int i = 0; i < n; i++)
            BuildFenceSegment(parent, Vector3.Lerp(a, b, i / (float)n), Vector3.Lerp(a, b, (i + 1) / (float)n), height, mat, i == n - 1);
    }

    /// <summary>Tramo de valla: poste + 2 travesaños, collider sólido y NavMeshObstacle que bloquea el NavMesh.</summary>
    void BuildFenceSegment(Transform parent, Vector3 a, Vector3 b, float height, Material mat, bool endPost)
    {
        a.y = GroundY(a.x, a.z, _penCenter.y);
        b.y = GroundY(b.x, b.z, _penCenter.y);
        Vector3 flat = new Vector3(b.x - a.x, 0f, b.z - a.z);
        float len = flat.magnitude;
        if (len < 0.01f) return;

        var seg = new GameObject("Valla");
        seg.transform.SetParent(parent, false);
        seg.transform.SetPositionAndRotation((a + b) * 0.5f, Quaternion.LookRotation(flat / len, Vector3.up));

        var col = seg.AddComponent<BoxCollider>();
        col.size = new Vector3(0.3f, height + 1f, len + 0.1f);
        col.center = new Vector3(0f, (height + 1f) * 0.5f - 0.5f, 0f);

        var obs = seg.AddComponent<NavMeshObstacle>();
        obs.shape = NavMeshObstacleShape.Box;
        obs.size = new Vector3(0.5f, height + 1f, len + 0.2f);
        obs.center = col.center;
        obs.carving = true;
        obs.carveOnlyStationary = true;

        float dy = b.y - a.y;
        float tilt = -Mathf.Atan2(dy, len) * Mathf.Rad2Deg;
        foreach (float h in new[] { height * 0.45f, height * 0.9f })
            AddCube(seg.transform, new Vector3(0f, h, 0f), Quaternion.Euler(tilt, 0f, 0f), new Vector3(0.1f, 0.12f, len), mat);

        AddCube(seg.transform, seg.transform.InverseTransformPoint(a) + Vector3.up * height * 0.5f, Quaternion.identity, new Vector3(0.2f, height + 0.15f, 0.2f), mat);
        if (endPost)
            AddCube(seg.transform, seg.transform.InverseTransformPoint(b) + Vector3.up * height * 0.5f, Quaternion.identity, new Vector3(0.2f, height + 0.15f, 0.2f), mat);
    }

    static void AddCube(Transform parent, Vector3 localPos, Quaternion localRot, Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = localRot;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
    }

    static readonly Dictionary<Color, Material> _matCache = new Dictionary<Color, Material>();
    static Material MakeMaterial(Color color)
    {
        if (_matCache.TryGetValue(color, out var m) && m != null) return m;
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        m = new Material(shader) { color = color };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        _matCache[color] = m;
        return m;
    }

    // ───────────────────────── Bucle de juego ─────────────────────────

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && kb.rKey.wasPressedThisFrame) { Restart(); return; }

        switch (State)
        {
            case GameState.Playing:
                TimeLeft = Mathf.Max(0f, TimeLeft - Time.deltaTime);
                CountSheep();
                if (SheepTotal > 0 && SheepInside == SheepTotal) Win();
                else if (TimeLeft <= 0f) Lose();
                break;

            case GameState.Lost:
                _lostTimer -= Time.unscaledDeltaTime;
                if (_lostTimer <= 0f) Restart();
                break;
        }
        UpdateHud();
    }

    void CountSheep()
    {
        int inside = 0, total = 0;
        if (flockManager != null)
            foreach (var s in flockManager.allSheep)
            {
                if (s == null || s.CurrentState == SheepAgent.SheepState.Caught) continue;
                total++;
                if (s.IsPenned) inside++; // una vez dentro ya no puede salir
            }
        SheepInside = inside;
        SheepTotal = total;
    }

    void Win()
    {
        State = GameState.Won;
        EndControls();
        float t = Elapsed;
        float best = PlayerPrefs.GetFloat(BestTimeKey, float.MaxValue);
        bool record = t < best;
        if (record) { PlayerPrefs.SetFloat(BestTimeKey, t); PlayerPrefs.Save(); best = t; }
        ShowPanel("¡Rebaño completo!",
            $"Tiempo: {t:0.0} s\n{(record ? "¡Nuevo récord!" : $"Mejor tiempo: {best:0.0} s")}",
            true);
        Time.timeScale = 0f;
    }

    void Lose()
    {
        State = GameState.Lost;
        EndControls();
        _lostTimer = restartDelay;
        ShowPanel("¡Se acabó el tiempo!", $"Ovejas en el corral: {SheepInside}/{SheepTotal}\nReiniciando...", false);
    }

    void EndControls()
    {
        if (cannon != null) cannon.enabled = false;
        if (_playerMovement != null) _playerMovement.enabled = false;
        var rb = player != null ? player.GetComponent<Rigidbody>() : null;
        if (rb != null) rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);
    }

    public void Restart()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    // ───────────────────────── HUD (Canvas instanciado en la escena) ─────────────────────────

    [Header("UI (Canvas de la escena, la crea el botón «Generar UI en la escena»)")]
    [SerializeField] private Canvas _hudCanvas;
    [SerializeField] private Text _timerText, _countText, _roseText, _brambleText, _panelTitle, _panelBody, _tabHint;
    [SerializeField] private RectTransform _roseBar, _brambleBar;
    [SerializeField] private GameObject _panel, _panelButton, _legend;
    private Font _font;

    public bool HasHud => _hudCanvas != null && _timerText != null && _panel != null;
    public Canvas HudCanvas => _hudCanvas;

    /// <summary>Prepara la UI de la escena al empezar (o la crea si falta).</summary>
    void SetupHud()
    {
        if (!HasHud)
        {
            Debug.LogWarning("[HerdingGame] No hay UI en la escena: creándola temporalmente. Usa el botón «Generar UI en la escena» del Inspector.");
            BuildHud();
        }
        _panel.SetActive(false);
        _legend.SetActive(false);
        var button = _panelButton.GetComponent<Button>();
        button.onClick.RemoveListener(Restart);
        button.onClick.AddListener(Restart);
    }

    /// <summary>Crea (o recrea) el Canvas del HUD con todos sus elementos. También en modo edición.</summary>
    public Canvas BuildHud()
    {
        if (_hudCanvas != null)
        {
            if (Application.isPlaying) Destroy(_hudCanvas.gameObject); else DestroyImmediate(_hudCanvas.gameObject);
        }
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var canvasGo = new GameObject("HUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGo.GetComponent<Canvas>();
        _hudCanvas = canvas;
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        var root = canvasGo.transform;

        _timerText = MakeText(root, "Timer", 72, TextAnchor.UpperCenter, new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(400, 90));
        _countText = MakeText(root, "Count", 34, TextAnchor.UpperCenter, new Vector2(0.5f, 1f), new Vector2(0f, -110f), new Vector2(700, 50));

        _roseText = MakeText(root, "Rosas", 28, TextAnchor.LowerLeft, new Vector2(0f, 0f), new Vector2(30f, 70f), new Vector2(700, 40), new Vector2(0f, 0f));
        _roseBar = MakeBar(root, new Vector2(30f, 60f), new Color(1f, 0.45f, 0.7f));
        _brambleText = MakeText(root, "Zarzas", 28, TextAnchor.LowerLeft, new Vector2(0f, 0f), new Vector2(30f, 20f), new Vector2(700, 40), new Vector2(0f, 0f));
        _brambleBar = MakeBar(root, new Vector2(30f, 10f), new Color(0.5f, 0.8f, 0.3f));

        // Textos de ejemplo para verlos colocados en el editor (en partida se actualizan solos)
        int secs0 = Mathf.CeilToInt(timeLimit);
        _timerText.text = $"{secs0 / 60}:{secs0 % 60:00}";
        _countText.text = "Ovejas en el corral: 0 / 20";
        _roseText.text = "Rosas  ●●●   <size=20>Shift + click izq.</size>";
        _brambleText.text = "Zarzas ●●   <size=20>Shift + click der.</size>";

        // Leyenda de estados (TAB)
        _legend = new GameObject("Leyenda", typeof(RectTransform), typeof(Image));
        _legend.transform.SetParent(root, false);
        var lrt = (RectTransform)_legend.transform;
        lrt.anchorMin = lrt.anchorMax = lrt.pivot = new Vector2(1f, 1f);
        lrt.anchoredPosition = new Vector2(-24f, -24f);
        int rows = SheepDebugInfo.Legend.Length;
        lrt.sizeDelta = new Vector2(300f, 56f + rows * 40f);
        var lbg = _legend.GetComponent<Image>(); lbg.color = new Color(0f, 0f, 0f, 0.55f); lbg.raycastTarget = false;
        var lt = MakeText(_legend.transform, "Titulo", 28, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(16f, -10f), new Vector2(270, 36), new Vector2(0f, 1f));
        lt.text = "Estado de las ovejas";
        for (int i = 0; i < rows; i++)
        {
            var (label, color) = SheepDebugInfo.Legend[i];
            var sw = new GameObject("Color", typeof(RectTransform), typeof(Image));
            sw.transform.SetParent(_legend.transform, false);
            var srt = (RectTransform)sw.transform;
            srt.anchorMin = srt.anchorMax = srt.pivot = new Vector2(0f, 1f);
            srt.anchoredPosition = new Vector2(18f, -56f - i * 40f);
            srt.sizeDelta = new Vector2(26f, 26f);
            var si = sw.GetComponent<Image>(); si.color = color; si.raycastTarget = false;
            var tx = MakeText(_legend.transform, label, 26, TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(56f, -50f - i * 40f), new Vector2(230, 38), new Vector2(0f, 1f));
            tx.text = label;
        }
        _legend.SetActive(false);

        _tabHint = MakeText(root, "TabHint", 22, TextAnchor.UpperRight, new Vector2(1f, 1f), new Vector2(-24f, -24f), new Vector2(400, 30), new Vector2(1f, 1f));
        _tabHint.text = "TAB: ver estado de las ovejas";
        _tabHint.color = new Color(1f, 1f, 1f, 0.7f);

        // Panel de fin de partida
        _panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        _panel.transform.SetParent(root, false);
        var prt = (RectTransform)_panel.transform;
        prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one; prt.offsetMin = prt.offsetMax = Vector2.zero;
        _panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.65f);
        _panelTitle = MakeText(_panel.transform, "Titulo", 96, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, 140f), new Vector2(1400, 130));
        _panelBody = MakeText(_panel.transform, "Texto", 46, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, -10f), new Vector2(1400, 160));

        _panelButton = new GameObject("Boton", typeof(RectTransform), typeof(Image), typeof(Button));
        _panelButton.transform.SetParent(_panel.transform, false);
        var brt = (RectTransform)_panelButton.transform;
        brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.5f);
        brt.sizeDelta = new Vector2(460, 90);
        brt.anchoredPosition = new Vector2(0f, -170f);
        _panelButton.GetComponent<Image>().color = new Color(0.95f, 0.75f, 0.3f);
        var bt = MakeText(_panelButton.transform, "Label", 38, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(460, 90));
        bt.text = "Jugar otra vez (R)";
        bt.color = new Color(0.15f, 0.1f, 0.05f);
        _panel.SetActive(false);
        return canvas;
    }

    Text MakeText(Transform parent, string name, int size, TextAnchor anchor, Vector2 anchorPoint, Vector2 pos, Vector2 sizeDelta, Vector2? pivot = null)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(Outline));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = anchorPoint;
        rt.pivot = pivot ?? anchorPoint;
        rt.anchoredPosition = pos;
        rt.sizeDelta = sizeDelta;
        var t = go.GetComponent<Text>();
        t.font = _font;
        t.fontSize = size;
        t.alignment = anchor;
        t.color = Color.white;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        var o = go.GetComponent<Outline>();
        o.effectColor = new Color(0f, 0f, 0f, 0.8f);
        o.effectDistance = new Vector2(2f, -2f);
        return t;
    }

    RectTransform MakeBar(Transform parent, Vector2 pos, Color color)
    {
        var bg = new GameObject("BarBg", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(parent, false);
        var brt = (RectTransform)bg.transform;
        brt.anchorMin = brt.anchorMax = brt.pivot = Vector2.zero;
        brt.anchoredPosition = pos;
        brt.sizeDelta = new Vector2(220, 8);
        var bgi = bg.GetComponent<Image>(); bgi.color = new Color(0f, 0f, 0f, 0.5f); bgi.raycastTarget = false;

        var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(bg.transform, false);
        var frt = (RectTransform)fill.transform;
        frt.anchorMin = Vector2.zero; frt.anchorMax = new Vector2(1f, 1f); frt.pivot = new Vector2(0f, 0.5f);
        frt.offsetMin = frt.offsetMax = Vector2.zero;
        var fi = fill.GetComponent<Image>(); fi.color = color; fi.raycastTarget = false;
        return frt;
    }

    void UpdateHud()
    {
        if (_timerText == null) return;
        int secs = Mathf.CeilToInt(TimeLeft);
        _timerText.text = $"{secs / 60}:{secs % 60:00}";
        _timerText.color = TimeLeft <= 10f && State == GameState.Playing
            ? Color.Lerp(Color.white, new Color(1f, 0.25f, 0.2f), Mathf.PingPong(Time.time * 2f, 1f))
            : Color.white;
        _countText.text = $"Ovejas en el corral: {SheepInside} / {SheepTotal}";
        bool dbg = SheepDebugInfo.Visible;
        if (_legend.activeSelf != dbg) _legend.SetActive(dbg);
        _tabHint.gameObject.SetActive(!dbg);

        if (cannon != null)
        {
            _roseText.text = $"Rosas  {Pips(cannon.RoseCharges, cannon.roseMaxCharges)}   <size=20>Shift + click izq.</size>";
            _brambleText.text = $"Zarzas {Pips(cannon.BrambleCharges, cannon.brambleMaxCharges)}   <size=20>Shift + click der.</size>";
            _roseBar.anchorMax = new Vector2(Mathf.Clamp01(cannon.RoseRecharge01), 1f);
            _brambleBar.anchorMax = new Vector2(Mathf.Clamp01(cannon.BrambleRecharge01), 1f);
        }
    }

    static string Pips(int have, int max)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < max; i++) sb.Append(i < have ? "●" : "○");
        return sb.ToString();
    }

    void ShowPanel(string title, string body, bool showButton)
    {
        _panel.SetActive(true);
        _panelTitle.text = title;
        _panelBody.text = body;
        _panelButton.SetActive(showButton);
    }

    void OnDrawGizmos()
    {
        Vector3 c = Application.isPlaying ? _penCenter : transform.position;
        Gizmos.color = new Color(0.6f, 0.4f, 0.2f, 0.8f);
        Gizmos.DrawWireCube(c, new Vector3(mapHalfSize * 2f, 0.1f, mapHalfSize * 2f));
        Gizmos.color = new Color(0.3f, 1f, 0.3f, 0.8f);
        const int N = 48;
        for (int i = 0; i < N; i++)
        {
            float a0 = i * Mathf.PI * 2f / N, a1 = (i + 1) * Mathf.PI * 2f / N;
            Gizmos.DrawLine(c + new Vector3(Mathf.Sin(a0), 0, Mathf.Cos(a0)) * penRadius, c + new Vector3(Mathf.Sin(a1), 0, Mathf.Cos(a1)) * penRadius);
        }
    }
}
