namespace FactoryWars.EditorTools {
  using System;
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using FactoryWars.View;
  using UnityEditor;
  using UnityEngine;
  using UnityEngine.Rendering;
  using Object = UnityEngine.Object;

  /// <summary>
  /// Map4P v4 꾸미기 블록(PROMPT-MAP4P-V4-DRESS 2-B). 보유 Synty 팩 조각만 골라 조립하고(원본 무수정), 블록마다 메시 하나(재질별 서브메시)로 굽는다.
  /// 블록 좌표: 원점 = 블록 바닥 가운데, +X 동, +Z 북, <b>정면 = -Z(남쪽, 화면 아래)</b>. GridDress가 칸 지도에 맞춰 90도 단위로 돌려 놓는다.
  /// 바닥 포장(인도·잔디·주차선)은 블록이 아니라 GridPaint가 맵 바닥에 칠한다(그리기 호출 절약).
  /// 출력: Assets/FactoryWarsArt/Blocks/Dress/*.prefab(gitignore, 메뉴로 재생성) + VisualCatalog "dress4p/{종류}_{번호}".
  /// 종류: H1 1×1 / HW 2×1(가로) / HD 1×2(세로) / H4 2×2 도시 · K 공원 · L 주차 · X 잔해 · Y 컨테이너 · Q 부두 · M 자원지 설비
  ///       · E 경계 고층 · EL 경계 저층 · EI 경계 안쪽 방벽 · SK 맵 밖 스카이라인 · CW 요새 담장 8m · CP 담장 모서리 · CT 정문 감시탑 · CB 요새 건물 2×2 · GR 다리 난간.
  /// 실행: -executeMethod FactoryWars.EditorTools.DressKit.BuildAll
  /// </summary>
  public static class DressKit {
    public const string Out = BlockKit.OutFolder + "/Dress";
    const string PC = "Assets/ExternalAssets/Synty/PolygonCity/Prefabs/";
    const string PM = "Assets/ExternalAssets/Synty/PolygonMilitary/Prefabs/";
    const string PK = "Assets/ExternalAssets/Synty/PolygonConstruction/Prefabs/";
    const string PB = "Assets/ExternalAssets/Synty/PolygonBattleRoyale/Prefabs/";
    const string PG = "Assets/ExternalAssets/Synty/PolygonGeneric/Prefabs/";
    static readonly List<string> Log = new List<string>();
    static readonly Dictionary<string, float> FrontCache = new Dictionary<string, float>();

    // ------------------------------------------------------------ front detection

    static IEnumerable<MeshFilter> Lod0Filters(GameObject item) {
      var skip = new HashSet<Renderer>();
      foreach (var group in item.GetComponentsInChildren<LODGroup>()) {
        var lods = group.GetLODs();
        for (var i = 1; i < lods.Length; i++) foreach (var r in lods[i].renderers) if (r != null) skip.Add(r);
      }
      foreach (var f in item.GetComponentsInChildren<MeshFilter>()) {
        var r = f.GetComponent<MeshRenderer>();
        if (r == null || !r.enabled || skip.Contains(r) || f.sharedMesh == null) continue;
        yield return f;
      }
    }

    /// <summary>프리팹 정면 방향을 -Z로 돌리는 Y 각도. 유리 재질이 있으면 유리 면 방향, 없으면 세부(삼각형 수)가 가장 많은 수평 면.</summary>
    static float Front(string path) {
      if (FrontCache.TryGetValue(path, out var cached)) return cached;
      var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
      var yaw = 0f;
      if (source != null) {
        var item = (GameObject)PrefabUtility.InstantiatePrefab(source);
        item.transform.position = Vector3.zero;
        var glass = Vector3.zero;
        var counts = new float[4];
        foreach (var f in Lod0Filters(item)) {
          var mesh = f.sharedMesh;
          var mats = f.GetComponent<MeshRenderer>().sharedMaterials;
          var v = mesh.vertices;
          var m = f.transform.localToWorldMatrix;
          for (var s = 0; s < mesh.subMeshCount; s++) {
            var isGlass = s < mats.Length && mats[s] != null && mats[s].name.IndexOf("Glass", StringComparison.OrdinalIgnoreCase) >= 0;
            var t = mesh.GetTriangles(s);
            for (var i = 0; i + 2 < t.Length; i += 3) {
              var a = m.MultiplyPoint3x4(v[t[i]]);
              var n = Vector3.Cross(m.MultiplyPoint3x4(v[t[i + 1]]) - a, m.MultiplyPoint3x4(v[t[i + 2]]) - a);
              var area = n.magnitude;
              if (area < 1e-6f) continue;
              n /= area;
              if (isGlass) { glass += new Vector3(n.x, 0, n.z) * area; continue; }
              if (n.x > 0.8f) counts[0]++; else if (n.x < -0.8f) counts[1]++; else if (n.z > 0.8f) counts[2]++; else if (n.z < -0.8f) counts[3]++;
            }
          }
        }
        Object.DestroyImmediate(item);
        Vector3 dir;
        if (glass.sqrMagnitude > 0.5f) {
          dir = Mathf.Abs(glass.x) > Mathf.Abs(glass.z) ? new Vector3(Mathf.Sign(glass.x), 0, 0) : new Vector3(0, 0, Mathf.Sign(glass.z));
        } else {
          var best = Array.IndexOf(counts, counts.Max());
          dir = new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back }[best];
        }
        yaw = Mathf.Round(Vector3.SignedAngle(dir, Vector3.back, Vector3.up));
        Log.Add($"front {Path.GetFileNameWithoutExtension(path)}: {(glass.sqrMagnitude > 0.5f ? "glass" : "detail")} {dir} -> yaw {yaw}");
      }
      FrontCache[path] = yaw;
      return yaw;
    }

