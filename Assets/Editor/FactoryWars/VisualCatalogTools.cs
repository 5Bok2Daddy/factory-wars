namespace FactoryWars.EditorTools {
  using System;
  using System.Collections.Generic;
  using System.Globalization;
  using System.IO;
  using System.Linq;
  using System.Text;
  using FactoryWars.View;
  using UnityEditor;
  using UnityEngine;

  /// <summary>
  /// 에셋 팩 교체 도구(항목 30). 사용자 설명서: Game/Docs/ASSET-SWAP.md
  /// 1) Build Slots: 게임 요소마다 빈 슬롯을 만든다(채워진 슬롯은 그대로 둔다).
  /// 2) Scan Pack: 팩 폴더의 프리팹 목록·미리보기 접촉 시트·슬롯 후보 CSV를 만든다.
  /// 3) Import CSV: 사용자가 고친 CSV를 슬롯에 넣는다.
  /// 4) Convert Materials: 내장 셰이더 머티리얼을 URP Lit으로 바꾼다(분홍 머티리얼 방지).
  /// </summary>
  public static class VisualCatalogTools {
    public const string AssetPath = "Assets/QuantumUser/Resources/FactoryWars/Visual/VisualCatalog.asset";
    public const string ViewCatalogPath = "Assets/QuantumUser/Resources/FactoryWars/Art/ViewCatalog.asset";
    public static readonly string[] Effects = { "muzzle", "impact", "explosion", "flame", "smoke", "weather_sand", "weather_rain", "weather_fog" };

    static string LogFolder() {
      var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Game-logs", "assets"));
      Directory.CreateDirectory(folder);
      return folder;
    }

    // ---------- 1. 슬롯 ----------

    [MenuItem("Tools/Factory Wars/Assets/1. Build Visual Slots")]
    public static void BuildSlotsMenu() {
      var catalog = BuildSlots();
      Debug.Log($"[FW-Assets] 슬롯 {catalog.Slots.Count}개 ({AssetPath})");
    }

    public static VisualCatalog BuildSlots() {
      var catalog = AssetDatabase.LoadAssetAtPath<VisualCatalog>(AssetPath);
      if (catalog == null) {
        catalog = ScriptableObject.CreateInstance<VisualCatalog>();
        Directory.CreateDirectory(Path.GetDirectoryName(AssetPath));
        AssetDatabase.CreateAsset(catalog, AssetPath);
      }
      var keep = catalog.Slots.Where(s => s != null && string.IsNullOrEmpty(s.Id) == false).ToDictionary(s => s.Id);
      var slots = new List<VisualSlot>();
      foreach (var (id, category, team) in SlotIds()) {
        if (keep.TryGetValue(id, out var slot) == false) {
          slot = new VisualSlot { Id = id, Category = category, TeamColor = team, AutoFit = category == VisualCategory.Unit || category == VisualCategory.Building || category == VisualCategory.Resource };
          slot.RealWorld = category == VisualCategory.Unit || category == VisualCategory.Building ? RealWorldCheck.Unchecked : RealWorldCheck.Clear;
        }
        slot.Category = category;
        slots.Add(slot);
      }
      // 표에 없는 슬롯(사용자가 직접 만든 것)은 뒤에 남긴다.
      slots.AddRange(keep.Values.Where(s => slots.Contains(s) == false));
      catalog.Slots = slots;
      catalog.Invalidate();
      EditorUtility.SetDirty(catalog);
      AssetDatabase.SaveAssets();
      return catalog;
    }

    /// <summary>모든 슬롯 Id. 유닛·건물은 ViewCatalog에서, 소품·도시·지형은 DressKind에서, 효과는 고정 목록에서 온다.</summary>
    public static IEnumerable<(string Id, VisualCategory Category, TeamColorMode Team)> SlotIds() {
      var view = AssetDatabase.LoadAssetAtPath<ViewCatalog>(ViewCatalogPath);
      foreach (var entry in view.Entries) {
        var id = VisualCatalog.ViewSlotId(entry.Id, entry.Unit);
        var category = id.StartsWith("unit/") ? VisualCategory.Unit : id.StartsWith("resource/") ? VisualCategory.Resource : id.StartsWith("terrain/") ? VisualCategory.Terrain : VisualCategory.Building;
        var team = category == VisualCategory.Unit || category == VisualCategory.Building ? TeamColorMode.MaterialName : TeamColorMode.None;
        yield return (id, category, team);
      }
      // 항목 31 새 유닛: 지금은 기존 모델을 빌려 쓰지만(ViewCatalog.TemporaryModels) 칸은 따로 둔다.
      // 항목 32 새 건물도 같다(빌린 모델이 건물이면 건물 칸).
      foreach (var model in ViewCatalog.TemporaryModels) {
        var unit = view.TryGet(model.Borrow, out var borrowed) && borrowed.Unit;
        yield return (unit ? VisualCatalog.UnitId(model.Id) : VisualCatalog.BuildingId(model.Id), unit ? VisualCategory.Unit : VisualCategory.Building, TeamColorMode.MaterialName);
      }
      foreach (DressKind kind in Enum.GetValues(typeof(DressKind))) {
        yield return (VisualCatalog.DressId(kind), DressCategory(kind), TeamColorMode.None);
      }
      foreach (var fx in Effects) {
        yield return (VisualCatalog.FxId(fx), VisualCategory.Effect, TeamColorMode.None);
      }
    }

    public static VisualCategory DressCategory(DressKind kind) {
      switch (kind) {
        case DressKind.CitySmall: case DressKind.CityMedium: case DressKind.CityIndustrial: case DressKind.CityTower: case DressKind.CityGarrison:
        case DressKind.Ruin: case DressKind.Furniture: case DressKind.SuburbanProp: case DressKind.CityCafe:
          return VisualCategory.City;
        case DressKind.Conifer: case DressKind.Autumn: case DressKind.Broadleaf: case DressKind.TreeSparse: case DressKind.Stump: case DressKind.Bush:
        case DressKind.RockLarge: case DressKind.RockSmall: case DressKind.Cliff: case DressKind.Grass: case DressKind.GroundPatch:
        case DressKind.BridgePillar: case DressKind.BridgeDeck: case DressKind.TrainTrack: case DressKind.BaseWall: case DressKind.WaterGate:
          return VisualCategory.Terrain;
        case DressKind.Crystal:
          return VisualCategory.Resource;
        default:
          return VisualCategory.Prop;
      }
    }

    // ---------- 2. 팩 스캔 ----------

    /// <summary>슬롯 Id 조각 → 프리팹 경로에서 찾을 낱말. 영어 팩 이름 규칙(Synty·POLYGON·SIMPLE 등)에 맞췄다.</summary>
    static readonly (string Part, string[] Words)[] Hints = {
      ("engineer", new[] { "engineer", "worker", "builder", "mechanic", "construction" }),
      ("ranger_squad", new[] { "soldier", "infantry", "rifle", "trooper" }),
      ("militia", new[] { "militia", "soldier", "infantry", "rebel" }),
      ("skyguard_squad", new[] { "soldier", "rocket", "stinger", "launcher_soldier", "infantry" }),
      ("flame_squad", new[] { "flame", "soldier", "infantry", "hazmat" }),
      ("eagle_tank", new[] { "tank", "mbt", "armor" }),
      ("dragonscale_tank", new[] { "tank", "heavy", "armor" }),
      ("hawk_gunship", new[] { "helicopter", "heli", "chopper", "gunship" }),
      ("skyfortress_bomber", new[] { "bomber", "plane", "aircraft", "jet" }),
      ("thunder_aa", new[] { "aa", "anti_air", "antiair", "flak", "radar_truck" }),
      ("longwall_mlrs", new[] { "rocket", "mlrs", "artillery", "launcher" }),
      ("scout_boat", new[] { "boat", "patrol", "dinghy", "speedboat" }),
      ("artillery_ship", new[] { "ship", "destroyer", "gunboat", "frigate" }),
      ("headquarters", new[] { "headquarter", "hq", "command", "base" }),
      ("power_plant", new[] { "power", "generator", "reactor", "plant" }),
      ("processor", new[] { "refinery", "processor", "smelter", "factory" }),
      ("supply_depot", new[] { "depot", "warehouse", "storage", "supply" }),
      ("barracks", new[] { "barracks", "tent", "bunker" }),
      ("research_lab", new[] { "lab", "research", "science", "radar" }),
      ("vehicle_factory", new[] { "factory", "garage", "workshop" }),
      ("airfield", new[] { "hangar", "airfield", "helipad", "runway" }),
      ("heavy_works", new[] { "heavy", "foundry", "industrial", "factory" }),
      ("ground_tower", new[] { "tower", "turret", "bunker", "pillbox" }),
      ("air_defense", new[] { "aa", "sam", "anti_air", "missile", "turret" }),
      ("collection_yard", new[] { "yard", "container", "crane", "scrap" }),
      ("shipyard", new[] { "dock", "pier", "shipyard", "harbor", "port" }),
      ("iron_mine", new[] { "mine", "ore", "rock", "quarry" }),
      ("bridge", new[] { "bridge" }),
      ("Conifer", new[] { "pine", "conifer", "fir" }),
      ("Broadleaf", new[] { "tree", "oak", "birch" }),
      ("Autumn", new[] { "autumn", "tree" }),
      ("TreeSparse", new[] { "tree_dead", "dead", "tree" }),
      ("Stump", new[] { "stump", "log" }),
      ("Bush", new[] { "bush", "shrub" }),
      ("RockLarge", new[] { "rock_large", "boulder", "rock" }),
      ("RockSmall", new[] { "rock_small", "pebble", "rock" }),
      ("Cliff", new[] { "cliff" }),
      ("Grass", new[] { "grass" }),
      ("CitySmall", new[] { "house", "building_small", "shop" }),
      ("CityMedium", new[] { "building", "apartment", "office" }),
      ("CityIndustrial", new[] { "warehouse", "industrial", "factory" }),
      ("CityTower", new[] { "tower", "skyscraper", "highrise" }),
      ("CityGarrison", new[] { "bunker", "building" }),
      ("Container", new[] { "container" }),
      ("Barrier", new[] { "barrier", "sandbag", "barricade", "fence" }),
      ("SupplyCrate", new[] { "crate", "box", "supply" }),
      ("Debris", new[] { "debris", "rubble", "wreck" }),
      ("Ruin", new[] { "ruin", "rubble", "destroyed" }),
      ("Wreck", new[] { "wreck", "destroyed", "burnt" }),
      ("explosion", new[] { "explosion", "fx_explosion", "blast" }),
      ("muzzle", new[] { "muzzle", "flash" }),
      ("impact", new[] { "impact", "hit", "spark" }),
      ("flame", new[] { "flame", "fire" }),
      ("smoke", new[] { "smoke" }),
    };

    /// <summary>프리팹 경로에서 슬롯 후보 점수. 낱말이 앞에 있을수록 높다. 0이면 후보가 아니다.</summary>
    public static int Score(string slotId, string prefabPath) {
      var name = Path.GetFileNameWithoutExtension(prefabPath).ToLowerInvariant();
      var path = prefabPath.ToLowerInvariant();
      var part = slotId.Substring(slotId.IndexOf('/') + 1);
      var key = part.Length > 3 && (part.StartsWith("ff_") || part.StartsWith("cf_")) ? part.Substring(3) : part;
      var hint = Hints.FirstOrDefault(h => h.Part == key);
      if (hint.Words == null) {
        return 0;
      }
      var score = 0;
      for (var i = 0; i < hint.Words.Length; i++) {
        var word = hint.Words[i];
        if (name.Contains(word)) {
          score = Math.Max(score, 100 - i * 10);
        } else if (path.Contains(word)) {
          score = Math.Max(score, 40 - i * 5);
        }
      }
      // 캐릭터 팩의 부품(팔·머리 등)이나 LOD는 낮춘다.
      if (name.Contains("lod") || name.Contains("_part") || name.Contains("attach")) {
        score /= 2;
      }
      return score;
    }

    [MenuItem("Tools/Factory Wars/Assets/2. Scan Pack Folder…")]
    public static void ScanMenu() {
      var folder = EditorUtility.OpenFolderPanel("에셋 팩 폴더(Assets 안)", Application.dataPath, "");
      if (string.IsNullOrEmpty(folder)) {
        return;
      }
      var relative = ToAssetPath(folder);
      if (relative == null) {
        EditorUtility.DisplayDialog("Factory Wars", "Assets 폴더 안의 팩 폴더를 고르세요. 먼저 unitypackage를 가져오세요.", "확인");
        return;
      }
      var result = Scan(relative, true);
      EditorUtility.RevealInFinder(result.CsvPath);
    }

    /// <summary>배치 모드: -executeMethod FactoryWars.EditorTools.VisualCatalogTools.ScanBatch -packFolder Assets/…</summary>
    public static void ScanBatch() {
      var args = Environment.GetCommandLineArgs();
      var index = Array.IndexOf(args, "-packFolder");
      var folder = index >= 0 && index + 1 < args.Length ? args[index + 1] : "Assets/ExternalAssets";
      Scan(folder, true);
    }

    public class ScanResult {
      public string[] Prefabs;
      public string CsvPath;
      public string SheetPath;
      public int Suggested;
    }

    public static string ToAssetPath(string full) {
      full = full.Replace('\\', '/');
      var data = Application.dataPath.Replace('\\', '/');
      return full.StartsWith(data) ? "Assets" + full.Substring(data.Length) : null;
    }

    public static ScanResult Scan(string folder, bool sheet) {
      var guids = AssetDatabase.FindAssets("t:Prefab", new[] { folder });
      var models = AssetDatabase.FindAssets("t:Model", new[] { folder });
      var prefabs = guids.Concat(models).Select(AssetDatabase.GUIDToAssetPath).Distinct().OrderBy(p => p, StringComparer.Ordinal).ToArray();
      var catalog = BuildSlots();
      var log = LogFolder();
      var list = Path.Combine(log, "pack-prefabs.txt");
      File.WriteAllLines(list, prefabs);

      var csv = new StringBuilder();
      csv.AppendLine(CsvHeader);
      var suggested = 0;
      foreach (var slot in catalog.Slots) {
        var ranked = prefabs.Select(p => (Path: p, Score: Score(slot.Id, p))).Where(c => c.Score > 0).OrderByDescending(c => c.Score).ThenBy(c => c.Path.Length).Take(3).ToArray();
        var current = string.Join("|", slot.Prefabs.Where(p => p != null).Select(AssetDatabase.GetAssetPath));
        var pick = current.Length > 0 ? current : ranked.Length > 0 ? ranked[0].Path : "";
        suggested += current.Length == 0 && pick.Length > 0 ? 1 : 0;
        var alternatives = string.Join("|", ranked.Skip(1).Select(c => c.Path));
        csv.AppendLine(Row(slot, pick, alternatives));
      }
      var csvPath = Path.Combine(log, "visual-slots.csv");
      File.WriteAllText(csvPath, csv.ToString(), new UTF8Encoding(true));
      var sheetPath = sheet ? ContactSheet(prefabs, Path.Combine(log, "pack-contact-sheet.png")) : null;
      Debug.Log($"[FW-Assets] 프리팹 {prefabs.Length}개, 후보 제안 {suggested}개 → {csvPath}");
      return new ScanResult { Prefabs = prefabs, CsvPath = csvPath, SheetPath = sheetPath, Suggested = suggested };
    }

    // ---------- 3. CSV ----------

    public const string CsvHeader = "Id,Category,Prefabs,Alternatives,Scale,AutoFit,YawOffset,TeamColor,TeamToken,RealWorld,Notes";

    static string Row(VisualSlot slot, string prefabs, string alternatives) {
      string Q(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
      return string.Join(",", Q(slot.Id), slot.Category, Q(prefabs), Q(alternatives), slot.Scale.ToString("0.###", CultureInfo.InvariantCulture),
        slot.AutoFit ? "1" : "0", slot.YawOffset.ToString("0.#", CultureInfo.InvariantCulture), slot.TeamColor, Q(slot.TeamMaterialToken), slot.RealWorld, Q(slot.Notes));
    }

    [MenuItem("Tools/Factory Wars/Assets/3. Import Slot CSV…")]
    public static void ImportMenu() {
      var path = EditorUtility.OpenFilePanel("슬롯 CSV", LogFolder(), "csv");
      if (string.IsNullOrEmpty(path) == false) {
        var (filled, missing) = ImportCsv(path);
        EditorUtility.DisplayDialog("Factory Wars", $"채운 슬롯 {filled}개, 찾지 못한 프리팹 {missing}개(콘솔 참고)", "확인");
      }
    }

    /// <summary>배치 모드: -executeMethod FactoryWars.EditorTools.VisualCatalogTools.ImportBatch -slotCsv 경로</summary>
    public static void ImportBatch() {
      var args = Environment.GetCommandLineArgs();
      var index = Array.IndexOf(args, "-slotCsv");
      ImportCsv(index >= 0 && index + 1 < args.Length ? args[index + 1] : Path.Combine(LogFolder(), "visual-slots.csv"));
    }

    /// <summary>CSV를 슬롯에 넣는다. Prefabs 칸이 비면 슬롯을 비운다(지금 모델로 돌아감). (채운 수, 못 찾은 프리팹 수)</summary>
    public static (int Filled, int Missing) ImportCsv(string path) {
      var catalog = BuildSlots();
      var lines = File.ReadAllLines(path, Encoding.UTF8);
      var header = ParseCsvLine(lines[0]);
      int Col(string name) => Array.IndexOf(header, name);
      var filled = 0;
      var missing = 0;
      for (var l = 1; l < lines.Length; l++) {
        if (string.IsNullOrWhiteSpace(lines[l])) {
          continue;
        }
        var cells = ParseCsvLine(lines[l]);
        string Cell(string name) => Col(name) >= 0 && Col(name) < cells.Length ? cells[Col(name)].Trim() : "";
        var id = Cell("Id");
        var slot = catalog.Get(id);
        if (slot == null) {
          slot = new VisualSlot { Id = id };
          catalog.Slots.Add(slot);
          catalog.Invalidate();
        }
        if (Enum.TryParse<VisualCategory>(Cell("Category"), out var category)) {
          slot.Category = category;
        }
        slot.Prefabs = new List<GameObject>();
        foreach (var prefabPath in Cell("Prefabs").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)) {
          var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath.Trim());
          if (prefab == null) {
            missing++;
            Debug.LogWarning($"[FW-Assets] {id}: 프리팹 없음 {prefabPath}");
            continue;
          }
          slot.Prefabs.Add(prefab);
        }
        if (float.TryParse(Cell("Scale"), NumberStyles.Float, CultureInfo.InvariantCulture, out var scale)) {
          slot.Scale = scale;
        }
        slot.AutoFit = Cell("AutoFit") != "0";
        if (float.TryParse(Cell("YawOffset"), NumberStyles.Float, CultureInfo.InvariantCulture, out var yaw)) {
          slot.YawOffset = yaw;
        }
        if (Enum.TryParse<TeamColorMode>(Cell("TeamColor"), out var team)) {
          slot.TeamColor = team;
        }
        if (Cell("TeamToken").Length > 0) {
          slot.TeamMaterialToken = Cell("TeamToken");
        }
        if (Enum.TryParse<RealWorldCheck>(Cell("RealWorld"), out var real)) {
          slot.RealWorld = real;
        }
        slot.Notes = Cell("Notes");
        Measure(slot);
        filled += slot.IsFilled ? 1 : 0;
      }
      catalog.Invalidate();
      EditorUtility.SetDirty(catalog);
      AssetDatabase.SaveAssets();
      Debug.Log($"[FW-Assets] CSV 가져오기: 채운 슬롯 {filled}, 못 찾은 프리팹 {missing}");
      return (filled, missing);
    }

    /// <summary>첫 프리팹의 바닥 보정·크기를 슬롯에 저장한다(게임 중에 다시 재지 않게).</summary>
    public static void Measure(VisualSlot slot) {
      if (slot.Prefab == null) {
        slot.PivotOffset = Vector3.zero;
        slot.Size = Vector3.zero;
        return;
      }
      var bounds = VisualCatalog.LocalBounds(slot.Prefab);
      slot.PivotOffset = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
      slot.Size = bounds.size;
    }

    public static string[] ParseCsvLine(string line) {
      var cells = new List<string>();
      var cell = new StringBuilder();
      var quoted = false;
      for (var i = 0; i < line.Length; i++) {
        var c = line[i];
        if (quoted) {
          if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') {
            cell.Append('"');
            i++;
          } else if (c == '"') {
            quoted = false;
          } else {
            cell.Append(c);
          }
        } else if (c == '"') {
          quoted = true;
        } else if (c == ',') {
          cells.Add(cell.ToString());
          cell.Clear();
        } else {
          cell.Append(c);
        }
      }
      cells.Add(cell.ToString());
      return cells.ToArray();
    }

    [MenuItem("Tools/Factory Wars/Assets/Export Current Slots CSV")]
    public static string ExportCsv() {
      var catalog = BuildSlots();
      var csv = new StringBuilder();
      csv.AppendLine(CsvHeader);
      foreach (var slot in catalog.Slots) {
        csv.AppendLine(Row(slot, string.Join("|", slot.Prefabs.Where(p => p != null).Select(AssetDatabase.GetAssetPath)), ""));
      }
      var path = Path.Combine(LogFolder(), "visual-slots-current.csv");
      File.WriteAllText(path, csv.ToString(), new UTF8Encoding(true));
      return path;
    }

    // ---------- 4. URP 머티리얼 ----------

    [MenuItem("Tools/Factory Wars/Assets/4. Convert Pack Materials to URP…")]
    public static void ConvertMenu() {
      var folder = ToAssetPath(EditorUtility.OpenFolderPanel("에셋 팩 폴더(Assets 안)", Application.dataPath, "") ?? "");
      if (folder != null) {
        var count = ConvertMaterials(folder);
        EditorUtility.DisplayDialog("Factory Wars", $"URP로 바꾼 머티리얼 {count}개", "확인");
      }
    }

    /// <summary>배치 모드: -executeMethod FactoryWars.EditorTools.VisualCatalogTools.ConvertBatch -packFolder Assets/…</summary>
    public static void ConvertBatch() {
      var args = Environment.GetCommandLineArgs();
      var index = Array.IndexOf(args, "-packFolder");
      ConvertMaterials(index >= 0 && index + 1 < args.Length ? args[index + 1] : "Assets/ExternalAssets");
    }

    /// <summary>
    /// 내장(Standard·Legacy·Mobile) 셰이더나 없는 셰이더를 쓰는 머티리얼을 URP Lit(또는 Simple Lit)으로 바꾼다.
    /// 기본 텍스처·색·노멀·발광·알파 컷을 옮긴다. 바꾼 수를 돌려준다.
    /// </summary>
    public static int ConvertMaterials(string folder) {
      var lit = Shader.Find("Universal Render Pipeline/Lit");
      if (lit == null) {
        Debug.LogError("[FW-Assets] URP Lit 셰이더가 없음");
        return 0;
      }
      var count = 0;
      foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { folder })) {
        var path = AssetDatabase.GUIDToAssetPath(guid);
        // 구입한 팩(Synty) 원본은 고치지 않는다(항목 32 사용자 결정). 팩 머티리얼은 Shader Graph라 URP에서 바로 그려진다.
        if (path.StartsWith("Assets/ExternalAssets/Synty", StringComparison.Ordinal)) {
          continue;
        }
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null || NeedsConversion(material) == false) {
          continue;
        }
        ConvertMaterial(material, lit);
        EditorUtility.SetDirty(material);
        count++;
      }
      AssetDatabase.SaveAssets();
      Debug.Log($"[FW-Assets] URP 변환 {count}개 ({folder})");
      return count;
    }

    public static bool NeedsConversion(Material material) {
      var shader = material.shader;
      if (shader == null || shader.name == "Hidden/InternalErrorShader") {
        return true;
      }
      var name = shader.name;
      return name == "Standard" || name == "Standard (Specular setup)" || name.StartsWith("Legacy Shaders/") || name.StartsWith("Mobile/") || name.StartsWith("Unlit/") || name.StartsWith("Nature/") || shader.isSupported == false;
    }

    public static void ConvertMaterial(Material material, Shader lit) {
      var texture = material.HasProperty("_MainTex") ? material.GetTexture("_MainTex") : null;
      var color = material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;
      var normal = material.HasProperty("_BumpMap") ? material.GetTexture("_BumpMap") : null;
      var emission = material.HasProperty("_EmissionMap") ? material.GetTexture("_EmissionMap") : null;
      var emissionColor = material.HasProperty("_EmissionColor") ? material.GetColor("_EmissionColor") : Color.black;
      var cutoff = material.HasProperty("_Cutoff") ? material.GetFloat("_Cutoff") : 0.5f;
      var cutout = material.IsKeywordEnabled("_ALPHATEST_ON") || material.renderQueue == 2450;
      material.shader = lit;
      if (texture != null) {
        material.SetTexture("_BaseMap", texture);
      }
      material.SetColor("_BaseColor", color);
      if (normal != null) {
        material.SetTexture("_BumpMap", normal);
        material.EnableKeyword("_NORMALMAP");
      }
      if (emission != null || emissionColor.maxColorComponent > 0.01f) {
        material.SetTexture("_EmissionMap", emission);
        material.SetColor("_EmissionColor", emissionColor);
        material.EnableKeyword("_EMISSION");
      }
      if (cutout) {
        material.SetFloat("_AlphaClip", 1f);
        material.SetFloat("_Cutoff", cutoff);
        material.EnableKeyword("_ALPHATEST_ON");
      }
      material.SetFloat("_Smoothness", 0.2f);
    }

    // ---------- 접촉 시트 ----------

    const int Cell = 160;
    const int Columns = 12;

    /// <summary>프리팹마다 미리보기 한 칸(이름은 pack-prefabs.txt의 줄 번호와 같은 순서). 그래픽 장치가 없으면 건너뛴다.</summary>
    public static string ContactSheet(string[] prefabs, string output) {
      if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null || prefabs.Length == 0) {
        return null;
      }
      var shown = prefabs.Take(240).ToArray();
      var rows = Mathf.CeilToInt(shown.Length / (float)Columns);
      var sheet = new Texture2D(Columns * Cell, rows * Cell, TextureFormat.RGB24, false);
      var fill = Enumerable.Repeat(new Color32(40, 44, 52, 255), sheet.width * sheet.height).ToArray();
      sheet.SetPixels32(fill);
      var target = new RenderTexture(Cell, Cell, 24);
      var cameraObject = new GameObject("ContactSheetCamera") { hideFlags = HideFlags.HideAndDontSave };
      var camera = cameraObject.AddComponent<Camera>();
      camera.clearFlags = CameraClearFlags.SolidColor;
      camera.backgroundColor = new Color(0.82f, 0.85f, 0.9f);
      camera.targetTexture = target;
      camera.enabled = false;
      var lightObject = new GameObject("ContactSheetLight") { hideFlags = HideFlags.HideAndDontSave };
      var light = lightObject.AddComponent<Light>();
      light.type = LightType.Directional;
      light.transform.rotation = Quaternion.Euler(50, -30, 0);
      var read = new Texture2D(Cell, Cell, TextureFormat.RGB24, false);
      var offset = new Vector3(10000, 0, 10000);
      for (var i = 0; i < shown.Length; i++) {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(shown[i]);
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
          camera.transform.position = bounds.center + new Vector3(1f, 0.8f, -1f).normalized * radius * 2.6f;
          camera.transform.LookAt(bounds.center);
          camera.nearClipPlane = 0.01f;
          camera.farClipPlane = radius * 10f;
          camera.Render();
          RenderTexture.active = target;
          read.ReadPixels(new Rect(0, 0, Cell, Cell), 0, 0);
          read.Apply();
          RenderTexture.active = null;
          var x = i % Columns * Cell;
          var y = (rows - 1 - i / Columns) * Cell;
          sheet.SetPixels(x, y, Cell, Cell, read.GetPixels());
        }
        UnityEngine.Object.DestroyImmediate(item);
      }
      sheet.Apply();
      File.WriteAllBytes(output, sheet.EncodeToPNG());
      UnityEngine.Object.DestroyImmediate(cameraObject);
      UnityEngine.Object.DestroyImmediate(lightObject);
      UnityEngine.Object.DestroyImmediate(target);
      return output;
    }
  }
}
