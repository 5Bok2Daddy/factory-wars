namespace FactoryWars.View {
  using System.Collections.Generic;
  using Photon.Deterministic;
  using Quantum;
  using UnityEngine;
  using UnityEngine.Rendering;

  /// <summary>
  /// 칸 지도 맵(MapSpec.CellGrid, Map4P v4) 화면. 구도는 칸 글자 그대로이고 높이는 판정 모양(고지대·비탈)에서 읽는다.
  /// 시범 단계(2026-11-22): 본진 블록(block/blk_base_pad, 자리마다 90도 회전)과 도시 2×2 블록(block/blk_city_mixed_2x2_*)만
  /// 실제 모델이고, 나머지 칸은 칸 종류 색의 임시 바닥 타일이다.
  /// 화면 전용이며 시뮬레이션 상태를 읽기만 한다.
  /// </summary>
  public static class GridMapView {
    public const string BaseSlot = "block/blk_base_pad";
    const string CompoundTrimSlot = "block/blk_compound_pilot_trim";
    const float Chamfer = 1.35f;
    static readonly (int R, int C)[] CornerNeighbours = { (1, -1), (-1, -1), (-1, 1), (1, 1) };
    public static readonly string[] CitySlots = { "block/blk_city_mixed_2x2_a", "block/blk_city_mixed_2x2_b", "block/blk_city_mixed_2x2_c" };

    public static bool Uses(MapSpec map) => map != null && map.CellGridSize > 0 && map.CellGrid != null && map.CellGrid.Length == map.CellGridSize * map.CellGridSize;

    /// <summary>Task 39: a bridge cell over the canal (G next to W). Its ground drops to the canal floor so the water runs on under the deck.</summary>
    public static bool Deck(MapSpec map, int r, int c) => Cell(map, r, c) == 'G' &&
      (Cell(map, r - 1, c) == 'W' || Cell(map, r + 1, c) == 'W' || Cell(map, r, c - 1) == 'W' || Cell(map, r, c + 1) == 'W');

    public static char Cell(MapSpec map, int r, int c) =>
      r < 0 || c < 0 || r >= map.CellGridSize || c >= map.CellGridSize ? 'E' : (char)map.CellGrid[r * map.CellGridSize + c];

    static float Size(MapSpec map) => map.CellGridCell.AsFloat;
    static float Half(MapSpec map) => map.CellGridSize * Size(map) / 2f;
    public static float CellX(MapSpec map, float c) => -Half(map) + Size(map) * (c + 0.5f);
    public static float CellZ(MapSpec map, float r) => Half(map) - Size(map) * (r + 0.5f);

    /// <summary>시범 블록 배치 기록(검사·캡처용).</summary>
    public static readonly List<GameObject> PlacedBlocks = new List<GameObject>();
    /// <summary>시범 도시 블록 칸(왼쪽 위 칸).</summary>
    public static readonly List<Vector2Int> CityBlockCells = new List<Vector2Int>();
    public static Vector3 BaseCorner { get; private set; }

    /// <summary>임시 바닥 색(칸 지도 범례 계열, 채도를 낮춘 확실한 임시 색).</summary>
    static Color Tile(char cell) {
      switch (cell) {
        case 'B': return new Color(0.66f, 0.66f, 0.63f);
        case 'p': return new Color(0.61f, 0.59f, 0.53f);
        case 'v': return new Color(0.55f, 0.55f, 0.54f);
        case 'P': return new Color(0.31f, 0.32f, 0.33f);
        case 'R': return new Color(0.25f, 0.26f, 0.28f);
        case 's': return new Color(0.285f, 0.29f, 0.305f);
        case 'G': return new Color(0.50f, 0.50f, 0.50f);
        case 'w': return new Color(0.70f, 0.64f, 0.53f);
        case 'W': return new Color(0.28f, 0.33f, 0.34f);
        case 'C': return new Color(0.53f, 0.49f, 0.43f);
        case 'M': return new Color(0.49f, 0.46f, 0.40f);
        case 'K': return new Color(0.42f, 0.55f, 0.38f);
        case 'L': return new Color(0.46f, 0.49f, 0.54f);
        case 'Y': return new Color(0.52f, 0.47f, 0.41f);
        case 'X': return new Color(0.52f, 0.45f, 0.36f);
        case 'H': return new Color(0.56f, 0.56f, 0.55f);
        case 'E': return new Color(0.34f, 0.34f, 0.36f);
        default: return new Color(0.5f, 0.5f, 0.5f);
      }
    }

    static float Hash(int r, int c) => Mathf.Abs(((r * 73856093) ^ (c * 19349663)) % 1000) / 1000f;

    /// <summary>칸 한 점의 화면 높이: 판정 고지대·비탈(TieredTerrain.SurfaceHeight)과 같은 값, 물 칸은 운하 바닥.</summary>
    static float Height(MapSpec map, char cell, float x, float z) {
      if (cell == 'W') return TerrainKit.WaterLevel - 1.2f;
      return TieredTerrain.SurfaceHeight(map, x, z);
    }

    public static int ConvexCorners(int row, int col, System.Func<int, int, bool> keep) {
      var mask = 0;
      for (var corner = 0; corner < 4; corner++) {
        var (dr, dc) = CornerNeighbours[corner];
        if (!keep(row + dr, col) && !keep(row, col + dc)) mask |= 1 << corner;
      }
      return mask;
    }

    public static Vector2[] TileOutline(float x0, float z0, float x1, float z1, int chamfers) {
      var corners = new[] { new Vector2(x0, z0), new Vector2(x0, z1), new Vector2(x1, z1), new Vector2(x1, z0) };
      if (chamfers == 0) return corners;
      var outline = new List<Vector2>(8);
      for (var corner = 0; corner < corners.Length; corner++) {
        var point = corners[corner];
        if ((chamfers & (1 << corner)) == 0) {
          outline.Add(point);
        } else {
          outline.Add(Vector2.MoveTowards(point, corners[(corner + 3) % 4], Chamfer));
          outline.Add(Vector2.MoveTowards(point, corners[(corner + 1) % 4], Chamfer));
        }
      }
      return outline.ToArray();
    }

    public static void Build(Transform root, MapSpec map, Material material) {
      PlacedBlocks.Clear();
      CityBlockCells.Clear();
      var n = map.CellGridSize;
      var size = Size(map);
      var covered = new bool[n, n];
      // 꾸미기 블록(DressKit)이 있으면 도시·공원·주차·잔해·경계·요새 전부를 칸 지도 규칙으로 놓는다(시범 도시 블록 대신).
      var dressed = GridDress.Ready();
      if (dressed) {
        GridDress.Build(root, map);
        GridPaint.Build(root, map, material != null ? material : ArtLook.NeutralMaterial);
      } else {
        PlaceCityBlocks(root, map, covered);
      }
      PlaceBases(root, map);
      if (VisualCatalog.TryFilled(CompoundTrimSlot, out var trim)) {
        var compound = Object.Instantiate(trim.Prefab, root);
        compound.name = "CompoundTrim";
        compound.transform.localPosition = map.TraderPosition.ToUnityVector3() + Vector3.up * 0.03f;
        PlacedBlocks.Add(compound);
      }

      var vertices = new List<Vector3>();
      var colors = new List<Color>();
      var triangles = new List<int>();
      var water = new MeshKit();
      var linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
      void Face(IList<Vector3> points, Color color) {
        var start = vertices.Count;
        var tint = linear ? color.linear : color;
        foreach (var point in points) {
          vertices.Add(point);
          colors.Add(tint);
        }
        for (var i = 1; i + 1 < points.Count; i++) {
          triangles.Add(start); triangles.Add(start + i); triangles.Add(start + i + 1);
        }
      }
      void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color) => Face(new[] { a, b, c, d }, color);
      var raised = new bool[n, n];
      var detail = new bool[n, n];
      var corners = new int[n, n];
      for (var r = 0; r < n; r++) {
        for (var c = 0; c < n; c++) {
          var center = new Vector3(CellX(map, c), 0, CellZ(map, r)).ToFPVector3();
          raised[r, c] = map.HighGroundAt(center) >= 0;
          foreach (var ramp in map.Ramps) {
            if (ramp.Distance(center).AsFloat < size * 1.5f) {
              detail[r, c] = true;
              break;
            }
          }
        }
      }
      bool KeepCorner(int r, int c) => Cell(map, r, c) is 'P' or 'E' || raised[r, c];
      // 칸마다 윗면(모서리 높이를 칸 안쪽에서 읽어 절벽은 칸 경계에서 끊긴다) + 이웃보다 높은 변의 옆면.
      var heights = new float[n, n, 4];
      for (var r = 0; r < n; r++) {
        for (var c = 0; c < n; c++) {
          var cell = Cell(map, r, c);
          if (Deck(map, r, c)) cell = 'W';
          var x0 = CellX(map, c) - size / 2f;
          var x1 = x0 + size;
          var z1 = CellZ(map, r) + size / 2f;
          var z0 = z1 - size;
          const float In = 0.02f;
          // 모서리 순서: 0 남서, 1 북서, 2 북동, 3 남동.
          heights[r, c, 0] = Height(map, cell, x0 + In, z0 + In);
          heights[r, c, 1] = Height(map, cell, x0 + In, z1 - In);
          heights[r, c, 2] = Height(map, cell, x1 - In, z1 - In);
          heights[r, c, 3] = Height(map, cell, x1 - In, z0 + In);
          var lift = cell == 'W' ? 0f : 0.03f;
          // 칸마다 아주 약한 명암 차(본진·도로는 더 약하게: 8m 바둑판이 보이지 않게).
          var calm = cell == 'B' || cell == 'R' || cell == 'v' || cell == 's' || cell == 'G' || cell == 'C';
          var shade = (calm ? 0.985f : 0.96f) + (calm ? 0.03f : 0.08f) * Hash(r, c);
          var color = Tile(cell) * shade;
          color.a = 1f;
          var mask = raised[r, c] ? ConvexCorners(r, c, KeepCorner) : 0;
          corners[r, c] = mask;
          if (mask != 0) {
            var outline = TileOutline(x0, z0, x1, z1, mask);
            Face(System.Array.ConvertAll(outline, p => new Vector3(p.x, TieredTerrain.MainHeight + lift, p.y)), color);
          } else if (detail[r, c] && !raised[r, c]) {
            const int segments = 8;
            var step = size / segments;
            Vector3 Point(float x, float z) => new Vector3(x,
              Height(map, cell, Mathf.Clamp(x, x0 + In, x1 - In), Mathf.Clamp(z, z0 + In, z1 - In)) + lift, z);
            for (var u = 0; u < segments; u++) {
              for (var v = 0; v < segments; v++) {
                var x = x0 + u * step;
                var z = z0 + v * step;
                Quad(Point(x, z), Point(x, z + step), Point(x + step, z + step), Point(x + step, z), color);
              }
            }
          } else {
            Quad(new Vector3(x0, heights[r, c, 0] + lift, z0), new Vector3(x0, heights[r, c, 1] + lift, z1),
              new Vector3(x1, heights[r, c, 2] + lift, z1), new Vector3(x1, heights[r, c, 3] + lift, z0), color);
          }
          if (cell == 'E') {
            var gutter = new Color(0.46f, 0.46f, 0.43f);
            if (c == 0) Quad(new Vector3(x0 + 1.2f, .05f, z0), new Vector3(x0 + 1.2f, .05f, z1), new Vector3(x0 + 1.55f, .05f, z1), new Vector3(x0 + 1.55f, .05f, z0), gutter);
            if (c == n - 1) Quad(new Vector3(x1 - 1.55f, .05f, z0), new Vector3(x1 - 1.55f, .05f, z1), new Vector3(x1 - 1.2f, .05f, z1), new Vector3(x1 - 1.2f, .05f, z0), gutter);
            if (r == 0) Quad(new Vector3(x0, .051f, z1 - 1.55f), new Vector3(x0, .051f, z1 - 1.2f), new Vector3(x1, .051f, z1 - 1.2f), new Vector3(x1, .051f, z1 - 1.55f), gutter);
            if (r == n - 1) Quad(new Vector3(x0, .051f, z0 + 1.2f), new Vector3(x0, .051f, z0 + 1.55f), new Vector3(x1, .051f, z0 + 1.55f), new Vector3(x1, .051f, z0 + 1.2f), gutter);
          }
          if (cell == 'W' || cell == 'G') {
            var y = TerrainKit.WaterLevel;
            water.Quad(new Vector3(x0, y, z0), new Vector3(x0, y, z1), new Vector3(x1, y, z1), new Vector3(x1, y, z0), Vector3.up);
          }
        }
      }
      for (var r = 0; r < n; r++) {
        for (var c = 0; c < n; c++) {
          var cell = Cell(map, r, c);
          var x0 = CellX(map, c) - size / 2f;
          var x1 = x0 + size;
          var z1 = CellZ(map, r) + size / 2f;
          var z0 = z1 - size;
          var face = Tile(cell) * (cell == 'B' || cell == 'p' || cell == 'v' ? 0.64f : 0.78f);
          face.a = 1f;
          // 북(r-1): 이 칸 1·2 모서리 vs 이웃 0·3.
          Side(r - 1, c, 1, 2, 0, 3, new Vector3(x0, 0, z1), new Vector3(x1, 0, z1));
          // 남(r+1): 이 칸 3·0 vs 이웃 2·1.
          Side(r + 1, c, 3, 0, 2, 1, new Vector3(x1, 0, z0), new Vector3(x0, 0, z0));
          // 서(c-1): 이 칸 0·1 vs 이웃 3·2.
          Side(r, c - 1, 0, 1, 3, 2, new Vector3(x0, 0, z0), new Vector3(x0, 0, z1));
          // 동(c+1): 이 칸 2·3 vs 이웃 1·0.
          Side(r, c + 1, 2, 3, 1, 0, new Vector3(x1, 0, z1), new Vector3(x1, 0, z0));

          var mask = corners[r, c];
          if (mask != 0) {
            var tile = TileOutline(x0, z0, x1, z1, 0);
            for (var i = 0; i < tile.Length; i++) {
              if ((mask & (1 << i)) == 0) continue;
              var point = tile[i];
              var a = Vector2.MoveTowards(point, tile[(i + 3) % 4], Chamfer);
              var b = Vector2.MoveTowards(point, tile[(i + 1) % 4], Chamfer);
              var bottom = new Vector3(point.x, .03f, point.y);
              Retaining(bottom, bottom, new Vector3(a.x, TieredTerrain.MainHeight + .03f, a.y),
                new Vector3(b.x, TieredTerrain.MainHeight + .03f, b.y));
            }
          }

          void Retaining(Vector3 a0, Vector3 b0, Vector3 a1, Vector3 b1) {
            var t = .85f;
            var a = Vector3.Lerp(a0, a1, t);
            var b = Vector3.Lerp(b0, b1, t);
            Quad(b0, b, a, a0, new Color(.36f, .36f, .34f));
            Quad(b, b1, a1, a, new Color(.67f, .64f, .58f));
          }

          void Side(int nr, int nc, int ia, int ib, int na, int nb, Vector3 pa, Vector3 pb) {
            if (nr < 0 || nc < 0 || nr >= n || nc >= n) return;
            var ha = heights[r, c, ia];
            var hb = heights[r, c, ib];
            var la = heights[nr, nc, na];
            var lb = heights[nr, nc, nb];
            var neighbour = Cell(map, nr, nc);
            if (ha - la < .05f && hb - lb < .05f) return;
            if (raised[r, c] && !raised[nr, nc] && neighbour != 'W') {
              var ta = (corners[r, c] & (1 << ia)) != 0 ? Vector3.MoveTowards(pa, pb, Chamfer) : pa;
              var tb = (corners[r, c] & (1 << ib)) != 0 ? Vector3.MoveTowards(pb, pa, Chamfer) : pb;
              Retaining(new Vector3(pa.x, la + .03f, pa.z), new Vector3(pb.x, lb + .03f, pb.z),
                new Vector3(ta.x, ha + .03f, ta.z), new Vector3(tb.x, hb + .03f, tb.z));
              return;
            }
            Quad(new Vector3(pb.x, lb, pb.z), new Vector3(pb.x, hb + .03f, pb.z), new Vector3(pa.x, ha + .03f, pa.z), new Vector3(pa.x, la, pa.z), face);
          }
        }
      }
      // Mark the apron in world space using the same grade as the rendered ground and units.
      foreach (var ramp in map.Ramps) {
        var center = ramp.Center.ToUnityVector3();
        var forward = -new Vector3(center.x, 0, center.z).normalized;
        var lateral = Vector3.Cross(forward, Vector3.up);
        Vector3 Point(float along, float across) {
          var pos = center + forward * along + lateral * across;
          pos.y = TieredTerrain.SurfaceHeight(map, pos.x, pos.z) + .075f;
          return pos;
        }
        var paint = new Color(.81f, .70f, .43f);
        for (var u = -12; u < 14; u++) {
          if ((u + 12) % 4 < 2) Quad(Point(u, -.13f), Point(u, .13f), Point(u + 1, .13f), Point(u + 1, -.13f), paint);
        }
      }
      AddOuterBuffer(Half(map), Quad);
      var mesh = new Mesh { name = "grid_map_tiles", indexFormat = IndexFormat.UInt32 };
      mesh.SetVertices(vertices);
      mesh.SetColors(colors);
      mesh.SetTriangles(triangles, 0);
      mesh.RecalculateNormals();
      mesh.RecalculateBounds();
      var tiles = new GameObject("GridMapTiles", typeof(MeshFilter), typeof(MeshRenderer));
      tiles.transform.SetParent(root, false);
      tiles.GetComponent<MeshFilter>().sharedMesh = mesh;
      var renderer = tiles.GetComponent<MeshRenderer>();
      renderer.sharedMaterial = material != null ? material : ArtLook.NeutralMaterial;
      renderer.shadowCastingMode = ShadowCastingMode.On;
      var waterMaterial = UrbanDecor.WaterMaterial();
      if (water.TriangleCount > 0 && waterMaterial != null) {
        var canal = new GameObject("GridCanal", typeof(MeshFilter), typeof(MeshRenderer));
        canal.transform.SetParent(root, false);
        var waterMesh = water.ToMesh("grid_canal");
        waterMesh.colors = new Color[waterMesh.vertexCount];
        canal.GetComponent<MeshFilter>().sharedMesh = waterMesh;
        canal.GetComponent<MeshRenderer>().sharedMaterial = waterMaterial;
        canal.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
      }
    }

    static void AddOuterBuffer(float half, System.Action<Vector3, Vector3, Vector3, Vector3, Color> quad) {
      Vector3[] Ring(float offset, float cut, float y) {
        var h = half + offset;
        var s = h - cut;
        return new[] { new Vector3(-s, y, -h), new Vector3(-h, y, -s), new Vector3(-h, y, s), new Vector3(-s, y, h),
          new Vector3(s, y, h), new Vector3(h, y, s), new Vector3(h, y, -s), new Vector3(s, y, -h) };
      }
      var inner = Ring(0, 0, .028f);
      foreach (var (width, cut, y, tint) in new[] {
        (1.4f, .6f, .01f, new Color(.57f, .55f, .48f)),
        (5f, 2.5f, -.2f, new Color(.38f, .39f, .37f)),
        (13f, 8f, -.65f, new Color(.49f, .48f, .40f)),
        (26f, 19f, -2f, new Color(.42f, .44f, .38f)),
        (44f, 33f, -7f, new Color(.37f, .40f, .36f)),
      }) {
        var outer = Ring(width, cut, y);
        for (var i = 0; i < inner.Length; i++) {
          var next = (i + 1) % inner.Length;
          quad(inner[i], outer[i], outer[next], inner[next], tint);
        }
        inner = outer;
      }
    }

    /// <summary>자리 0(남서) 본진 칸의 안쪽 모서리. 편집기 BlockKit·생성기와 같은 규칙(헬기장에서 B·p·v로 이어진 칸).</summary>
    public static Vector3 SeatZeroCorner(MapSpec map) {
      var n = map.CellGridSize;
      var seen = new HashSet<Vector2Int>();
      var queue = new Queue<Vector2Int>();
      queue.Enqueue(new Vector2Int(n - 8, 8));
      int minC = int.MaxValue, maxR = int.MinValue;
      while (queue.Count > 0) {
        var p = queue.Dequeue();
        if (p.x < 0 || p.y < 0 || p.x >= n || p.y >= n || seen.Contains(p)) continue;
        var ch = Cell(map, p.x, p.y);
        if (ch != 'B' && ch != 'p' && ch != 'v') continue;
        seen.Add(p);
        minC = Mathf.Min(minC, p.y);
        maxR = Mathf.Max(maxR, p.x);
        queue.Enqueue(new Vector2Int(p.x + 1, p.y));
        queue.Enqueue(new Vector2Int(p.x - 1, p.y));
        queue.Enqueue(new Vector2Int(p.x, p.y + 1));
        queue.Enqueue(new Vector2Int(p.x, p.y - 1));
      }
      return new Vector3(CellX(map, minC) - Size(map) / 2f, 0f, CellZ(map, maxR) - Size(map) / 2f);
    }

    static void PlaceBases(Transform root, MapSpec map) {
      if (VisualCatalog.TryFilled(BaseSlot, out var slot) == false) return;
      var corner = SeatZeroCorner(map);
      BaseCorner = corner;
      for (var seat = 0; seat < 4; seat++) {
        // 판정의 자리 회전(반시계 90도: (x, z) → (-z, x))과 같게 돌린다. 유니티 Y 회전은 시계 방향이 +다.
        var p = corner;
        for (var t = 0; t < seat; t++) p = new Vector3(-p.z, 0f, p.x);
        var block = Object.Instantiate(slot.Prefab, root);
        block.name = $"Base{seat}";
        block.transform.localPosition = new Vector3(p.x, TieredTerrain.MainHeight, p.z);
        block.transform.localRotation = Quaternion.Euler(0f, -90f * seat, 0f);
        PlacedBlocks.Add(block);
      }
    }

    /// <summary>2×2가 모두 H인 칸 묶음(겹치지 않게 북서부터)에 도시 블록을 놓는다. 종류·방향은 칸 해시.</summary>
    static void PlaceCityBlocks(Transform root, MapSpec map, bool[,] covered) {
      var n = map.CellGridSize;
      var slots = new List<VisualSlot>();
      foreach (var id in CitySlots) {
        if (VisualCatalog.TryFilled(id, out var slot)) slots.Add(slot);
      }
      if (slots.Count == 0) return;
      for (var r = 0; r + 1 < n; r++) {
        for (var c = 0; c + 1 < n; c++) {
          if (covered[r, c] || covered[r, c + 1] || covered[r + 1, c] || covered[r + 1, c + 1]) continue;
          if (Cell(map, r, c) != 'H' || Cell(map, r, c + 1) != 'H' || Cell(map, r + 1, c) != 'H' || Cell(map, r + 1, c + 1) != 'H') continue;
          covered[r, c] = covered[r, c + 1] = covered[r + 1, c] = covered[r + 1, c + 1] = true;
          var h = Mathf.Abs((r * 92821) ^ (c * 68917));
          var slot = slots[h % slots.Count];
          var block = Object.Instantiate(slot.Prefab, root);
          block.name = $"City_{r}_{c}";
          block.transform.localPosition = new Vector3(CellX(map, c + 0.5f), 0.03f, CellZ(map, r + 0.5f));
          block.transform.localRotation = Quaternion.Euler(0f, 90f * (h / 7 % 4), 0f);
          PlacedBlocks.Add(block);
          CityBlockCells.Add(new Vector2Int(r, c));
        }
      }
    }
  }
}
