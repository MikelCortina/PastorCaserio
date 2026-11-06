using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Inspector de HerdingGame con un botón que instancia las vallas del mapa y el corral
/// como objetos reales de la escena (con Undo). También disponible en el menú PastorCaserio.
/// </summary>
[CustomEditor(typeof(HerdingGame))]
public class HerdingGameEditor : Editor
{
    const string MaterialFolder = "Assets/Art/Materials";

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space(10);

        var game = (HerdingGame)target;
        using (new EditorGUI.DisabledScope(Application.isPlaying))
        {
            string label = game.levelRoot == null ? "Generar vallas en la escena" : "Regenerar vallas en la escena";
            if (GUILayout.Button(label, GUILayout.Height(32))) Generate(game);
            string uiLabel = game.HasHud ? "Regenerar UI en la escena" : "Generar UI en la escena";
            if (GUILayout.Button(uiLabel, GUILayout.Height(32))) GenerateUI(game);
        }
        if (!game.HasHud)
            EditorGUILayout.HelpBox("La UI de la partida aún no está en la escena. Pulsa «Generar UI en la escena».", MessageType.Warning);

        if (game.levelRoot == null)
            EditorGUILayout.HelpBox("Todavía no hay vallas en la escena. Pulsa el botón para crearlas.", MessageType.Warning);
        else
            EditorGUILayout.HelpBox("Tras cambiar el radio, la entrada o el tamaño del mapa, pulsa \"Regenerar\" y guarda la escena.", MessageType.Info);
    }

    [MenuItem("PastorCaserio/Generar vallas en la escena")]
    static void GenerateFromMenu()
    {
        var game = Object.FindFirstObjectByType<HerdingGame>();
        if (game == null) { EditorUtility.DisplayDialog("PastorCaserio", "No hay ningún HerdingGame en la escena abierta.", "OK"); return; }
        Generate(game);
        Selection.activeObject = game.levelRoot != null ? game.levelRoot.gameObject : game.gameObject;
    }

    [MenuItem("PastorCaserio/Generar UI en la escena")]
    static void GenerateUIFromMenu()
    {
        var game = Object.FindFirstObjectByType<HerdingGame>();
        if (game == null) { EditorUtility.DisplayDialog("PastorCaserio", "No hay ningún HerdingGame en la escena abierta.", "OK"); return; }
        GenerateUI(game);
        if (game.HudCanvas != null) Selection.activeGameObject = game.HudCanvas.gameObject;
    }

    /// <summary>Crea el Canvas del HUD (temporizador, contador, munición, leyenda y panel final) en la escena.</summary>
    public static void GenerateUI(HerdingGame game)
    {
        Undo.SetCurrentGroupName("Generar UI");
        int group = Undo.GetCurrentGroup();
        Undo.RecordObject(game, "Generar UI");
        if (game.HudCanvas != null) Undo.DestroyObjectImmediate(game.HudCanvas.gameObject);

        Canvas canvas = game.BuildHud();
        Undo.RegisterCreatedObjectUndo(canvas.gameObject, "Generar UI");
        if (canvas.gameObject.scene != game.gameObject.scene)
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(canvas.gameObject, game.gameObject.scene);

        if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            Debug.LogWarning("[HerdingGame] No hay EventSystem en la escena: el botón «Jugar otra vez» no responderá al ratón (R sigue funcionando).");

        Undo.CollapseUndoOperations(group);
        EditorUtility.SetDirty(game);
        EditorSceneManager.MarkSceneDirty(game.gameObject.scene);
        Debug.Log("[HerdingGame] UI generada en la escena. Guarda la escena (Ctrl+S).");
    }

    public static void Generate(HerdingGame game)
    {
        Undo.SetCurrentGroupName("Generar vallas");
        int group = Undo.GetCurrentGroup();
        Undo.RecordObject(game, "Generar vallas");

        // Materiales como assets, para que la escena guarde referencias válidas
        if (game.fenceMaterial == null) game.fenceMaterial = GetOrCreateMaterial("FenceWood", new Color(0.55f, 0.38f, 0.2f));
        if (game.wallMaterial == null) game.wallMaterial = GetOrCreateMaterial("FenceDark", new Color(0.32f, 0.22f, 0.12f));
        if (game.penGroundMaterial == null) game.penGroundMaterial = GetOrCreateMaterial("PenGround", new Color(0.45f, 0.6f, 0.25f));

        if (game.levelRoot != null) Undo.DestroyObjectImmediate(game.levelRoot.gameObject);
        game.levelRoot = null;

        Transform root = game.BuildLevel();
        Undo.RegisterCreatedObjectUndo(root.gameObject, "Generar vallas");
        if (game.gameObject.scene.IsValid()) SceneManager_MoveToScene(root.gameObject, game.gameObject);

        Undo.CollapseUndoOperations(group);
        EditorUtility.SetDirty(game);
        EditorSceneManager.MarkSceneDirty(game.gameObject.scene);
        Debug.Log($"[HerdingGame] Vallas generadas: {root.GetComponentsInChildren<UnityEngine.AI.NavMeshObstacle>().Length} tramos. Guarda la escena (Ctrl+S).");
    }

    static void SceneManager_MoveToScene(GameObject go, GameObject reference)
    {
        if (go.scene != reference.scene) UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, reference.scene);
    }

    static Material GetOrCreateMaterial(string name, Color color)
    {
        string path = $"{MaterialFolder}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;

        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        mat = new Material(shader) { color = color };
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.15f);
        if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder("Assets/Art", "Materials");
        AssetDatabase.CreateAsset(mat, path);
        AssetDatabase.SaveAssets();
        return mat;
    }

    /// <summary>Para ejecutar en batchmode: abre SampleScene, genera las vallas y guarda.</summary>
    public static void GenerateInSampleSceneBatch()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        var game = Object.FindFirstObjectByType<HerdingGame>();
        if (game == null) { Debug.LogError("[HerdingGame] No hay HerdingGame en SampleScene"); return; }
        Generate(game);
        GenerateUI(game);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[HerdingGame] SampleScene guardada con las vallas.");
    }
}
