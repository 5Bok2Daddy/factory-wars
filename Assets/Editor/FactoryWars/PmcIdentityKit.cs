namespace FactoryWars.EditorTools {
  using System.Collections.Generic;
  using System.IO;
  using FactoryWars.View;
  using UnityEditor;
  using UnityEngine;

  /// <summary>
  /// Task 39 fix 5: PMC identity kit made by Codex in art-staging/t39 (fictional "Veyrspan Company", emblem, gate banner, vehicle side
  /// stripe, shoulder patch, shop icons + state overlays). Imports the PNGs into FactoryWarsArt/PMC/Identity and FactoryWarsArt/UI/PMC,
  /// makes our decal materials and fills the PMC shop slots of UiCatalog. Palette: charcoal #252A31, amber #F2B64B.
  /// </summary>
  public static class PmcIdentityKit {
    public const string Identity = "Assets/FactoryWarsArt/PMC/Identity";
    public const string Icons = "Assets/FactoryWarsArt/UI/PMC";
    public static readonly Color Charcoal = new Color32(0x25, 0x2A, 0x31, 0xFF);
    public static readonly Color Amber = new Color32(0xF2, 0xB6, 0x4B, 0xFF);

    static string Staging => Path.GetFullPath(Path.Combine(Application.dataPath, "../../art-staging/t39"));

    /// <summary>UiCatalog slot id → staged icon file.</summary>
    public static readonly (string Id, string File, string Note)[] ShopIcons = {
      ("pmc_operator_squad", "operator_squad", "PMC 상점: 작전 분대"),
      ("pmc_marksman", "marksman", "PMC 상점: 저격수"),
      ("pmc_armoured_suv", "armoured_suv", "PMC 상점: 장갑차"),
      ("pmc_attack_drone", "attack_drone", "PMC 상점: 공격 드론"),
      ("pmc_tank", "light_tank", "PMC 상점: 전차"),
      ("pmc_supply_trade", "supply_trade", "PMC 상점: 보급 교환"),
      ("pmc_diplomacy", "diplomacy_message", "PMC: 외교 메시지"),
      ("pmc_cargo_truck", "cargo_truck", "PMC: 교역 화물 트럭"),
      ("pmc_state_locked", "state_locked", "PMC 상태: 잠김(우상단)"),
      ("pmc_state_sold_out", "state_sold_out", "PMC 상태: 품절(우상단)"),
      ("pmc_state_cooldown", "state_cooldown", "PMC 상태: 대기(우상단)"),
    };

    public static void Import() {
      Folder(Identity);
      Folder(Icons);
      if (Directory.Exists(Staging) == false) {
        Debug.LogWarning($"[FW-PMC] staging folder missing: {Staging} (using already imported files)");
      } else {
        foreach (var file in Directory.GetFiles(Path.Combine(Staging, "pmc"), "*.png")) File.Copy(file, Path.Combine(Identity, Path.GetFileName(file)), true);
        foreach (var file in Directory.GetFiles(Path.Combine(Staging, "icons"), "*.png")) File.Copy(file, Path.Combine(Icons, Path.GetFileName(file)), true);
      }
      AssetDatabase.Refresh();
      foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Identity })) {
        var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid));
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.mipmapEnabled = true;
        importer.SaveAndReimport();
      }
      foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Icons })) {
        var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid));
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.sRGBTexture = true;
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.mipmapEnabled = false;
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);
        importer.SaveAndReimport();
      }
      FillUiCatalog();
      Debug.Log("[FW-PMC] identity kit imported");
    }

    static void FillUiCatalog() {
      var catalog = Resources.Load<UiCatalog>(UiCatalog.ResourcePath);
      if (catalog == null) { Debug.LogWarning("[FW-PMC] UiCatalog missing"); return; }
      foreach (var (id, file, note) in ShopIcons) {
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{Icons}/{file}.png");
        var slot = catalog.Get(id);
        if (slot == null) catalog.Slots.Add(slot = new UiCatalog.Slot { Id = id });
        slot.Sprite = sprite;
        slot.Note = note;
        if (sprite == null) Debug.LogWarning($"[FW-PMC] icon missing {file}");
      }
      EditorUtility.SetDirty(catalog);
      AssetDatabase.SaveAssets();
    }

    static void Folder(string path) {
      var parts = path.Split('/');
      for (var i = 1; i < parts.Length; i++) {
        var parent = string.Join("/", parts, 0, i);
        if (AssetDatabase.IsValidFolder(parent + "/" + parts[i]) == false) AssetDatabase.CreateFolder(parent, parts[i]);
      }
    }

    static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();

    /// <summary>Our URP Lit material: a staged decal texture (alpha-clipped) or a plain palette colour.</summary>
    public static Material Mat(string name, string texture = null, Color? colour = null) {
      if (Cache.TryGetValue(name, out var cached) && cached != null) return cached;
      Folder(Identity);
      var path = $"{Identity}/PMC_{name}.mat";
      var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
      if (mat == null) {
        mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        AssetDatabase.CreateAsset(mat, path);
      }
      mat.SetColor("_BaseColor", colour ?? Color.white);
      mat.SetFloat("_Smoothness", 0.15f);
      mat.SetFloat("_Metallic", 0f);
      if (texture != null) {
        mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{Identity}/{texture}.png"));
        mat.SetFloat("_AlphaClip", 1f);
        mat.SetFloat("_Cutoff", 0.5f);
        mat.EnableKeyword("_ALPHATEST_ON");
        mat.SetFloat("_Cull", 0f);
      }
      mat.enableInstancing = true;
      EditorUtility.SetDirty(mat);
      Cache[name] = mat;
      return mat;
    }

    /// <summary>A textured quad (UV 0..1) facing <paramref name="normal"/>; both faces when <paramref name="twoSided"/>.</summary>
    public static GameObject Quad(Transform parent, string name, Material mat, Vector3 centre, Vector3 normal, float width, float height, bool twoSided = false) {
      var right = Vector3.Cross(Vector3.up, normal).normalized;
      if (right.sqrMagnitude < 0.01f) right = Vector3.right;
      var up = Vector3.Cross(normal, right).normalized;
      var o = normal.normalized * 0.01f;
      var v = new List<Vector3>();
      var uv = new List<Vector2>();
      var tris = new List<int>();
      void Face(Vector3 n, bool flip) {
        var b = v.Count;
        var r = flip ? -right : right;
        v.Add(centre + n - r * width / 2f - up * height / 2f);
        v.Add(centre + n + r * width / 2f - up * height / 2f);
        v.Add(centre + n + r * width / 2f + up * height / 2f);
        v.Add(centre + n - r * width / 2f + up * height / 2f);
        uv.AddRange(new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
        tris.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
      }
      Face(o, false);
      if (twoSided) Face(-o, true);
      var mesh = new Mesh { name = name };
      mesh.SetVertices(v);
      mesh.SetUVs(0, uv);
      mesh.SetTriangles(tris, 0);
      mesh.RecalculateNormals();
      mesh.RecalculateBounds();
      var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
      go.transform.SetParent(parent, false);
      go.GetComponent<MeshFilter>().sharedMesh = mesh;
      go.GetComponent<MeshRenderer>().sharedMaterial = mat;
      return go;
    }

    /// <summary>A plain box in a palette material (pole, sign panel, frame), local axes.</summary>
    public static GameObject Box(Transform parent, string name, Material mat, Vector3 centre, Vector3 size, float yaw = 0f) {
      var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
      Object.DestroyImmediate(go.GetComponent<Collider>());
      go.name = name;
      go.transform.SetParent(parent, false);
      go.transform.localPosition = centre;
      go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
      go.transform.localScale = size;
      go.GetComponent<MeshRenderer>().sharedMaterial = mat;
      return go;
    }
  }
}
