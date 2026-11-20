namespace FactoryWars.EditorTools {
  using System.IO;
  using System.Linq;
  using FactoryWars.View;
  using Quantum;
  using Quantum.Editor;
  using UnityEditor;
  using UnityEditor.SceneManagement;
  using UnityEngine;

  public static class NewBundleTools {
    const string Bundle = "Assets/ThirdParty/NewBundle";
    const string Output = Bundle + "/Generated/Resources/FactoryWarsBundle";

    [MenuItem("Tools/Factory Wars/Assets/Prepare New Bundle")]
    public static void Prepare() {
      var shader = Shader.Find("Universal Render Pipeline/Simple Lit");
      if (shader == null) throw new System.InvalidOperationException("URP Simple Lit is unavailable.");
      Directory.CreateDirectory(Output);
      AssetDatabase.Refresh();
      var selected = Directory.GetFiles(Bundle, "*.prefab", SearchOption.AllDirectories)
        .Where(path => Path.GetFileName(Path.GetDirectoryName(path)) == "Selected").OrderBy(path => path).ToArray();
      if (selected.Length == 0) throw new System.InvalidOperationException("Run Tools/import_new_bundle.py first.");
      foreach (var file in selected) {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(file.Replace('\\', '/'));
        var model = (GameObject)PrefabUtility.InstantiatePrefab(source);
        try {
          PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
          foreach (var lod in model.GetComponentsInChildren<LODGroup>(true)) {
            var levels = lod.GetLODs();
            var visible = levels.Length > 0 ? levels[0].renderers : System.Array.Empty<Renderer>();
            foreach (var discarded in levels.Skip(1).SelectMany(level => level.renderers).Distinct()) {
              if (discarded != null && visible.Contains(discarded) == false) {
                var filter = discarded.GetComponent<MeshFilter>();
                Object.DestroyImmediate(discarded);
                if (filter != null) Object.DestroyImmediate(filter);
              }
            }
            Object.DestroyImmediate(lod);
          }
          foreach (var collider in model.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
          foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>(true)) {
            renderer.sharedMaterials = renderer.sharedMaterials.Select(material => PrepareMaterial(material, shader)).ToArray();
          }
          var key = Path.GetFileNameWithoutExtension(file);
          PrefabUtility.SaveAsPrefabAsset(model, $"{Output}/{key}.prefab");
          Debug.Log($"[FW-Bundle] {key} {VisualCatalog.LocalBounds(model)}");
        } finally {
          Object.DestroyImmediate(model);
        }
      }
      var sandPath = Directory.GetFiles(Bundle + "/Desert/Source", "Sand_2_Normal.png", SearchOption.AllDirectories).Single()
        .Replace('\\', '/');
      var target = Output + "/SandNormal.png";
      if (AssetDatabase.LoadAssetAtPath<Texture2D>(target) == null && AssetDatabase.CopyAsset(sandPath, target) == false) {
        throw new System.InvalidOperationException("Could not prepare the desert surface normal.");
      }
      var sandImporter = (TextureImporter)AssetImporter.GetAtPath(target);
      sandImporter.textureType = TextureImporterType.NormalMap;
      sandImporter.maxTextureSize = 1024;
      sandImporter.wrapMode = TextureWrapMode.Repeat;
      sandImporter.SaveAndReimport();
      AssetDatabase.SaveAssets();
    }

    static Material PrepareMaterial(Material source, Shader shader) {
      if (source == null) throw new System.InvalidOperationException("Missing bundle material.");
      if (source.shader != null && source.shader.name.StartsWith("Universal Render Pipeline/")) return source;

      var key = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source));
      var path = $"{Output}/{key}.mat";
      var material = AssetDatabase.LoadAssetAtPath<Material>(path);
      if (material != null) return material;
      material = new Material(shader);
      material.SetTexture("_BaseMap", source.HasProperty("_BaseMap") ? source.GetTexture("_BaseMap") : source.mainTexture);
      material.SetColor("_BaseColor", source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor")
        : source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white);
      material.SetFloat("_Smoothness", 0.1f);
      material.enableInstancing = true;
      AssetDatabase.CreateAsset(material, path);
      return material;
    }

    public static void RebakeFiveMaps() {
      foreach (var map in MapCatalog.Maps) {
        var scene = EditorSceneManager.OpenScene($"Assets/QuantumUser/Scenes/{map.Id}.unity");
        var data = Object.FindAnyObjectByType<QuantumMapData>();
        if (data == null) throw new System.InvalidOperationException($"Missing QuantumMapData in {map.Id}");
        QuantumEditorAutoBaker.BakeMap(data, QuantumMapDataBakeFlags.BakeUnityNavMesh |
          QuantumMapDataBakeFlags.ImportUnityNavMesh | QuantumMapDataBakeFlags.BakeNavMesh);
        if (scene.isDirty && EditorSceneManager.SaveScene(scene) == false) {
          throw new System.InvalidOperationException($"Could not save {map.Id}");
        }
        Debug.Log($"[FW-Bundle] NavMesh baked for {map.Id}");
      }
      AssetDatabase.SaveAssets();
    }
  }
}
