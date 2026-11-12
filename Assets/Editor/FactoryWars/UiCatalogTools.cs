namespace FactoryWars.EditorTools {
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using FactoryWars.View;
  using UnityEditor;
  using UnityEngine;

  /// <summary>
  /// 항목 31 UI 도구. (1) UiTheme·UiCatalog 에셋 만들기(칸은 비어 있고 임시 그림을 쓴다).
  /// (2) 유닛·건물 아이콘을 모델에서 촬영(고정 각도·투명 배경) → Resources/FactoryWars/Icons/model_&lt;id&gt;.png.
  /// 에셋 팩으로 모델을 바꾼 뒤 메뉴 한 번으로 다시 찍는다. 그래픽 장치가 있어야 한다(-nographics 금지).
  /// </summary>
  public static class UiCatalogTools {
    const string Folder = "Assets/QuantumUser/Resources/FactoryWars/UI";
    const string IconFolder = "Assets/QuantumUser/Resources/FactoryWars/Icons";
    const int IconSize = 128;

    [MenuItem("Tools/Factory Wars/UI/Build Theme And Slots")]
    public static void BuildAssets() {
      Directory.CreateDirectory(Folder);
      var themePath = $"{Folder}/UiTheme.asset";
      if (AssetDatabase.LoadAssetAtPath<UiTheme>(themePath) == null) {
        AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<UiTheme>(), themePath);
      }
      var catalogPath = $"{Folder}/UiCatalog.asset";
      var catalog = AssetDatabase.LoadAssetAtPath<UiCatalog>(catalogPath);
      if (catalog == null) {
        catalog = ScriptableObject.CreateInstance<UiCatalog>();
        AssetDatabase.CreateAsset(catalog, catalogPath);
      }
      foreach (var slot in UiCatalog.DefaultSlots()) {
        var existing = catalog.Get(slot.Id);
        if (existing == null) {
          catalog.Slots.Add(slot);
        } else {
          existing.Fallback = slot.Fallback;
          existing.Note = slot.Note;
        }
      }
      EditorUtility.SetDirty(catalog);
      AssetDatabase.SaveAssets();
      Debug.Log($"[FW-UI] UI 칸 {catalog.Slots.Count}개");
    }

    /// <summary>배치 모드: -executeMethod FactoryWars.EditorTools.UiCatalogTools.BuildAll (아이콘 굽기 → 칸 → 모델 아이콘).</summary>
    public static void BuildAll() {
      IconBaker.BakeAll();
      BuildAssets();
      VisualCatalogTools.BuildSlots();
      CaptureModelIcons();
    }

    [MenuItem("Tools/Factory Wars/UI/Capture Model Icons")]
    public static void CaptureModelIcons() {
      var catalog = Resources.Load<ViewCatalog>(ViewCatalog.ResourcePath);
      if (catalog == null) {
        Debug.LogError("[FW-UI] ViewCatalog 없음");
        return;
      }
      var ids = catalog.Entries.Select(e => e.Id).Concat(ViewCatalog.TemporaryModels.Select(m => m.Id)).Distinct().ToList();
      var cameraObject = new GameObject("IconCamera");
      var camera = cameraObject.AddComponent<Camera>();
      camera.clearFlags = CameraClearFlags.SolidColor;
      camera.backgroundColor = new Color(0, 0, 0, 0);
      camera.orthographic = true;
      camera.enabled = false;
      var lightObject = new GameObject("IconLight");
      var light = lightObject.AddComponent<Light>();
      light.type = LightType.Directional;
      light.intensity = 1.2f;
      lightObject.transform.rotation = Quaternion.Euler(50, -30, 0);
      var surface = new RenderTexture(IconSize, IconSize, 24, RenderTextureFormat.ARGB32);
      camera.targetTexture = surface;
      var count = 0;
      Directory.CreateDirectory(IconFolder);
      try {
        foreach (var id in ids) {
          if (catalog.TryGet(id, out var entry) == false || entry.Prefab == null) {
            continue;
          }
          VisualCatalog.Override(VisualCatalog.ViewSlotId(id, entry.Unit), ref entry, out var slot);
          var prefab = slot != null && slot.Prefab != null ? slot.Prefab : entry.Prefab;
          var model = Object.Instantiate(prefab);
          model.transform.position = new Vector3(0, -1000, 0);
          ArtLook.Apply(model, 0);
          var renderers = model.GetComponentsInChildren<Renderer>();
          if (renderers.Length == 0) {
            Object.DestroyImmediate(model);
            continue;
          }
          var bounds = renderers[0].bounds;
          foreach (var r in renderers) {
            bounds.Encapsulate(r.bounds);
          }
          // 고정 각도: 왼쪽 앞 위에서 내려다본다.
          var rotation = Quaternion.Euler(30, -35, 0);
          var radius = bounds.extents.magnitude;
          camera.transform.rotation = rotation;
          camera.transform.position = bounds.center - rotation * Vector3.forward * (radius * 4f);
          camera.orthographicSize = radius * 1.02f;
          camera.nearClipPlane = 0.01f;
          camera.farClipPlane = radius * 10f;
          camera.Render();
          RenderTexture.active = surface;
          var texture = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false);
          texture.ReadPixels(new Rect(0, 0, IconSize, IconSize), 0, 0);
          texture.Apply();
          RenderTexture.active = null;
          var path = $"{IconFolder}/model_{id}.png";
          File.WriteAllBytes(path, texture.EncodeToPNG());
          Object.DestroyImmediate(texture);
          Object.DestroyImmediate(model);
          AssetDatabase.ImportAsset(path);
          var importer = (TextureImporter)AssetImporter.GetAtPath(path);
          importer.textureType = TextureImporterType.Sprite;
          importer.spriteImportMode = SpriteImportMode.Single;
          importer.mipmapEnabled = false;
          importer.alphaIsTransparency = true;
          importer.SaveAndReimport();
          count++;
        }
      } finally {
        camera.targetTexture = null;
        surface.Release();
        Object.DestroyImmediate(cameraObject);
        Object.DestroyImmediate(lightObject);
      }
      Debug.Log($"[FW-UI] 모델 아이콘 {count}개 (Icons/model_*.png)");
    }
  }
}
