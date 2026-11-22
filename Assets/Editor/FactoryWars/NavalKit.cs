namespace FactoryWars.EditorTools {
  using System.Collections.Generic;
  using System.Linq;
  using FactoryWars.View;
  using UnityEditor;
  using UnityEngine;

  /// <summary>
  /// 포격함(ff/cf_artillery_ship) 조립 키트(2026-11-22 교체). 예전 Synty 고무보트(SM_Veh_Boat_01, 8.4m) 늘이기 대신
  /// 프로젝트에 있는 Kenney Watercraft(CC0) 화물선 빈 선체(ship-cargo-c, 10.6m)를 1.3배(약 13.7m)로 놓고,
  /// Synty Military 다연장 발사대 2기(함수·함미), 장갑 함교(통제실), 레이더·안테나 마스트를 얹는다.
  /// Kenney 선체는 원본 colormap 그대로, Synty 부품만 진영 색 변형을 쓴다. 원본 팩 파일은 건드리지 않고 우리 폴더에 키트 프리팹만 저장한다.
  /// 배치: -executeMethod FactoryWars.EditorTools.SyntyAssign.RebuildArtilleryShips
  /// </summary>
  public static partial class SyntyAssign {
    const string ShipHull = "Assets/ExternalAssets/Kenney/watercraft-pack/ship-cargo-c.fbx";
    const float ShipHullScale = 1.3f;
    /// <summary>선체 갑판 높이(배율 적용 후). 부품을 이 위에 얹는다.</summary>
    const float ShipDeck = 1.62f;
    /// <summary>화면 길이(m). 슬롯 배율이 이 길이에 맞춘다.</summary>
    const float ShipSpan = 14f;

    static IEnumerable<(string Path, Vector3 Pos, Vector3 Rot, float Scale)> ArtilleryShipParts() {
      const string PM = PackRoot + "/PolygonMilitary/Prefabs/";
      yield return (ShipHull, Vector3.zero, Vector3.zero, ShipHullScale);
      // 함수 갑판(앞 갑판 위) 다연장 발사대 1기, 함미 발사대 1기(뒤를 향한다).
      yield return (PM + "Props/Military/SM_Prop_Missile_Launcher_02.prefab", new Vector3(0f, ShipDeck, 4.3f), Vector3.zero, 0.62f);
      yield return (PM + "Props/Military/SM_Prop_Missile_Launcher_02.prefab", new Vector3(0f, ShipDeck, -4.7f), new Vector3(0f, 180f, 0f), 0.6f);
      // 장갑 함교 + 레이더 마스트 + 안테나.
      yield return (PM + "Props/Military/SM_Prop_Drone_Control_Room_01.prefab", new Vector3(0f, ShipDeck, -1.4f), Vector3.zero, 0.46f);
      yield return (PM + "Props/Military/SM_Prop_Mobile_Radar_01.prefab", new Vector3(0f, ShipDeck + 1.5f, -1.6f), Vector3.zero, 0.42f);
      yield return (PM + "Props/SM_Prop_Aerial_01.prefab", new Vector3(0.8f, ShipDeck + 1.35f, -2.3f), Vector3.zero, 0.8f);
    }

    /// <summary>진영별 함정 회색 단색 머티리얼(URP Lit, 텍스처 없음). 자유 연방은 푸른 회색, 대륙 연방은 녹회색.</summary>
    static Material HullMaterial(string faction) {
      var folder = $"{OutRoot}/Materials";
      if (AssetDatabase.IsValidFolder(folder) == false) {
        AssetDatabase.CreateFolder(OutRoot, "Materials");
      }
      var path = $"{folder}/Naval_Hull_{faction}.mat";
      var material = AssetDatabase.LoadAssetAtPath<Material>(path);
      if (material == null) {
        material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        AssetDatabase.CreateAsset(material, path);
      }
      var color = faction == "ff" ? new Color(0.43f, 0.47f, 0.52f) : new Color(0.41f, 0.44f, 0.40f);
      material.SetColor("_BaseColor", color);
      material.color = color;
      material.SetFloat("_Smoothness", 0.28f);
      material.SetFloat("_Metallic", 0.15f);
      EditorUtility.SetDirty(material);
      return material;
    }

    static GameObject AssembleArtilleryShip(string faction) {
      var swap = faction == "ff" ? PropsFree : PropsContinental;
      var map = new Dictionary<string, Material>();
      for (var i = 0; i + 1 < swap.Length; i += 2) {
        var mat = FindMaterial(swap[i + 1]);
        if (mat != null) map[swap[i]] = mat;
      }
      var root = new GameObject($"{faction}_artillery_ship_kit");
      var index = 0;
      foreach (var part in ArtilleryShipParts()) {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(part.Path);
        if (source == null) {
          Debug.LogWarning($"[FW-Naval] 부품 없음: {part.Path}");
          index++;
          continue;
        }
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(source, root.transform) ?? Object.Instantiate(source, root.transform);
        instance.name = $"Part{index}_{source.name}";
        instance.transform.localPosition = part.Pos;
        instance.transform.localRotation = Quaternion.Euler(part.Rot);
        instance.transform.localScale = Vector3.one * part.Scale;
        if (part.Path == ShipHull) {
          var hull = HullMaterial(faction);
          foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true)) {
            renderer.sharedMaterials = Enumerable.Repeat(hull, renderer.sharedMaterials.Length).ToArray();
          }
        } else {
          foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true)) {
            var mats = renderer.sharedMaterials;
            var touched = false;
            for (var m = 0; m < mats.Length; m++) {
              if (mats[m] != null && map.TryGetValue(mats[m].name, out var to)) {
                mats[m] = to;
                touched = true;
              }
            }
            if (touched) renderer.sharedMaterials = mats;
          }
        }
        index++;
      }
      var saved = PrefabUtility.SaveAsPrefabAsset(root, $"{OutRoot}/Prefabs/{faction}_artillery_ship_kit.prefab");
      Object.DestroyImmediate(root);
      return saved;
    }

    static void BuildArtilleryShip(VisualCatalog catalog, string faction) {
      var prefab = AssembleArtilleryShip(faction);
      if (prefab == null) {
        return;
      }
      var slotId = $"unit/{faction}_artillery_ship";
      var slot = catalog.Slots.FirstOrDefault(s => s != null && s.Id == slotId);
      if (slot == null) {
        slot = new VisualSlot { Id = slotId };
        catalog.Slots.Add(slot);
      }
      var (pivot, size) = MeasureActive(prefab);
      slot.Category = VisualCategory.Unit;
      slot.Prefabs = new List<GameObject> { prefab };
      slot.PivotOffset = pivot;
      slot.Size = size;
      slot.YawOffset = 0f;
      slot.TeamColor = TeamColorMode.None;
      slot.TeamTextures = new Texture2D[0];
      slot.RealWorld = RealWorldCheck.Clear;
      slot.OwnerRing = true;
      slot.Animator = null;
      slot.AutoFit = false;
      slot.Scale = ShipSpan / Mathf.Max(0.1f, Mathf.Max(size.x, size.z));
      slot.SpinParts = new[] { "Radar:Y" };
      slot.Notes = "Kenney Watercraft cargo hull (CC0) + Synty Military launchers, bridge, radar mast";
      Debug.Log($"[FW-Naval] {slotId} size={size} scale={slot.Scale:0.###} span={Mathf.Max(size.x, size.z) * slot.Scale:0.#}m");
    }

    [MenuItem("Tools/Factory Wars/Assets/Rebuild Artillery Ships")]
    public static void RebuildArtilleryShips() {
      var catalog = Resources.Load<VisualCatalog>(VisualCatalog.ResourcePath);
      foreach (var faction in new[] { "ff", "cf" }) {
        BuildArtilleryShip(catalog, faction);
      }
      catalog.Invalidate();
      EditorUtility.SetDirty(catalog);
      AssetDatabase.SaveAssets();
    }
  }
}