    /// <summary>City 모듈 건물(상가·아파트 층)은 모두 같은 방향으로 쌓으므로 상가 유리 방향 하나로 정한다.</summary>
    static float CityFront => Front(PC + "Buildings/SM_Bld_Shop_01.prefab");

    // ------------------------------------------------------------ builder

    sealed class B {
      public Transform Root;
      public string Id;
      public float HalfX, HalfZ, Margin = 0.6f;

      public GameObject Prop(string path, float x, float z, float yaw = 0f, float y = 0f) =>
        BlockKit.Place(Root, path, new Vector3(x, y, z), yaw, 1f);

      /// <summary>건물: 정면 자동 보정 + 블록 안에 들어가는지 확인(통행 벽 0.6m 안쪽).</summary>
      public GameObject Bld(string path, float x, float z, float turn = 0f, float y = 0f) {
        var g = Prop(path, x, z, Front(path) + turn, y);
        Fit(g);
        return g;
      }

      public void Fit(GameObject g) {
        if (g == null) return;
        var b = BlockKit.WorldBounds(g);
        var o = Root.position;
        if (b.min.x - o.x < -HalfX + Margin - 0.15f || b.max.x - o.x > HalfX - Margin + 0.15f ||
            b.min.z - o.z < -HalfZ + Margin - 0.15f || b.max.z - o.z > HalfZ - Margin + 0.15f) {
          Log.Add($"WARN {Id}: {g.name} bounds x[{b.min.x - o.x:0.0},{b.max.x - o.x:0.0}] z[{b.min.z - o.z:0.0},{b.max.z - o.z:0.0}] outside ±{HalfX - Margin:0.0}/±{HalfZ - Margin:0.0}");
        }
      }

      /// <summary>City 모듈 건물 한 채(5×5m): 1층 + 위층 n개 + 지붕. 높이를 재서 쌓는다. 반환 = 지붕 아래 높이.</summary>
      public float Stack(float x, float z, string ground, string floor, int floors, string roof, string extraTop = null) {
        var yaw = CityFront;
        var y = 0f;
        var first = Prop(PC + "Buildings/" + ground + ".prefab", x, z, yaw, y);
        Fit(first);
        y += first != null ? BlockKit.WorldBounds(first).size.y : 3f;
        for (var i = 0; i < floors; i++) {
          var name = i == floors - 1 && extraTop != null ? extraTop : floor;
          var piece = Prop(PC + "Buildings/" + name + ".prefab", x, z, yaw, y);
          y += piece != null ? BlockKit.WorldBounds(piece).size.y : 3f;
        }
        if (!string.IsNullOrEmpty(roof)) Prop(PC + "Buildings/" + roof + ".prefab", x, z, yaw, y);
        return y;
      }
    }

    sealed class Spec {
      public string Id;
      public float SizeX, SizeZ;
      public Action<B> Build;
      public Spec(string id, float sx, float sz, Action<B> build) { Id = id; SizeX = sx; SizeZ = sz; Build = build; }
    }

    // ------------------------------------------------------------ shared prop sets

    static string P(string name) => PC + "Props/SM_Prop_" + name + ".prefab";
    static string MP(string name) => PM + "Props/SM_Prop_" + name + ".prefab";
    static string MM(string name) => PM + "Props/Military/SM_Prop_" + name + ".prefab";
    static string MD(string name) => PM + "Props/Debris/SM_Prop_" + name + ".prefab";
    static string ME(string name) => PM + "Environment/SM_Env_" + name + ".prefab";
    static string MB(string name) => PM + "Buildings/SM_Bld_" + name + ".prefab";
    static string KP(string name) => PK + "Props/SM_Prop_" + name + ".prefab";
    static string CV(string name) => PC + "Vehicles/SM_Veh_" + name + ".prefab";
    static string MV(string name) => PM + "Vehicles/Destroyed/SM_Veh_" + name + ".prefab";

    /// <summary>인도 앞줄(z = 앞 가장자리 + 0.6) 소품: 변형마다 다른 조합·위치(일정 간격 줄 금지).</summary>
    static void Street(B b, int v, float halfX, float frontZ) {
      var z = frontZ + 0.55f;
      switch (v % 6) {
        case 0: b.Prop(P("LightPole_Base_01"), -halfX + 1.1f, z, 90f); b.Prop(P("Hydrant_01"), 1.3f, z); b.Prop(P("TrashCan_01"), halfX - 1.6f, z + 0.1f); break;
        case 1: b.Prop(P("Planter_01"), -halfX * 0.35f, z + 0.1f); b.Prop(P("Mailbox_01"), halfX - 1.0f, z); break;
        case 2: b.Prop(P("LightPole_Base_01"), halfX - 1.4f, z, 90f); b.Prop(P("ParkBench_01"), -halfX + 2.4f, z + 0.15f, 180f); break;
        case 3: b.Prop(P("Newspaper_01"), -0.8f, z); b.Prop(P("PowerBox_01"), -halfX + 0.9f, z + 0.2f); b.Prop(P("Planter_02"), halfX - 1.9f, z + 0.1f); break;
        case 4: b.Prop(P("ParkingMeter_01"), -halfX + 1.8f, z - 0.1f); b.Prop(P("LightPole_Base_01"), 0.6f, z, 90f); break;
        default: b.Prop(P("TrashCan_01"), -halfX + 1.3f, z + 0.1f); b.Prop(P("Hydrant_01"), halfX - 2.3f, z); break;
      }
    }

    // ------------------------------------------------------------ block specs

