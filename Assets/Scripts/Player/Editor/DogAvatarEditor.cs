using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Botón "Construir perro" en el Inspector y menú PastorCaserio → Convertir jugador en perro.</summary>
[CustomEditor(typeof(DogAvatar))]
public class DogAvatarEditor : Editor
{
    const string MaterialFolder = "Assets/Art/Materials";

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space(10);
        var dog = (DogAvatar)target;
        using (new EditorGUI.DisabledScope(Application.isPlaying))
        {
            if (GUILayout.Button(dog.IsBuilt ? "Reconstruir perro" : "Construir perro", GUILayout.Height(32)))
                BuildDog(dog);
        }
        if (!dog.IsBuilt)
            EditorGUILayout.HelpBox("El perro aún no está en la escena (se construirá al darle a Play). Pulsa el botón para instanciarlo y guarda la escena.", MessageType.Info);
    }

    [MenuItem("PastorCaserio/Convertir jugador en perro")]
    static void ConvertPlayer()
    {
        var player = GameObject.FindGameObjectWithTag("Player");
        if (player == null) { EditorUtility.DisplayDialog("PastorCaserio", "No hay ningún objeto con tag Player en la escena.", "OK"); return; }
        var dog = player.GetComponent<DogAvatar>();
        if (dog == null) dog = Undo.AddComponent<DogAvatar>(player);
        BuildDog(dog);
        Selection.activeGameObject = player;
    }

    public static void BuildDog(DogAvatar dog)
    {
        Undo.SetCurrentGroupName("Construir perro");
        int group = Undo.GetCurrentGroup();
        Undo.RecordObject(dog, "Construir perro");
        var box = dog.GetComponent<BoxCollider>();
        if (box != null) Undo.RecordObject(box, "Construir perro");
        var mr = dog.GetComponent<MeshRenderer>();
        if (mr != null) Undo.RecordObject(mr, "Construir perro");

        if (dog.furDark == null) dog.furDark = GetOrCreateMaterial("DogFurDark", new Color(0.08f, 0.08f, 0.09f));
        if (dog.furLight == null) dog.furLight = GetOrCreateMaterial("DogFurLight", new Color(0.93f, 0.92f, 0.88f));
        if (dog.noseMat == null) dog.noseMat = GetOrCreateMaterial("DogNose", new Color(0.25f, 0.12f, 0.12f));

        if (dog.model != null) Undo.DestroyObjectImmediate(dog.model.gameObject);
        dog.model = null;
        dog.Build();
        Undo.RegisterCreatedObjectUndo(dog.model.gameObject, "Construir perro");

        Undo.CollapseUndoOperations(group);
        EditorUtility.SetDirty(dog);
        EditorSceneManager.MarkSceneDirty(dog.gameObject.scene);
    }

    static Material GetOrCreateMaterial(string name, Color color)
    {
        string path = $"{MaterialFolder}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        mat = new Material(shader) { color = color };
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.1f);
        AssetDatabase.CreateAsset(mat, path);
        AssetDatabase.SaveAssets();
        return mat;
    }
}
