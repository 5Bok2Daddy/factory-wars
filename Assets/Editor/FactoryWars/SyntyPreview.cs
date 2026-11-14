namespace FactoryWars.EditorTools {
  using System;
  using System.IO;
  using System.Linq;
  using UnityEditor;
  using UnityEngine;

  /// <summary>
  /// 산 에셋 팩의 모델을 크게 미리 본다(항목 32-1 칸 배정용). 배치 모드(그래픽 장치 필요):
  /// -executeMethod FactoryWars.EditorTools.SyntyPreview.Run -previewFilter "Assets/ExternalAssets/Synty/PolygonMilitary/Prefabs/Vehicles/SM_Veh_" -previewName vehicles
  /// 경로가 previewFilter로 시작하는 프리팹을 이름순으로 한 장에 그린다(한 칸 320px, 칸 아래 번호는 목록 파일의 줄 번호).
  /// 결과: Game-logs/assets/synty/preview-{이름}.png, preview-{이름}.txt
  /// </summary>
  public static class SyntyPreview {
    const int Cell = 320;
    const int Columns = 6;

    public static void Run() {
      var args = Environment.GetCommandLineArgs();
      string Arg(string key, string fallback) {
        var index = Array.IndexOf(args, key);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
      }
      var filters = Arg("-previewFilter", "Assets/ExternalAssets/Synty").Split('|');
      var exclude = Arg("-previewExclude", "").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
      var name = Arg("-previewName", "pack");
      var wanted = Arg("-previewNames", "").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
      var all = AssetDatabase.FindAssets("t:Prefab", new[] { Arg("-previewRoot", "Assets/ExternalAssets/Synty") }).Select(AssetDatabase.GUIDToAssetPath).ToArray();
      var prefabs = wanted.Length > 0
        // 파일 이름(확장자 제외)으로 고른다. 순서는 준 순서 그대로(미리보기 번호와 맞춘다).
        ? wanted.Select(w => all.FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == w)).Where(p => p != null).Take(96).ToArray()
        : all.Where(p => filters.Any(f => p.StartsWith(f, StringComparison.Ordinal)) && exclude.Any(e => p.Contains(e)) == false)
        .OrderBy(p => p, StringComparer.Ordinal).Take(96).ToArray();
      var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Game-logs", "assets", "synty"));
      Directory.CreateDirectory(folder);
      File.WriteAllLines(Path.Combine(folder, $"preview-{name}.txt"), prefabs.Select((p, i) => $"{i + 1}\t{p}\t{Size(p)}"));
      Render(prefabs, Path.Combine(folder, $"preview-{name}.png"));
      Debug.Log($"[FW-Synty] 미리보기 {prefabs.Length}개 → preview-{name}.png");
    }

    static string Size(string path) {
      var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
      var bounds = FactoryWars.View.VisualCatalog.LocalBounds(prefab);
      var skinned = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length;
      var animator = prefab.GetComponentInChildren<Animator>(true);
      return $"{bounds.size.x:0.0}x{bounds.size.y:0.0}x{bounds.size.z:0.0}m r{prefab.GetComponentsInChildren<Renderer>(true).Length} sk{skinned}{(animator != null ? " anim" : "")}";
    }

    /// <summary>
    /// 팩 프리팹의 계층·렌더러·머티리얼을 글로 적는다(읽기만 한다). 배치 모드:
    /// -executeMethod FactoryWars.EditorTools.SyntyPreview.Hierarchy -previewGuids "guid|guid" -previewName tanks
    /// 결과: Game-logs/assets/synty/hier-{이름}.txt
    /// </summary>
    public static void Hierarchy() {
      var args = Environment.GetCommandLineArgs();
      string Arg(string key, string fallback) {
        var index = Array.IndexOf(args, key);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
      }
      var guids = Arg("-previewGuids", "").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
      var name = Arg("-previewName", "pack");
      var lines = new System.Collections.Generic.List<string>();
      foreach (var guid in guids) {
        var path = AssetDatabase.GUIDToAssetPath(guid);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null) {
          lines.Add($"## {guid} 없음");
          continue;
        }
        var all = prefab.GetComponentsInChildren<Renderer>(true);
        var active = all.Count(r => r.gameObject.activeInHierarchy && r.enabled);
        var size = FactoryWars.View.VisualCatalog.LocalBounds(prefab).size;
        lines.Add($"## {guid} {System.IO.Path.GetFileNameWithoutExtension(path)} size {size.x:0.0}x{size.y:0.0}x{size.z:0.0} renderers {all.Length} active {active} skinned {prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length} animator {(prefab.GetComponentInChildren<Animator>(true) != null)}");
        Dump(prefab.transform, 0, lines, prefab.transform.childCount > 70);
      }
      var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Game-logs", "assets", "synty"));
      Directory.CreateDirectory(folder);
      File.WriteAllLines(Path.Combine(folder, $"hier-{name}.txt"), lines);
      Debug.Log($"[FW-Synty] 계층 {guids.Length}개 → hier-{name}.txt");
    }

    static void Dump(Transform node, int depth, System.Collections.Generic.List<string> lines, bool renderersOnly) {
      var renderer = node.GetComponent<Renderer>();
      var info = "";
      if (renderer != null) {
        var filter = node.GetComponent<MeshFilter>();
        var skinned = renderer as SkinnedMeshRenderer;
        var mesh = skinned != null ? skinned.sharedMesh : filter != null ? filter.sharedMesh : null;
        info = $" [{(skinned != null ? "skin" : "mesh")} {(renderer.enabled ? "on" : "off")} mats {string.Join(",", renderer.sharedMaterials.Select(m => m != null ? m.name : "null"))} mesh {(mesh != null ? mesh.bounds.size.ToString("0.0") : "-")}]";
      }
      if (renderersOnly == false || renderer != null) {
        lines.Add($"{new string(' ', depth * 2)}{node.name}{(node.gameObject.activeSelf ? "" : " (inactive)")} p{node.localPosition.ToString("0.00")} r{node.localEulerAngles.ToString("0")} s{node.localScale.x:0.00}{info}");
      }
      foreach (Transform child in node) {
        Dump(child, depth + 1, lines, renderersOnly);
      }
    }

    /// <summary>
    /// 같은 프리팹에 팩의 색 변형 머티리얼을 하나씩 입혀 한 장에 그린다(진영 구분용 변형 고르기, 읽기만 한다. 씬 사본만 바꾼다). 배치 모드(그래픽 필요):
    /// -executeMethod FactoryWars.EditorTools.SyntyPreview.Variants -previewGuids "guid" -previewMaterials "머티리얼 이름|..." -previewName name
    /// 프리팹의 첫 머티리얼(트랙·유리 제외)을 후보로 바꾼다. 결과: preview-variants-{이름}.png, .txt
    /// </summary>
    public static void Variants() {
      var args = Environment.GetCommandLineArgs();
      string Arg(string key, string fallback) {
        var index = Array.IndexOf(args, key);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
      }
      var guid = Arg("-previewGuids", "").Split('|')[0];
      var names = Arg("-previewMaterials", "").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
      var name = Arg("-previewName", "pack");
      var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
      var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Game-logs", "assets", "synty"));
      Directory.CreateDirectory(folder);
      var lines = new System.Collections.Generic.List<string>();
      var materials = names.Select(n => {
        var found = AssetDatabase.FindAssets("t:Material " + n, new[] { "Assets/ExternalAssets/Synty" }).Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == n);
        lines.Add($"{n}	{found}");
        return found != null ? AssetDatabase.LoadAssetAtPath<Material>(found) : null;
      }).ToArray();
      File.WriteAllLines(Path.Combine(folder, $"preview-variants-{name}.txt"), lines);
      if (prefab == null || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) {
        return;
      }
      var main = prefab.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).First(m => m != null && m.name.Contains("Glass") == false && m.name.Contains("Track") == false);
      var rows = Mathf.CeilToInt(materials.Length / (float)Columns);
      var sheet = new Texture2D(Columns * Cell, rows * Cell, TextureFormat.RGB24, false);
      sheet.SetPixels32(Enumerable.Repeat(new Color32(40, 44, 52, 255), sheet.width * sheet.height).ToArray());
      var target = new RenderTexture(Cell, Cell, 24);
      var cameraObject = new GameObject("PreviewCamera") { hideFlags = HideFlags.HideAndDontSave };
      var camera = cameraObject.AddComponent<Camera>();
      camera.clearFlags = CameraClearFlags.SolidColor;
      camera.backgroundColor = new Color(0.82f, 0.85f, 0.9f);
      camera.targetTexture = target;
      camera.enabled = false;
      var lightObject = new GameObject("PreviewLight") { hideFlags = HideFlags.HideAndDontSave };
      var light = lightObject.AddComponent<Light>();
      light.type = LightType.Directional;
      light.intensity = 1.3f;
      light.transform.rotation = Quaternion.Euler(50, -30, 0);
      var read = new Texture2D(Cell, Cell, TextureFormat.RGB24, false);
      for (var i = 0; i < materials.Length; i++) {
        if (materials[i] == null) {
          continue;
        }
        var item = UnityEngine.Object.Instantiate(prefab);
        item.hideFlags = HideFlags.HideAndDontSave;
        item.transform.position = new Vector3(10000, 0, 10000);
        foreach (var r in item.GetComponentsInChildren<Renderer>(true)) {
          var mats = r.sharedMaterials;
          for (var k = 0; k < mats.Length; k++) {
            if (mats[k] == main) {
              mats[k] = materials[i];
            }
          }
          r.sharedMaterials = mats;
        }
        var renderers = item.GetComponentsInChildren<Renderer>().Where(r => r.gameObject.activeInHierarchy).ToArray();
        var bounds = renderers[0].bounds;
        foreach (var r in renderers) {
          bounds.Encapsulate(r.bounds);
        }
        var radius = Mathf.Max(0.1f, bounds.extents.magnitude);
        camera.transform.position = bounds.center + new Vector3(1f, 0.75f, -1f).normalized * radius * 2.4f;
        camera.transform.LookAt(bounds.center);
        camera.nearClipPlane = 0.01f;
        camera.farClipPlane = radius * 10f;
        camera.Render();
        RenderTexture.active = target;
        read.ReadPixels(new Rect(0, 0, Cell, Cell), 0, 0);
        read.Apply();
        RenderTexture.active = null;
        sheet.SetPixels((i % Columns) * Cell, (rows - 1 - i / Columns) * Cell, Cell, Cell, read.GetPixels());
        UnityEngine.Object.DestroyImmediate(item);
      }
      sheet.Apply();
      File.WriteAllBytes(Path.Combine(folder, $"preview-variants-{name}.png"), sheet.EncodeToPNG());
      camera.targetTexture = null;
      target.Release();
      UnityEngine.Object.DestroyImmediate(cameraObject);
      UnityEngine.Object.DestroyImmediate(lightObject);
      UnityEngine.Object.DestroyImmediate(read);
      UnityEngine.Object.DestroyImmediate(sheet);
    }

    public static void Render(string[] prefabs, string output) {
      if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null || prefabs.Length == 0) {
        return;
      }
      var rows = Mathf.CeilToInt(prefabs.Length / (float)Columns);
      var sheet = new Texture2D(Columns * Cell, rows * Cell, TextureFormat.RGB24, false);
      sheet.SetPixels32(Enumerable.Repeat(new Color32(40, 44, 52, 255), sheet.width * sheet.height).ToArray());
      var target = new RenderTexture(Cell, Cell, 24);
      var cameraObject = new GameObject("PreviewCamera") { hideFlags = HideFlags.HideAndDontSave };
      var camera = cameraObject.AddComponent<Camera>();
      camera.clearFlags = CameraClearFlags.SolidColor;
      camera.backgroundColor = new Color(0.82f, 0.85f, 0.9f);
      camera.targetTexture = target;
      camera.enabled = false;
      var lightObject = new GameObject("PreviewLight") { hideFlags = HideFlags.HideAndDontSave };
      var light = lightObject.AddComponent<Light>();
      light.type = LightType.Directional;
      light.intensity = 1.3f;
      light.transform.rotation = Quaternion.Euler(50, -30, 0);
      var read = new Texture2D(Cell, Cell, TextureFormat.RGB24, false);
      var offset = new Vector3(10000, 0, 10000);
      for (var i = 0; i < prefabs.Length; i++) {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabs[i]);
        if (prefab == null) {
          continue;
        }
        var item = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        item.hideFlags = HideFlags.HideAndDontSave;
        item.transform.position = offset;
        var renderers = item.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0) {
          var bounds = renderers[0].bounds;
          foreach (var r in renderers) {
            bounds.Encapsulate(r.bounds);
          }
          var radius = Mathf.Max(0.1f, bounds.extents.magnitude);
          camera.transform.position = bounds.center + new Vector3(1f, 0.75f, -1f).normalized * radius * 2.4f;
          camera.transform.LookAt(bounds.center);
          camera.nearClipPlane = 0.01f;
          camera.farClipPlane = radius * 10f;
          camera.Render();
          RenderTexture.active = target;
          read.ReadPixels(new Rect(0, 0, Cell, Cell), 0, 0);
          read.Apply();
          RenderTexture.active = null;
          var column = i % Columns;
          var row = rows - 1 - i / Columns;
          sheet.SetPixels(column * Cell, row * Cell, Cell, Cell, read.GetPixels());
        }
        UnityEngine.Object.DestroyImmediate(item);
      }
      sheet.Apply();
      File.WriteAllBytes(output, sheet.EncodeToPNG());
      camera.targetTexture = null;
      target.Release();
      UnityEngine.Object.DestroyImmediate(cameraObject);
      UnityEngine.Object.DestroyImmediate(lightObject);
      UnityEngine.Object.DestroyImmediate(read);
      UnityEngine.Object.DestroyImmediate(sheet);
    }
  }
}