    static List<Spec> Specs() {
      var list = new List<Spec>();
      void Add(string id, float sx, float sz, Action<B> build) => list.Add(new Spec(id, sx, sz, build));

      // ---- H1: 1×1 도시 건물(8×8). 건물 정면 z ≈ -2.1, 앞 인도 1.9m.
      var h1 = new (string G, string F, int N, string R, string Top)[] {
        ("SM_Bld_Shop_01", "SM_Bld_Apartment_01", 2, "SM_Bld_Apartment_Roof_01", null),
        ("SM_Bld_Shop_02", "SM_Bld_Apartment_02", 1, "SM_Bld_Apartment_Roof_02", null),
        ("SM_Bld_Shop_04", "SM_Bld_Apartment_03", 3, "SM_Bld_Apartment_Roof_03", null),
        ("SM_Bld_Shop_Corner_01", "SM_Bld_Apartment_Corner_02", 2, "SM_Bld_Apartment_Roof_Corner_02", null),
        ("SM_Bld_Shop_05", "SM_Bld_Apartment_Stack_01", 1, "SM_Bld_Apartment_Roof_01", null),
        ("SM_Bld_Shop_06", "SM_Bld_Apartment_01", 2, "SM_Bld_Apartment_Roof_03", "SM_Bld_Apartment_Stack_03"),
        ("SM_Bld_Shop_Corner_02", "SM_Bld_Apartment_Corner_03", 2, "SM_Bld_Apartment_Roof_Corner_03", null),
        ("SM_Bld_Apartment_Door_02", "SM_Bld_Apartment_Stack_02", 1, "SM_Bld_Apartment_Roof_02", null),
      };
      for (var i = 0; i < h1.Length; i++) {
        var s = h1[i];
        var v = i;
        Add($"H1_{v:00}", 8, 8, b => {
          b.Stack(0f, 0.45f, s.G, s.F, s.N, s.R, s.Top);
          if (v % 3 == 0) b.Prop(PC + "Props/SM_Prop_Roof_Aircon_01.prefab", 1.0f, 1.2f, 0, 3f * (s.N + 1) + 0.6f);
          Street(b, v, 4f, -4f);
        });
      }
      // H1S/H1T: 정면이 동·서인 1칸 건물. 카메라(남)에 보이는 옆면(H1S = 로컬 -X, H1T = 로컬 +X)에 비상계단·실외기·배관을 붙여 민벽을 없앤다.
      for (var i = 0; i < 6; i++) {
        var s = h1[i];
        var v = i;
        foreach (var (kind, side) in new[] { ("H1S", -1f), ("H1T", 1f) }) {
          Add($"{kind}_{v:00}", 8, 8, b => {
            var x = -side * 0.6f;
            b.Stack(x, 0.45f, s.G, s.F, s.N, s.R, s.Top);
            var wall = x + side * 2.55f;
            var turn = side < 0 ? 90f : -90f;
            var esc = PC + "Buildings/SM_Bld_FireEscape_0" + (2 + v % 2) + ".prefab";
            b.Prop(esc, wall + side * 0.8f, 0.5f, Front(esc) + turn, 3.0f);
            if (s.N >= 2 || s.F.Contains("Stack")) b.Prop(esc, wall + side * 0.8f, 0.5f, Front(esc) + turn, 6.7f);
            b.Prop(P("Aircon_01"), wall + side * 0.3f, -1.3f, Front(P("Aircon_01")) + turn, 2.2f);
            b.Prop(P("Pipe_Small_01"), wall + side * 0.15f, 2.6f, turn);
            Street(b, v + 1, 4f, -4f);
          });
        }
      }
      Add("H1_08", 8, 8, b => {
        b.Bld(MB("Village_House_05"), 0f, 0.9f);
        b.Prop(P("Planter_02"), -2.3f, -3.2f); b.Prop(MM("Barrel_01"), 2.9f, -2.6f); b.Prop(MM("Barrel_02"), 3.1f, -1.8f);
      });
      Add("H1_09", 8, 8, b => {
        b.Bld(PK + "Buildings/SM_Bld_Portable_Office_02.prefab", -0.4f, 1.2f);
        b.Prop(MM("Crate_Stack_02"), 2.5f, 1.6f);
        b.Prop(KP("Generator_Large_01"), 2.2f, -3.0f, 90f); b.Prop(P("Cone_01"), -2.8f, -3.3f); b.Prop(P("Cone_02"), -2.2f, -3.5f);
      });

      // ---- HW: 2×1(16×8). 연립: 높이 다른 두 채 + 사이 골목 소품.
      Add("HW_00", 16, 8, b => {
        b.Stack(-4.3f, 0.45f, "SM_Bld_Shop_01", "SM_Bld_Apartment_02", 3, "SM_Bld_Apartment_Roof_02");
        b.Stack(4.3f, 0.45f, "SM_Bld_Shop_Corner_01", "SM_Bld_Apartment_Corner_01", 1, "SM_Bld_Apartment_Roof_Corner_01");
        b.Prop(P("Skip_02"), 0f, 1.8f, 90f); Street(b, 0, 8f, -4f);
      });
      Add("HW_01", 16, 8, b => {
        b.Bld(MB("Shop_01"), -2.6f, 0.4f);
        b.Prop(MM("Portaloo_01"), 5.6f, 1.8f); b.Prop(KP("Generator_Large_01"), 4.8f, -1.0f, 0f);
        Street(b, 1, 8f, -4f);
      });
      Add("HW_02", 16, 8, b => {
        b.Bld(PC + "Buildings/SM_Bld_Station_03.prefab", -2.0f, 0.4f);
        b.Prop(ME("Tree_Round_01"), 5.2f, 0.5f); b.Prop(P("ParkBench_01"), 5.0f, -2.6f, 180f);
        Street(b, 2, 8f, -4f);
      });
      Add("HW_03", 16, 8, b => {
        b.Stack(-4.6f, 0.45f, "SM_Bld_Shop_05", "SM_Bld_Apartment_Stack_02", 1, "SM_Bld_Apartment_Roof_02");
        b.Stack(1.0f, 0.45f, "SM_Bld_Shop_02", "SM_Bld_Apartment_01", 0, "SM_Bld_Apartment_Roof_01");
        b.Prop(P("TrashBag_01"), 4.6f, 1.6f); b.Prop(P("Skip_01"), 5.6f, 0.6f, 90f);
        Street(b, 3, 8f, -4f);
      });
      Add("HW_04", 16, 8, b => {
        b.Bld(PB + "Buildings/SM_Bld_SmallBuilding_03.prefab", -2.0f, 0.1f, -90f);
        b.Prop(MM("Crate_Stack_01"), 5.8f, 1.6f); b.Prop(MP("Pallet_01"), 5.4f, -1.4f, 20f);
        Street(b, 4, 8f, -4f);
      });
      Add("HW_05", 16, 8, b => {
        b.Stack(-4.4f, 0.45f, "SM_Bld_Shop_06", "SM_Bld_Apartment_03", 4, "SM_Bld_Apartment_Roof_03");
        b.Stack(4.4f, 0.45f, "SM_Bld_Shop_04", "SM_Bld_Apartment_01", 2, "SM_Bld_Apartment_Roof_01");
        b.Prop(P("Washingline_01"), 0f, 1.0f); Street(b, 5, 8f, -4f);
      });

      // ---- HD: 1×2(8×16). 앞 건물 + 뒤 건물(높이 차).
      Add("HD_00", 8, 16, b => {
        b.Stack(0f, -3.6f, "SM_Bld_Shop_02", "SM_Bld_Apartment_01", 1, "SM_Bld_Apartment_Roof_01");
        b.Stack(0f, 3.9f, "SM_Bld_Apartment_Door_01", "SM_Bld_Apartment_Stack_02", 1, "SM_Bld_Apartment_Roof_02");
        Street(b, 0, 4f, -8f);
      });
      Add("HD_01", 8, 16, b => { b.Bld(MB("Shop_09"), 0f, 0.8f); Street(b, 1, 4f, -8f); b.Prop(P("Skip_02"), 2.0f, 6.8f, 90f); });
      Add("HD_02", 8, 16, b => { b.Bld(MB("Shop_10"), 0f, 0.8f); Street(b, 2, 4f, -8f); b.Prop(MM("Barrel_Stack_01"), -2.2f, 6.6f); });
      Add("HD_03", 8, 16, b => {
        b.Bld(MB("Village_House_04"), 0f, -3.6f);
        b.Prop(KP("Shipping_Container_Small_01"), -1.2f, 4.6f, 90f); b.Prop(ME("Tree_Small_02"), 2.0f, 4.4f);
        Street(b, 3, 4f, -8f);
      });

      // ---- H4: 2×2(16×16).
      Add("H4_00", 16, 16, b => { b.Bld(PC + "Buildings/SM_Bld_OfficeSquare_04.prefab", 0f, 0f); b.Prop(P("SatDish_01"), 3f, 3f, 0, 7.6f); });
      Add("H4_01", 16, 16, b => {
        var y = 0f;
        var base0 = b.Bld(PC + "Buildings/SM_Bld_OfficeOld_Small_Base_01.prefab", -1.6f, 1.6f);
        y += base0 != null ? BlockKit.WorldBounds(base0).size.y : 6f;
        for (var i = 0; i < 2; i++) { var f = b.Bld(PC + "Buildings/SM_Bld_OfficeOld_Small_Floor_01.prefab", -1.6f, 1.6f, 0f, y); y += f != null ? BlockKit.WorldBounds(f).size.y : 3.2f; }
        b.Bld(PC + "Buildings/SM_Bld_OfficeOld_Small_Roof_01.prefab", -1.6f, 1.6f, 0f, y);
        b.Prop(ME("Tree_Round_02"), 5.6f, -5.2f); b.Prop(P("ParkBench_01"), 1.6f, -6.4f, 180f); b.Prop(P("LightPole_Base_01"), -6.2f, -7.4f, 90f);
      });
      Add("H4_02", 16, 16, b => {
        b.Bld(MB("Shop_05"), -2.2f, 1.8f);
        b.Stack(4.6f, -3.9f, "SM_Bld_Shop_Corner_02", "SM_Bld_Apartment_Corner_02", 1, "SM_Bld_Apartment_Roof_Corner_02");
        Street(b, 2, 8f, -8f);
      });
      Add("H4_03", 16, 16, b => {
        b.Stack(-4.4f, -3.9f, "SM_Bld_Shop_01", "SM_Bld_Apartment_02", 1, "SM_Bld_Apartment_Roof_02");
        b.Stack(4.4f, -3.9f, "SM_Bld_Shop_Corner_01", "SM_Bld_Apartment_Corner_02", 3, "SM_Bld_Apartment_Roof_Corner_02");
        b.Stack(-4.4f, 4.4f, "SM_Bld_Apartment_Door_02", "SM_Bld_Apartment_Stack_03", 1, "SM_Bld_Apartment_Roof_03");
        b.Stack(4.4f, 4.4f, "SM_Bld_Shop_06", "SM_Bld_Apartment_01", 0, "SM_Bld_Apartment_Roof_01");
        b.Prop(PC + "Environments/SM_Env_Tree_02.prefab", 0f, 0.3f); Street(b, 3, 8f, -8f);
      });
      Add("H4_04", 16, 16, b => {
        b.Bld(MB("Shop_04"), -2.6f, 1.4f);
        b.Stack(4.6f, 3.8f, "SM_Bld_Shop_05", "SM_Bld_Apartment_03", 2, "SM_Bld_Apartment_Roof_03");
        b.Prop(CV("Car_Taxi_01"), 4.6f, -3.8f, 180f); Street(b, 4, 8f, -8f);
      });
      Add("H4_05", 16, 16, b => { b.Bld(PC + "Buildings/SM_Bld_OfficeSquare_02.prefab", 0f, 0f); b.Prop(P("Roof_Aircon_02"), -3f, 2f, 0, 9.05f); b.Prop(P("Roof_Aircon_03"), 3.5f, -2.5f, 90, 9.05f); });

      // ---- K 공원(8×8). 나무 군집 + 벤치·등.
      Add("K_00", 8, 8, b => { b.Prop(ME("Tree_Round_02"), -1.6f, 1.2f); b.Prop(ME("Tree_Small_01"), 2.2f, 2.4f); b.Prop(ME("Bush_Group_01"), 2.4f, -1.6f); b.Prop(P("ParkBench_01"), -1.2f, -2.6f, 180f); });
      Add("K_01", 8, 8, b => { b.Prop(ME("Tree_Round_03"), 0.6f, 0.8f); b.Prop(ME("Bush_Small_01"), -2.6f, -2.2f); b.Prop(ME("Bush_Small_02"), -2.0f, -2.8f); b.Prop(P("LightPole_Base_02"), 2.9f, -2.9f); });
      Add("K_02", 8, 8, b => { b.Prop(PC + "Environments/SM_Env_Tree_01.prefab", -2.3f, 2.0f); b.Prop(PC + "Environments/SM_Env_Tree_03.prefab", 2.0f, 2.6f); b.Prop(PC + "Environments/SM_Env_Tree_02.prefab", 2.6f, -1.3f); b.Prop(P("PicnicTable_01"), -1.2f, -1.2f, 15f); });
      Add("K_03", 8, 8, b => { b.Prop(ME("Tree_Small_03"), -2.0f, -0.6f); b.Prop(ME("Bush_Group_02"), 1.8f, 1.8f); b.Prop(P("ParkBench_01"), 2.2f, -2.4f, 200f); b.Prop(P("TrashCan_01"), 0.8f, -3.0f); });

      // ---- L 주차(8×8). 주차선은 GridPaint가 X 방향으로 칠한다(차는 남북 방향).
      var cars = new[] { "Car_Sedan_01", "Car_Van_01", "Car_Small_01", "Car_Taxi_01", "Car_Medium_01", "Car_Muscle_01" };
      Add("L_00", 8, 8, b => { b.Prop(CV("Car_Sedan_01"), -1.25f, 0.6f); b.Prop(P("ParkingMeter_01"), -3.4f, -3.3f); b.Prop(P("TrashCan_01"), 3.3f, 3.2f); });
      Add("L_01", 8, 8, b => { b.Prop(CV("Car_Van_01"), 1.35f, 0.5f, 180f); b.Prop(P("ParkingMeter_02"), 3.4f, -3.3f); b.Prop(P("Cone_01"), -1.3f, -1.6f); });
      Add("L_02", 8, 8, b => { b.Prop(CV("Car_Taxi_01"), -1.25f, 0.6f, 180f); b.Prop(CV("Car_Small_01"), 1.35f, 0.9f, 2f); });
      Add("L_03", 8, 8, b => { b.Prop(CV("Car_Muscle_01"), 1.35f, 0.4f, 0f); b.Prop(P("Skip_02"), -2.4f, 2.6f, 90f); b.Prop(P("TrashBag_02"), -1.2f, 2.9f); });
      // ---- X 잔해·엄폐(8×8, 통행 가능): 가운데 3m 길은 비운다.
      Add("X_00", 8, 8, b => { b.Prop(MD("DebrisPile_02"), -0.4f, 2.6f, 0f); b.Prop(MV("Pickup_Small_Destroyed_01"), -2.6f, -1.2f, 12f); b.Prop(MD("Rubble_Pile_04"), 2.6f, -2.4f, 40f); });
      Add("X_01", 8, 8, b => { b.Prop(MM("Barrier_Base_Row_02"), 0f, -2.9f, 0f); b.Prop(MD("Rubble_Pile_06"), 2.2f, 2.2f, 70f); b.Prop(MD("Vehicle_Debris_07"), -2.4f, 1.6f, 25f); });
      Add("X_02", 8, 8, b => { b.Bld(MB("Ruins_04"), 0.4f, 1.4f); b.Prop(MD("Rubble_Pile_05"), -1.4f, -2.6f, -15f); b.Prop(MM("Barrier_Base_Single_02"), 2.8f, -2.4f, 30f); });
      Add("X_03", 8, 8, b => { b.Prop(MV("SUV_Destroyed_01"), 2.3f, 0.5f, 100f); b.Prop(MD("DebrisPile_04"), -1.0f, -2.8f, 0f); b.Prop(MP("Barrier_06"), -2.8f, 2.2f, 0f); b.Prop(MD("Rubble_Pile_03"), -2.4f, 0.6f, 60f); });

      // ---- Y 컨테이너(8×8): 뒤쪽(북) 가장자리에 쌓고 앞은 통로.
      var container = KP("Shipping_Container_01");
      var small = KP("Shipping_Container_Small_01");
      Add("Y_00", 8, 8, b => { b.Prop(container, 0f, 2.5f, 90f); b.Prop(container, 0.2f, 2.5f, 90f, 2.62f); b.Prop(MP("Pallet_02"), -2.6f, -2.4f, 15f); });
      Add("Y_01", 8, 8, b => { b.Prop(container, -0.4f, 2.5f, 90f); b.Prop(small, 1.4f, 2.45f, 90f, 2.62f); b.Prop(small, -2.4f, -1.6f, 0f); });
      Add("Y_02", 8, 8, b => { b.Prop(container, 0f, 2.5f, 90f); b.Prop(container, -0.1f, 2.5f, 90f, 2.62f); b.Prop(container, 0.15f, 2.5f, 90f, 5.24f); b.Prop(MM("Crate_Stack_02"), 2.6f, -2.4f); });
      Add("Y_03", 8, 8, b => { b.Prop(small, -1.9f, 2.45f, 90f); b.Prop(small, 1.6f, 2.45f, 90f); b.Prop(small, -1.7f, 2.45f, 90f, 2.62f); b.Prop(MM("Barrel_Stack_01"), 2.6f, -2.0f); });

      // ---- Q 부두(8×8): 앞(-Z) = 물 쪽 난간.
      void Rail(B b, float z, float len) { for (var x = -len / 2f + 0.95f; x < len / 2f; x += 1.9f) b.Prop(MB("Railing_01"), x, z); }
      Add("Q_00", 8, 8, b => { Rail(b, -3.85f, 8f); b.Prop(P("ParkBench_01"), -1.6f, -2.6f); b.Prop(P("LightPole_Base_02"), 2.6f, -3.1f); });
      Add("Q_01", 8, 8, b => { Rail(b, -3.85f, 8f); b.Prop(ME("Tree_Round_01"), 1.2f, 1.4f); b.Prop(P("TrashCan_01"), -2.8f, -3.0f); });
      Add("Q_02", 8, 8, b => { Rail(b, -3.85f, 8f); b.Prop(P("SidewalkPoles_01"), 0.4f, -2.8f); b.Prop(MP("Crate_Wood_02"), -2.6f, 1.8f, 20f); b.Prop(MP("Pallet_01"), 2.4f, 2.0f, -10f); });

      // ---- M 자원지 설비(맵 가장자리 칸만, 8×8): 뒤(북 = 바깥)에 설비.
      Add("M_00", 8, 8, b => { b.Bld(PK + "Buildings/SM_Bld_WaterTank_03.prefab", -1.0f, 1.0f); b.Prop(KP("Generator_Large_01"), 2.6f, -1.6f, 90f); });
      Add("M_01", 8, 8, b => { b.Prop(MP("Gastank_01"), 0f, 2.2f); b.Prop(MM("Barrel_Stack_01"), -2.4f, -1.6f); b.Prop(KP("Floodlights_01"), 2.4f, -1.4f, 180f); });
      Add("M_02", 8, 8, b => { b.Bld(MB("WaterTank_01"), 1.6f, 1.6f); b.Prop(MP("BarrelPile_01"), -1.0f, -1.6f); b.Prop(MM("Crate_Stack_03"), -2.8f, 2.4f); });

      // ---- E 경계(8×8, 통행 불가): 고층 배경 건물(겹쳐도 됨, 안쪽 칸으로는 넘지 않게 뒤로 0.8m).
      var tall = new[] { "01", "03", "06", "07", "08", "10", "11" };
      for (var i = 0; i < tall.Length; i++) { var n = tall[i]; Add($"E_{i:00}", 8, 8, b => { b.Margin = -1.5f; b.Bld(PG + $"Building/SM_Gen_Bld_Background_{n}.prefab", 0f, 0.8f); }); }
      var low = new[] { MB("Plywood_Shed_01"), MB("Shop_07"), MB("Village_House_02"), PB + "Buildings/SM_Bld_House_01.prefab" };
      for (var i = 0; i < low.Length; i++) { var p = low[i]; Add($"EL_{i:00}", 8, 8, b => { b.Margin = -1.5f; b.Bld(p, 0f, 0.6f); }); }
      // EI 경계 안쪽 줄: 방벽 + 잔해 + 낮은 폐허(여러 겹).
      Add("EI_00", 8, 8, b => { b.Prop(MP("Barrier_Tall_Group_03"), 0f, -2.6f, 90f); b.Prop(MD("DebrisPile_01"), 0f, 1.6f); });
      Add("EI_01", 8, 8, b => { b.Prop(MM("Barrier_Base_Row_02"), -0.6f, -3.1f); b.Prop(MP("Fence_04"), 0f, 0.4f); b.Prop(MD("Rubble_Pile_06"), 2.4f, 2.4f, 30f); });
      Add("EI_02", 8, 8, b => { b.Prop(MP("Barrier_Tall_Group_01"), -1.4f, -2.8f, 90f); b.Prop(MP("Barrier_Tall_Group_02"), 2.3f, -2.6f, 92f); b.Prop(MB("Ruins_03"), 0f, 1.8f); });
      Add("EI_03", 8, 8, b => { b.Prop(MM("Barrier_Base_Group_03"), 0f, -2.2f); b.Prop(KP("Shipping_Container_01"), 0f, 2.4f, 90f); });

      // ---- SK 맵 밖 스카이라인(통행 무관, 크기 자유).
      var sky = new[] { "02", "04", "05", "06", "08", "09", "10", "11" };
      for (var i = 0; i < sky.Length; i++) { var n = sky[i]; Add($"SK_{i:00}", 10, 10, b => { b.Margin = -5f; b.Bld(PG + $"Building/SM_Gen_Bld_Background_{n}.prefab", 0f, 0f); }); }

      // ---- 요새: 담장 8m(앞 = 바깥), 모서리 기둥, 정문 감시탑, 건물 무리 2×2.
      Add("CW_00", 8, 2, b => { b.Margin = -1f; b.Prop(MP("Barrier_Tall_Group_01"), -1.45f, 0f, 90f); b.Prop(MP("Barrier_Tall_Group_02"), 1.45f, 0.04f, 90f); b.Prop(MM("RazorWire_01"), 0.3f, -0.9f); });
      Add("CW_01", 8, 2, b => { b.Margin = -1f; b.Prop(MP("Barrier_Tall_Group_02"), -1.45f, 0.03f, 90f); b.Prop(MP("Barrier_Tall_Group_01"), 1.45f, 0f, 90f); });
      Add("CW_02", 8, 2, b => { b.Margin = -1f; b.Prop(MP("Barrier_Tall_Group_01"), -1.45f, 0.02f, 90f); b.Prop(MP("Barrier_Tall_Group_01"), 1.45f, 0f, 90f); b.Prop(MM("Barrier_Base_Single_01"), -2.4f, -1.2f, 20f); });
      Add("CP_00", 2, 2, b => { b.Margin = -1f; b.Prop(MP("Barrier_Tall_05"), 0f, 0f, 45f); });
      Add("CT_00", 8, 8, b => { b.Margin = -1f; b.Bld(MB("GuardTower_01"), 0f, 0.6f); b.Prop(MM("Barrier_Base_Row_01"), 0f, -2.6f); });
      Add("CB_00", 16, 16, b => {
        b.Bld(MB("Barracks_01"), 0f, 3.6f);
        b.Prop(MM("Radio_Control_Box_01"), 2.5f, -5.0f, 90f);
        b.Prop(MP("Satellite_Dish_02"), -5.5f, -5.0f); b.Prop(MM("Crate_Stack_Cover_02"), -3.0f, -2.5f);
      });
      Add("CB_01", 16, 16, b => {
        b.Bld(MB("Plywood_Shed_01"), -3.0f, 3.2f);
        b.Bld(PK + "Buildings/SM_Bld_Portable_Office_01.prefab", 4.6f, 3.4f);
        b.Prop(MM("Mobile_Radar_01"), -2.4f, -4.0f); b.Prop(MB("WaterTank_01"), 4.6f, -3.6f);
      });
      Add("CB_02", 16, 16, b => {
        b.Bld(MB("Tent_01"), -1.0f, 3.5f, 90f);
        b.Prop(MP("Gastank_01"), 2.5f, -5.5f); b.Prop(MP("Generator_Large_01"), 6.2f, -1.5f); b.Prop(MP("TV_Antenna_03"), 6.4f, 2.5f);
        b.Prop(MM("Crate_Stack_02"), -5.5f, -5.0f);
      });

      // ---- CC 요새 엄폐물 군집(1×1, 낮은 방벽·상자·드럼).
      Add("CC_00", 8, 8, b => { b.Prop(MM("Barrier_Base_Row_02"), -0.6f, -1.2f, 15f); b.Prop(MM("Crate_Stack_Cover_01"), 2.2f, 1.4f, 30f); b.Prop(MM("Barrel_01"), -2.6f, 1.8f); });
      Add("CC_01", 8, 8, b => { b.Prop(MM("Barrier_Base_Group_01"), 0.4f, 0.6f, -20f); b.Prop(MP("Pallet_02"), -2.4f, -2.2f, 10f); b.Prop(MM("Crate_Ammo_01"), 2.6f, -2.0f, 70f); });
      Add("CC_02", 8, 8, b => { b.Prop(MM("Barrier_Base_Row_01"), -1.6f, 1.2f, -35f); b.Prop(MM("Barrier_Base_Row_01"), 1.8f, -1.0f, 55f); b.Prop(MM("Barrel_Stack_01"), 2.4f, 2.4f); });
      Add("CC_03", 8, 8, b => { b.Prop(MM("Barrier_Base_Group_02"), 0f, 0.4f, 40f); b.Prop(MM("Crate_Plastic_01"), -2.6f, -2.4f, 5f); });

      // ---- GR 다리 난간 8m(앞 = 물 쪽).
      Add("GR_00", 8, 1, b => { b.Margin = -1f; Rail(b, 0f, 8f); });
      return list;
    }

