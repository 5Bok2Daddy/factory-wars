namespace FactoryWars.View {
  using System.Collections.Generic;
  using Quantum;
  using UnityEngine;

  /// <summary>
  /// Map4P v4 꾸미기 배치(PROMPT-MAP4P-V4-DRESS 2-B): 칸 지도 글자 → DressKit 블록("dress4p/종류_번호")을 칸 단위로 놓는다.
  /// 개별 소품 좌표는 없다. 블록 종류·변형·방향은 칸 글자, 이웃 칸, 칸 해시로만 정한다(결정적).
  /// - 정면(블록 -Z)은 남쪽(화면 아래, 카메라)이 기본. 남쪽에 길이 없고 동·서쪽에 길이 있으면 그쪽(간선 R 우선). 북쪽만 길이면 남쪽 유지.
  /// - 같은 종류의 이웃(왼쪽·위쪽)과 같은 변형은 쓰지 않는다.
  /// 화면 전용이며 시뮬레이션 상태를 읽기만 한다.
  /// </summary>
  public static class GridDress {
    public const string Prefix = "dress4p/";
    static readonly Dictionary<string, List<VisualSlot>> Kinds = new Dictionary<string, List<VisualSlot>>();

    /// <summary>배치 기록(검사·캡처용): 블록 이름 = 종류_변형@행,열.</summary>
    public static readonly List<GameObject> Placed = new List<GameObject>();

    static void LoadKinds() {
      Kinds.Clear();
      var catalog = VisualCatalog.Current;
      if (catalog == null) return;
      foreach (var slot in catalog.Slots) {
        if (slot == null || slot.Id == null || !slot.Id.StartsWith(Prefix) || slot.Prefab == null) continue;
        var name = slot.Id.Substring(Prefix.Length);
        var kind = name.Substring(0, name.LastIndexOf('_'));
        if (!Kinds.TryGetValue(kind, out var list)) Kinds[kind] = list = new List<VisualSlot>();
        list.Add(slot);
      }
      foreach (var list in Kinds.Values) list.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
    }

    /// <summary>낮음 품질 LOD 문턱 배율: 작은 블록 4배, 큰 도시 건물 2.5배, 나머지 1배.</summary>
    public static float LowCullFactor(string kind) =>
      kind == "K" || kind == "L" || kind == "X" || kind == "Y" || kind == "Q" || kind == "M" || kind == "CC" || kind == "EI" || kind == "GR" || kind.StartsWith("H1") ? 4f
      : kind.StartsWith("HW") || kind.StartsWith("HD") || kind.StartsWith("H4") ? 2.5f : 1f;

    public static bool Ready() {
      if (Kinds.Count == 0) LoadKinds();
      return Kinds.ContainsKey("H1");
    }

    static int Hash(int r, int c, int salt = 0) {
      unchecked {
        var h = (uint)(r * 73856093) ^ (uint)(c * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
        return (int)(h & 0x7fffffff);
      }
    }

    static bool Road(char ch) => ch == 'R' || ch == 's' || ch == 'G' || ch == 'P';

    /// <summary>방향: 0 남, 1 동, 2 북, 3 서. 블록 -Z가 그 방향을 보게 하는 Y 각도.</summary>
    static float Yaw(int dir) => dir switch { 1 => -90f, 2 => 180f, 3 => 90f, _ => 0f };

    /// <summary>칸 묶음(r0,c0,h,w)의 정면: 남쪽 길 → 남, 아니면 동·서 길(R 우선), 아니면 남.</summary>
    static int FrontDir(MapSpec map, int r0, int c0, int h, int w) {
      int Score(int dr, int dc, int count, bool vertical) {
        var best = 0;
        for (var i = 0; i < count; i++) {
          var ch = vertical ? GridMapView.Cell(map, r0 + i, dc) : GridMapView.Cell(map, dr, c0 + i);
          if (ch == 'R' || ch == 'G' || ch == 'P') best = Mathf.Max(best, 2);
          else if (ch == 's') best = Mathf.Max(best, 1);
        }
        return best;
      }
      if (Score(r0 + h, 0, w, false) > 0) return 0;
      var east = Score(0, c0 + w, h, true);
      var west = Score(0, c0 - 1, h, true);
      if (east > 0 || west > 0) return east > west || (east == west && Hash(r0, c0, 5) % 2 == 0) ? 1 : 3;
      return 0;
    }

    static GameObject Put(Transform parent, string kind, int variant, Vector3 position, float yaw, string tag) {
      if (!Kinds.TryGetValue(kind, out var list) || list.Count == 0) return null;
      var slot = list[variant % list.Count];
      var item = Object.Instantiate(slot.Prefab, parent);
      item.name = $"{slot.Id.Substring(Prefix.Length)}@{tag}";
      item.transform.localPosition = position;
      item.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
      item.isStatic = true;
      // Task 39 C1: parks and parking lots (many, small) do not cast shadows.
      if (kind == "K" || kind == "L") item.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
      // Harness fix (2026-11-24): keep parking lots (L), container yards (Y), parks (K) and ruins (X/XC) visible at gameplay & sector zoom
      // (1.5x instead of 5x) so plots never pop out into bare grey/green slabs.
      if (GraphicsQuality.Current != QualityTier.Low && (kind == "L" || kind == "Y" || kind == "K" || kind == "X" || kind == "XC") && item.TryGetComponent<LODGroup>(out var farPlot)) {
        var lods = farPlot.GetLODs();
        lods[0].screenRelativeTransitionHeight *= 1.5f;
        farPlot.SetLODs(lods);
      }
      if (GraphicsQuality.Current == QualityTier.Low) {
        item.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        // 낮음: 작은 블록(공원·주차·잔해·컨테이너·부두·설비·엄폐·경계 안쪽)은 멀면 일찍 감춘다(삼각형 예산).
        if (LowCullFactor(kind) > 1f && item.TryGetComponent<LODGroup>(out var lod)) {
          var lods = lod.GetLODs();
          lods[0].screenRelativeTransitionHeight *= LowCullFactor(kind);
          lod.SetLODs(lods);
        }
      }
      Placed.Add(item);
      return item;
    }

    public static void Build(Transform root, MapSpec map) {
      Placed.Clear();
      LoadKinds();
      if (!Kinds.ContainsKey("H1")) return;
      var n = map.CellGridSize;
      var size = map.CellGridCell.AsFloat;
      var parent = new GameObject("GridDress").transform;
      parent.SetParent(root, false);
      var covered = new bool[n, n];
      var variantAt = new string[n, n];
      var low = GraphicsQuality.Current == QualityTier.Low;
      var camp = map.MercenaryCamp && Kinds.ContainsKey("MK");

      int Pick(string kind, int r0, int c0, int h, int w, int salt) {
        if (!Kinds.TryGetValue(kind, out var list) || list.Count == 0) return 0;
        var count = list.Count;
        var v = Hash(r0, c0, salt) % count;
        for (var tries = 0; tries < count; tries++) {
          var id = kind + "_" + v;
          var clash = false;
          for (var i = -1; i <= h && !clash; i++) {
            for (var j = -1; j <= w && !clash; j++) {
              if (i >= 0 && i < h && j >= 0 && j < w) continue;
              var rr = r0 + i;
              var cc = c0 + j;
              if (rr >= 0 && cc >= 0 && rr < n && cc < n && variantAt[rr, cc] == id) clash = true;
            }
          }
          for (var k = 2; k <= 3 && !clash; k++) {
            if (c0 - k >= 0 && variantAt[r0, c0 - k] == id) clash = true;
            if (r0 - k >= 0 && variantAt[r0 - k, c0] == id) clash = true;
          }
          if (!clash) break;
          v = (v + 1) % count;
        }
        for (var i = 0; i < h; i++) for (var j = 0; j < w; j++) variantAt[r0 + i, c0 + j] = kind + "_" + v;
        return v;
      }
      Vector3 Center(int r0, int c0, int h, int w) => new Vector3(GridMapView.CellX(map, c0 + (w - 1) / 2f), 0.02f, GridMapView.CellZ(map, r0 + (h - 1) / 2f));
      bool Free(int r, int c, char ch) => r >= 0 && c >= 0 && r < n && c < n && !covered[r, c] && GridMapView.Cell(map, r, c) == ch;

      // ---- 도시 건물(H): 2×2 → 가로 2 → 세로 2 → 1칸. 해시로 묶음 크기를 섞는다.
      for (var r = 0; r < n; r++) {
        for (var c = 0; c < n; c++) {
          if (!Free(r, c, 'H')) continue;
          var h = Hash(r, c, 1);
          var can4 = Free(r, c + 1, 'H') && Free(r + 1, c, 'H') && Free(r + 1, c + 1, 'H');
          var canW = Free(r, c + 1, 'H');
          var canD = Free(r + 1, c, 'H');
          int rows = 1, cols = 1;
          if (can4 && h % 5 < 3) { rows = 2; cols = 2; } else if (canW && (!canD || h % 2 == 0) && h % 7 != 0) cols = 2; else if (canD && h % 7 != 1) rows = 2;
          for (var i = 0; i < rows; i++) for (var j = 0; j < cols; j++) covered[r + i, c + j] = true;
          var dir = FrontDir(map, r, c, rows, cols);
          string kind;
          var yaw = Yaw(dir);
          if (rows == 2 && cols == 2) kind = "H4";
          else if (rows == 1 && cols == 1) {
            // B2: mix damaged/destroyed buildings into the former X belt (rows 33-46, west/east wings).
            var inBelt = r >= 33 && r <= 46 && (c <= 26 || c >= 53);
            kind = inBelt && Kinds.ContainsKey("HR") && Hash(r, c, 23) % 3 == 0 ? "HR" : "H1";
          }
          else {
            var wide = cols == 2;
            var frontOnLongSide = (dir == 0 || dir == 2) == wide;
            kind = frontOnLongSide ? "HW" : "HD";
          }
          // 동·서향 건물은 카메라 쪽 옆면을 꾸민 변형으로(민벽 금지).
          if ((dir == 1 || dir == 3) && Kinds.ContainsKey(kind + (dir == 1 ? "S" : "T"))) kind += dir == 1 ? "S" : "T";
          Put(parent, kind, Pick(kind, r, c, rows, cols, 2), Center(r, c, rows, cols), yaw, $"{r},{c}");
        }
      }

      // ---- 1칸 블록: 공원·주차·잔해·컨테이너·부두·자원지·경계.
      for (var r = 0; r < n; r++) {
        for (var c = 0; c < n; c++) {
          var ch = GridMapView.Cell(map, r, c);
          var at = Center(r, c, 1, 1);
          switch (ch) {
            case 'K': Put(parent, "K", Pick("K", r, c, 1, 1, 3), at, Yaw(FrontDir(map, r, c, 1, 1)), $"{r},{c}"); break;
            case 'L': Put(parent, "L", Pick("L", r, c, 1, 1, 4), at, Hash(r, c, 9) % 2 == 0 ? 0f : 180f, $"{r},{c}"); break;
            case 'X': {
              var isConstruction = Kinds.ContainsKey("XC") && ((r == 39 && c == 17) || (r == 40 && c == 62));
              if (isConstruction) Put(parent, "XC", 0, at, Yaw(FrontDir(map, r, c, 1, 1)), $"{r},{c}");
              else Put(parent, "X", Pick("X", r, c, 1, 1, 6), at, 90f * (Hash(r, c, 7) % 4), $"{r},{c}");
              break;
            }
            case 'Y': {
              // Task 39: on the PMC camp map the compound's Y cells belong to the camp blocks (no container piles).
              if (camp && Mathf.Abs(at.x) < 56f && Mathf.Abs(at.z) < 56f) break;
              // 요새 안 컨테이너는 등(쌓은 쪽)을 담장 쪽으로, 밖은 길 규칙.
              var dir = Mathf.Abs(at.x) < 80f && Mathf.Abs(at.z) < 80f
                ? (Mathf.Abs(at.x) > Mathf.Abs(at.z) ? (at.x > 0 ? 3 : 1) : (at.z > 0 ? 0 : 2))
                : FrontDir(map, r, c, 1, 1);
              Put(parent, "Y", Pick("Y", r, c, 1, 1, 8), at, Yaw(dir), $"{r},{c}");
              break;
            }
            case 'w': {
              var dir = GridMapView.Cell(map, r + 1, c) == 'W' ? 0 : GridMapView.Cell(map, r, c + 1) == 'W' ? 1 : GridMapView.Cell(map, r - 1, c) == 'W' ? 2 : GridMapView.Cell(map, r, c - 1) == 'W' ? 3 : -1;
              if (dir >= 0) Put(parent, "Q", Pick("Q", r, c, 1, 1, 10), at, Yaw(dir), $"{r},{c}");
              break;
            }
            case 'M': {
              // 맵 가장자리 두 줄의 자원지 칸만 설비(자원 노드·채집 길·확장 부지는 안쪽).
              // 바깥 쪽(맵 가장자리 또는 M·길이 아닌 이웃) 칸, 자원 노드 10m·확장 부지 16m 밖만 설비.
              var edge = r <= 1 ? 2 : r >= n - 2 ? 0 : c <= 1 ? 3 : c >= n - 2 ? 1 : -1;
              if (edge < 0) {
                for (var d = 0; d < 4 && edge < 0; d++) {
                  var (dr, dc) = d switch { 0 => (1, 0), 1 => (0, 1), 2 => (-1, 0), _ => (0, -1) };
                  var nb = GridMapView.Cell(map, r + dr, c + dc);
                  if (nb != 'M' && nb != 'R' && nb != 's' && nb != 'G') edge = d;
                }
              }
              if (edge < 0) break;
              var clear = true;
              foreach (var node in map.ResourceNodes) if ((node.Shape.Center.ToUnityVector3() - at).sqrMagnitude < 10f * 10f) clear = false;
              foreach (var site in map.Expansions) if ((site.Position.ToUnityVector3() - at).sqrMagnitude < 16f * 16f) clear = false;
              if (clear && Hash(r, c, 11) % 4 != 0) Put(parent, "M", Pick("M", r, c, 1, 1, 12), at, Yaw((edge + 2) % 4), $"{r},{c}");
              break;
            }
            case 'E': {
              var inward = r <= 1 ? 0 : r >= n - 2 ? 2 : c <= 1 ? 1 : 3;
              var (dr, dc) = inward switch { 0 => (1, 0), 1 => (0, 1), 2 => (-1, 0), _ => (0, -1) };
              var inner = GridMapView.Cell(map, r + dr, c + dc) != 'E' && r + dr >= 0 && r + dr < n && c + dc >= 0 && c + dc < n;
              if (inner) Put(parent, "EI", Pick("EI", r, c, 1, 1, 13), at, Yaw(inward), $"{r},{c}");
              else {
                // 남쪽 띠는 카메라 앞이라 낮은 건물만.
                var kind = r >= n - 2 ? "EL" : Hash(r, c, 14) % 4 == 0 ? "EL" : "E";
                var item = Put(parent, kind, Pick(kind, r, c, 1, 1, 15), at, Yaw(inward), $"{r},{c}");
              }
              break;
            }
          }
        }
      }

      var dirs = new[] { (1, 0, 0), (0, 1, 1), (-1, 0, 2), (0, -1, 3) };
      // ---- Task 39 card 4: PMC camp = two baked blocks (perimeter + zones); the old wall/cover/container dressing is skipped.
      if (camp) {
        Put(parent, "MK", low ? 2 : 0, new Vector3(0f, 0.02f, 0f), 0f, "camp-perimeter");
        Put(parent, "MK", low ? 3 : 1, new Vector3(0f, 0.02f, 0f), 0f, "camp-zones");
        goto bridges;
      }
      // ---- 중앙 요새: 칸 경계 담장(정문 16m 제외) + 모서리 기둥 + 정문 감시탑 + 대각 4곳 건물 무리.
      bool InCompound(int r, int c) { var ch = GridMapView.Cell(map, r, c); return (ch == 'C' || ch == 'Y' || ch == 'p') && Mathf.Abs(r - n / 2 + 0.5f) < 8 && Mathf.Abs(c - n / 2 + 0.5f) < 8; }
      bool Gate(float x, float z) => (Mathf.Abs(x) < 8.5f && Mathf.Abs(z) > 28f) || (Mathf.Abs(z) < 8.5f && Mathf.Abs(x) > 28f);
      for (var r = 0; r < n; r++) {
        for (var c = 0; c < n; c++) {
          if (!InCompound(r, c)) continue;
          var center = Center(r, c, 1, 1);
          foreach (var (dr, dc, dir) in dirs) {
            if (InCompound(r + dr, c + dc)) continue;
            var mid = center + new Vector3(dc * size / 2f, 0f, -dr * size / 2f);
            if (Gate(mid.x, mid.z)) {
              // 정문 양옆 칸이면 감시탑(바깥 칸 쪽 담장 대신).
              continue;
            }
            Put(parent, "CW", Hash(r, c, 16 + dir) % 3, mid, Yaw(dir), $"{r},{c},{dir}");
          }
          // 볼록 모서리: 두 바깥 변이 만나는 꼭짓점.
          for (var k = 0; k < 4; k++) {
            var (ar, ac, _) = dirs[k];
            var (br, bc, _) = dirs[(k + 1) % 4];
            if (InCompound(r + ar, c + ac) || InCompound(r + br, c + bc)) continue;
            var corner = center + new Vector3((ac + bc) * size / 2f, 0f, -(ar + br) * size / 2f);
            if (!Gate(corner.x, corner.z)) Put(parent, "CP", 0, corner, 0f, $"{r},{c},p{k}");
          }
        }
      }
      for (var side = 0; side < 4; side++) {
        // 정문(변 가운데 16m) 양옆 바깥 칸에 감시탑.
        foreach (var s in new[] { -1, 1 }) {
          var along = s * 12f;
          var p = side switch { 0 => new Vector3(along, 0f, -76f), 1 => new Vector3(76f, 0f, along), 2 => new Vector3(along, 0f, 76f), _ => new Vector3(-76f, 0f, along) };
          var col = Mathf.FloorToInt((p.x + n * size / 2f) / size);
          var row = Mathf.FloorToInt((n * size / 2f - p.z) / size);
          // 바깥으로 첫 비요새 칸.
          var (dr, dc, _) = dirs[side];
          for (var step = 0; step < 6 && InCompound(row, col); step++) { row += dr; col += dc; }
          var ch = GridMapView.Cell(map, row, col);
          if (ch == 'R' || ch == 's') continue; // 길 위에는 두지 않는다.
          Put(parent, "CT", side, Center(row, col, 1, 1), Yaw(side), $"gate{side}{s}");
        }
      }
      for (var q = 0; q < 4; q++) {
        var sx = q % 2 == 0 ? -1 : 1;
        var sz = q < 2 ? -1 : 1;
        var target = new Vector2(sx * 30f, sz * 30f);
        var best = (r: -1, c: -1, d: float.MaxValue);
        for (var r = 0; r + 1 < n; r++) {
          for (var c = 0; c + 1 < n; c++) {
            if (GridMapView.Cell(map, r, c) != 'C' || GridMapView.Cell(map, r + 1, c) != 'C' || GridMapView.Cell(map, r, c + 1) != 'C' || GridMapView.Cell(map, r + 1, c + 1) != 'C') continue;
            var p = Center(r, c, 2, 2);
            var d = (new Vector2(p.x, p.z) - target).sqrMagnitude;
            if (d < best.d) best = (r, c, d);
          }
        }
        if (best.r < 0) continue;
        var at = Center(best.r, best.c, 2, 2);
        var face = Mathf.Abs(at.x) > Mathf.Abs(at.z) ? (at.x > 0 ? 3 : 1) : (at.z > 0 ? 0 : 2);
        Put(parent, "CB", q, at, Yaw(face), $"cb{q}");
        for (var i = 0; i < 2; i++) for (var j = 0; j < 2; j++) covered[best.r + i, best.c + j] = true;
      }

      // ---- 요새 엄폐물 군집: 십자 길·정문 길(|x|,|z| < 11)·상인 기지(22m)·시뮬레이션 엄폐물(±32,±32)·건물 무리 밖 C 칸, 해시로 4칸에 1개꼴.
      // 정문 4곳 안쪽 양옆 엄폐 군집(진입로는 비운다).
      for (var side = 0; side < 4; side++) {
        foreach (var s in new[] { -1, 1 }) {
          var p = side switch { 0 => new Vector3(s * 11f, 0f, -43f), 1 => new Vector3(43f, 0f, s * 11f), 2 => new Vector3(s * 11f, 0f, 43f), _ => new Vector3(-43f, 0f, s * 11f) };
          var col = Mathf.FloorToInt((p.x + n * size / 2f) / size);
          var row = Mathf.FloorToInt((n * size / 2f - p.z) / size);
          if (GridMapView.Cell(map, row, col) != 'C' || covered[row, col]) continue;
          covered[row, col] = true;
          Put(parent, "CC", Hash(row, col, 20), Center(row, col, 1, 1), Yaw(side), $"gatein{side}{s}");
        }
      }
      for (var r = 0; r < n; r++) {
        for (var c = 0; c < n; c++) {
          if (GridMapView.Cell(map, r, c) != 'C' || covered[r, c] || !InCompound(r, c)) continue;
          var at = Center(r, c, 1, 1);
          if (Mathf.Abs(at.x) < 11f || Mathf.Abs(at.z) < 11f || at.magnitude < 22f) continue;
          if (Mathf.Abs(Mathf.Abs(at.x) - 32f) < 8f && Mathf.Abs(Mathf.Abs(at.z) - 32f) < 8f) continue;
          var nearWall = false;
          foreach (var (dr, dc, _) in dirs) if (!InCompound(r + dr, c + dc)) nearWall = true;
          if (Hash(r, c, 17) % (nearWall ? 5 : at.magnitude < 50f ? 2 : 3) != 0) continue;
          covered[r, c] = true;
          Put(parent, "CC", Hash(r, c, 18), at, 90f * (Hash(r, c, 19) % 4), $"{r},{c}");
        }
      }

      bridges:
      // ---- 다리 난간: 다리(G) 칸의 물(W) 쪽 변.
      for (var r = 0; r < n; r++) {
        for (var c = 0; c < n; c++) {
          if (GridMapView.Cell(map, r, c) != 'G') continue;
          var center = Center(r, c, 1, 1);
          // Task 39 fix 2: bridges carry concrete parapets and steel arches (ArchBridgeView), no wooden rails.
          if (map.MercenaryCamp) continue;
          foreach (var (dr, dc, dir) in dirs) {
            if (GridMapView.Cell(map, r + dr, c + dc) != 'W') continue;
            Put(parent, "GR", 0, center + new Vector3(dc * (size / 2f - 0.3f), 0f, -dr * (size / 2f - 0.3f)), Yaw(dir), $"{r},{c},{dir}");
          }
        }
      }

      // ---- 맵 밖 스카이라인: 변마다 12~22m 간격(해시), 바깥 18~46m. 남쪽(카메라 앞)은 비운다.
      if (!low) {
        var half = n * size / 2f;
        for (var side = 1; side < 4; side++) {
          var t = -half - 30f;
          var i = 0;
          while (t < half + 30f) {
            var h = Hash(side, i, 21);
            var off = 18f + h % 28;
            var p = side switch { 1 => new Vector3(half + off, 0f, t), 2 => new Vector3(t, 0f, half + off), _ => new Vector3(-half - off, 0f, t) };
            p.y = OuterGround(off);
            Put(parent, "SK", h / 7, p, Yaw((side + 2) % 4) + (h % 3 - 1) * 4f, $"sky{side}_{i}");
            t += 12f + h % 11;
            i++;
          }
        }
      }
    }

    /// <summary>GridMapView 바깥 띠 높이(변에서 바깥으로 off m).</summary>
    static float OuterGround(float off) {
      var o = new[] { 0f, 1.4f, 5f, 13f, 26f, 44f };
      var y = new[] { .028f, .01f, -.2f, -.65f, -2f, -7f };
      for (var i = 1; i < o.Length; i++) if (off <= o[i]) return Mathf.Lerp(y[i - 1], y[i], (off - o[i - 1]) / (o[i] - o[i - 1]));
      return -7f;
    }
  }
}