    /// <summary>도시 건물 뒷벽(로컬 +Z): 얇은 배관·환기구·실외기로 민벽을 깬다(칸 밖으로 나가지 않게 0.5m 이내).</summary>
    static void BackDress(B b, Spec spec) {
      // 건물 조각(SM_Bld_*, 높이 2.5m 이상)만으로 뒷벽을 정한다. 칸 밖으로 나가면 생략(떠 있는 소품 금지).
      var parts = b.Root.Cast<Transform>().Where(t => t.name.StartsWith("SM_Bld") || t.GetComponentsInChildren<Transform>().Any(c => c.name.StartsWith("SM_Bld"))).ToList();
      if (parts.Count == 0) return;
      var all = BlockKit.WorldBounds(parts[0].gameObject);
      foreach (var t in parts) all.Encapsulate(BlockKit.WorldBounds(t.gameObject));
      if (all.size.y < 6f) return;
      var o = b.Root.position;
      var back = all.max.z - o.z;
      if (back + 0.5f > spec.SizeZ / 2f - 0.1f) return;
      var h = spec.Id.GetHashCode() & 0xff;
      var span = all.size.x * 0.35f;
      var x0 = all.center.x - o.x;
      b.Prop(P("Pipe_Small_01"), x0 - span + (h % 3) * 0.4f, back + 0.2f, 180f);
      b.Prop(P("Pipe_Small_02"), x0 + span * 0.6f, back + 0.2f, 180f);
      b.Prop(P("Aircon_01"), x0 + (h % 5 - 2) * 0.5f, back + 0.25f, Front(P("Aircon_01")) + 180f, 3.4f);
      if (all.size.y > 9f) b.Prop(P("Vents_Straight_01"), x0 - span * 0.3f, back + 0.2f, 180f, 6.5f);
    }

    // ------------------------------------------------------------ bake

    /// <summary>블록 안 모든 메시(LOD0)를 재질별 서브메시 하나로 합쳐 프리팹으로 저장한다. 큰 그림자는 건물만(높이 2.5m 이상).</summary>
    static GameObject Bake(GameObject root, Spec spec) {
      var byMaterial = new Dictionary<Material, List<CombineInstance>>();
      long triangles = 0;
      var maxHeight = 0f;
      var world = root.transform.worldToLocalMatrix;
      foreach (var f in Lod0Filters(root)) {
        var r = f.GetComponent<MeshRenderer>();
        var mats = r.sharedMaterials;
        maxHeight = Mathf.Max(maxHeight, r.bounds.max.y);
        for (var s = 0; s < f.sharedMesh.subMeshCount; s++) {
          var mat = s < mats.Length ? mats[s] : mats.LastOrDefault();
          if (mat == null) continue;
          if (!byMaterial.TryGetValue(mat, out var list)) byMaterial[mat] = list = new List<CombineInstance>();
          list.Add(new CombineInstance { mesh = f.sharedMesh, subMeshIndex = s, transform = world * f.transform.localToWorldMatrix });
          triangles += f.sharedMesh.GetIndexCount(s) / 3;
        }
      }
      var parts = new List<CombineInstance>();
      var materials = new List<Material>();
      foreach (var pair in byMaterial.OrderByDescending(p => p.Value.Count)) {
        var part = new Mesh { indexFormat = IndexFormat.UInt32 };
        part.CombineMeshes(pair.Value.ToArray(), true, true);
        parts.Add(new CombineInstance { mesh = part, transform = Matrix4x4.identity });
        materials.Add(pair.Key);
      }
      var mesh = new Mesh { name = spec.Id, indexFormat = triangles * 3 > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
      mesh.CombineMeshes(parts.ToArray(), false, false);
      foreach (var p in parts) Object.DestroyImmediate(p.mesh);
      mesh.RecalculateBounds();
      mesh.Optimize();
      var meshPath = $"{Out}/{spec.Id}_mesh.asset";
      var existing = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
      if (existing != null) {
        EditorUtility.CopySerialized(mesh, existing);
        mesh = existing;
      } else {
        AssetDatabase.CreateAsset(mesh, meshPath);
      }
      var baked = new GameObject(spec.Id, typeof(MeshFilter), typeof(MeshRenderer));
      baked.GetComponent<MeshFilter>().sharedMesh = mesh;
      var renderer = baked.GetComponent<MeshRenderer>();
      renderer.sharedMaterials = materials.ToArray();
      renderer.shadowCastingMode = maxHeight - root.transform.position.y > 2.5f ? ShadowCastingMode.On : ShadowCastingMode.Off;
      renderer.lightProbeUsage = LightProbeUsage.Off;
      renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
      var lod = baked.AddComponent<LODGroup>();
      lod.SetLODs(new[] { new LOD(maxHeight > 8f ? 0.005f : 0.012f, new Renderer[] { renderer }) });
      lod.RecalculateBounds();
      var prefab = PrefabUtility.SaveAsPrefabAsset(baked, $"{Out}/{spec.Id}.prefab");
      Object.DestroyImmediate(baked);
      Log.Add($"block {spec.Id}: tris {triangles}, materials {materials.Count} ({string.Join("|", materials.Select(m => m.name))}), height {maxHeight:0.0}");
      return prefab;
    }

    /// <summary>시범 블록·본진(장식 표 → 건설 금지 구역) 재생성 + 맵·NavMesh 굽기 + 꾸미기 블록.</summary>
    [MenuItem("Tools/Factory Wars/Map4P v4/Build Pilot + Bake + Dress")]
    public static void BuildBakeDress() {
      BlockKit.BuildAndBakePilot();
      BuildAll();
    }

    [MenuItem("Tools/Factory Wars/Map4P v4/Build Dress Blocks")]
    public static void BuildAll() {
      Log.Clear();
      FrontCache.Clear();
      if (!AssetDatabase.IsValidFolder("Assets/FactoryWarsArt")) AssetDatabase.CreateFolder("Assets", "FactoryWarsArt");
      if (!AssetDatabase.IsValidFolder(BlockKit.OutFolder)) AssetDatabase.CreateFolder("Assets/FactoryWarsArt", "Blocks");
      if (!AssetDatabase.IsValidFolder(Out)) AssetDatabase.CreateFolder(BlockKit.OutFolder, "Dress");
      var catalog = Resources.Load<VisualCatalog>(VisualCatalog.ResourcePath);
      catalog.Slots.RemoveAll(s => s != null && s.Id != null && s.Id.StartsWith(GridDress.Prefix));
      var origin = new Vector3(5000f, 0f, 5000f);
      foreach (var spec in Specs()) {
        var root = new GameObject(spec.Id);
        root.transform.position = origin;
        var b = new B { Root = root.transform, Id = spec.Id, HalfX = spec.SizeX / 2f, HalfZ = spec.SizeZ / 2f };
        try {
          spec.Build(b);
          if (spec.Id.StartsWith("H")) BackDress(b, spec);
          var prefab = Bake(root, spec);
          BlockKit.Register(catalog, spec.Id, prefab, new Vector3(spec.SizeX, 0f, spec.SizeZ));
          var slot = catalog.Slots.First(s => s != null && s.Id == "block/" + spec.Id);
          slot.Id = GridDress.Prefix + spec.Id;
          slot.Notes = "Map4P v4 dress block (DressKit.cs), baked Synty pieces";
        } catch (Exception e) {
          Log.Add($"ERROR {spec.Id}: {e}");
        } finally {
          Object.DestroyImmediate(root);
        }
      }
      catalog.Invalidate();
      EditorUtility.SetDirty(catalog);
      AssetDatabase.SaveAssets();
      var path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Game-logs", "dresskit.log"));
      File.WriteAllLines(path, Log);
      Debug.Log($"[FW-Dress] built {Specs().Count} blocks, log {path}\n" + string.Join("\n", Log.Where(l => l.StartsWith("WARN") || l.StartsWith("ERROR"))));
    }
  }
}
