namespace FactoryWars.View {
  using System.Collections.Generic;
  using Quantum;
  using UnityEngine;
  using UnityEngine.Rendering;

  /// <summary>
  /// Map4P v4 바닥 칠하기(PROMPT-MAP4P-V4-DRESS 2-A1): 칸 지도 글자대로 인도·잔디·주차선·잔해 바닥·야적장·부두 포장·차선·횡단보도·연석을 칠한다.
  /// 대각 도로의 8m 계단은 네 칸이 만나는 꼭짓점마다 모서리를 깎아(길 한 칸만 볼록: 인도 삼각형 3m, 길 아닌 칸 한 칸만 오목: 아스팔트 삼각형 1.8m)
  /// 연석이 대각선을 따라 이어지게 한다. 판정(NavMesh)은 바꾸지 않는다. 10×10칸(80m) 구역마다 메시 하나(화면 밖 잘림).
  /// 화면 전용이며 시뮬레이션 상태를 읽기만 한다.
  /// </summary>
  public static class GridPaint {
    const int Chunk = 10;
    const float RoadCut = 3.0f;
    const float SideCut = 1.8f;
    const float Curb = 0.28f;

    static readonly Color Sidewalk = new Color(.66f, .63f, .58f);
    static readonly Color Joint = new Color(.57f, .54f, .50f);
    static readonly Color CurbTop = new Color(.80f, .78f, .72f);
    static readonly Color Asphalt = new Color(.25f, .26f, .28f);
    static readonly Color AlleyGutter = new Color(.29f, .29f, .30f);
    static readonly Color Patch = new Color(.235f, .245f, .26f);
    static readonly Color WallStain = new Color(.30f, .33f, .31f);
    static readonly Color WallJoint = new Color(.42f, .41f, .38f);
    static readonly Color White = new Color(.88f, .88f, .84f);
    static readonly Color Yellow = new Color(.93f, .72f, .26f);
    static readonly Color Grass = new Color(.42f, .54f, .32f);
    static readonly Color GrassDark = new Color(.36f, .47f, .28f);
    static readonly Color Path = new Color(.71f, .65f, .53f);
    static readonly Color Lot = new Color(.31f, .32f, .33f);
    static readonly Color Rubble = new Color(.57f, .52f, .45f);
    static readonly Color Dirt = new Color(.47f, .40f, .32f);
    static readonly Color Yard = new Color(.58f, .57f, .53f);
    static readonly Color Stone = new Color(.71f, .67f, .59f);
    static readonly Color Coping = new Color(.82f, .80f, .74f);
    static readonly Color Industrial = new Color(.54f, .53f, .49f);
    static readonly Color Hazard = new Color(.90f, .52f, .18f);
    static readonly Color Stain = new Color(.40f, .40f, .38f);
    static readonly Color Compound = new Color(.61f, .59f, .54f);

    sealed class Kit {
      public readonly List<Vector3> V = new List<Vector3>();
      public readonly List<Color> C = new List<Color>();
      public readonly List<int> T = new List<int>();
      readonly bool _linear = QualitySettings.activeColorSpace == ColorSpace.Linear;

      public void Poly(IList<Vector2> points, float y, Color color) {
        var start = V.Count;
        var tint = _linear ? color.linear : color;
        foreach (var p in points) { V.Add(new Vector3(p.x, y, p.y)); C.Add(tint); }
        // 시계/반시계 관계없이 위를 보게: 넓이 부호로 순서를 정한다.
        var area = 0f;
        for (var i = 0; i < points.Count; i++) { var a = points[i]; var b = points[(i + 1) % points.Count]; area += a.x * b.y - b.x * a.y; }
        for (var i = 1; i + 1 < points.Count; i++) {
          if (area < 0) { T.Add(start); T.Add(start + i); T.Add(start + i + 1); } else { T.Add(start); T.Add(start + i + 1); T.Add(start + i); }
        }
      }

      public void Strip(Vector2 a, Vector2 b, float width, float y, Color color) {
        var d = b - a;
        if (d.sqrMagnitude < 1e-4f) return;
        var side = new Vector2(-d.y, d.x).normalized * (width * .5f);
        Poly(new[] { a - side, b - side, b + side, a + side }, y, color);
      }

      /// <summary>세로 벽면 사각형(a→b 수평선, y0~y1). normal 쪽을 본다.</summary>
      public void Wall(Vector2 a, Vector2 b, float y0, float y1, Vector2 normal, Color color) {
        var start = V.Count;
        var tint = _linear ? color.linear : color;
        var off = normal * 0.03f;
        V.Add(new Vector3(a.x + off.x, y0, a.y + off.y)); V.Add(new Vector3(a.x + off.x, y1, a.y + off.y));
        V.Add(new Vector3(b.x + off.x, y1, b.y + off.y)); V.Add(new Vector3(b.x + off.x, y0, b.y + off.y));
        for (var i = 0; i < 4; i++) C.Add(tint);
        var d = b - a;
        var facing = new Vector2(d.y, -d.x);
        if (Vector2.Dot(facing, normal) > 0) { T.Add(start); T.Add(start + 1); T.Add(start + 2); T.Add(start); T.Add(start + 2); T.Add(start + 3); }
        else { T.Add(start); T.Add(start + 2); T.Add(start + 1); T.Add(start); T.Add(start + 3); T.Add(start + 2); }
      }

      public void Rect(float x0, float z0, float x1, float z1, float y, Color color) => Poly(new[] { new Vector2(x0, z0), new Vector2(x1, z0), new Vector2(x1, z1), new Vector2(x0, z1) }, y, color);
    }

    static int Hash(int r, int c, int salt) {
      unchecked {
        var h = (uint)(r * 92821) ^ (uint)(c * 68917) ^ (uint)(salt * 40503);
        h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
        return (int)(h & 0x7fffffff);
      }
    }

    static bool RoadCell(char ch) => ch == 'R' || ch == 's' || ch == 'G';
    static bool Paved(char ch) => ch == 'H' || ch == 'K' || ch == 'L' || ch == 'X' || ch == 'Y' || ch == 'w' || ch == 'M' || ch == 'C';

    public static void Build(Transform root, MapSpec map, Material source) {
      var n = map.CellGridSize;
      var size = map.CellGridCell.AsFloat;
      var half = n * size / 2f;
      var chunks = (n + Chunk - 1) / Chunk;
      var kits = new Kit[chunks, chunks];
      for (var i = 0; i < chunks; i++) for (var j = 0; j < chunks; j++) kits[i, j] = new Kit();
      char Cell(int r, int c) => GridMapView.Cell(map, r, c);
      // 평지 칸: 고지대·경사로·물이 아닌 칸(바닥 높이 0).
      var flat = new bool[n + 2, n + 2];
      for (var r = 0; r < n; r++) {
        for (var c = 0; c < n; c++) {
          var ch = Cell(r, c);
          var center = new Vector3(GridMapView.CellX(map, c), 0, GridMapView.CellZ(map, r));
          var high = map.HighGroundAt(center.ToFPVector3()) >= 0;
          flat[r + 1, c + 1] = !high && ch != 'W' && ch != 'P' && ch != 'E' && ch != 'B' && ch != 'p' && ch != 'v' &&
            Mathf.Abs(TieredTerrain.SurfaceHeight(map, center.x, center.z)) < 0.02f;
        }
      }
      bool Flat(int r, int c) => r >= 0 && c >= 0 && r < n && c < n && flat[r + 1, c + 1];
      bool IsRoad(int r, int c) => Flat(r, c) && RoadCell(Cell(r, c));
      bool IsPaved(int r, int c) => Flat(r, c) && Paved(Cell(r, c));

      // 꼭짓점(행 경계 i, 열 경계 j) 깎기: 네 칸(i-1,j-1)(i-1,j)(i,j-1)(i,j) 중 길·포장 구성으로 정한다.
      // 반환: 0 없음, 1 길 하나만(길 칸 모서리를 인도로), 3 포장 하나만(포장 칸 모서리를 아스팔트로). which = 그 칸 번호(0..3).
      (int Kind, int Which) Vertex(int i, int j) {
        var cells = new[] { (i - 1, j - 1), (i - 1, j), (i, j - 1), (i, j) };
        int roads = 0, paved = 0, lastRoad = -1, lastPaved = -1;
        for (var k = 0; k < 4; k++) {
          var (r, c) = cells[k];
          if (IsRoad(r, c)) { roads++; lastRoad = k; } else if (IsPaved(r, c)) { paved++; lastPaved = k; }
        }
        if (roads == 1 && paved == 3) return (1, lastRoad);
        if (roads == 3 && paved == 1) return (3, lastPaved);
        return (0, -1);
      }
      float Trim(int i, int j) { var v = Vertex(i, j); return v.Kind == 1 ? RoadCut : v.Kind == 3 ? SideCut : 0f; }
      Vector2 Corner(int i, int j) => new Vector2(-half + j * size, half - i * size);

      for (var r = 0; r < n; r++) {
        for (var c = 0; c < n; c++) {
          if (!Flat(r, c)) continue;
          var ch = Cell(r, c);
          var kit = kits[r / Chunk, c / Chunk];
          var x0 = -half + c * size;
          var z1 = half - r * size;
          var x1 = x0 + size;
          var z0 = z1 - size;
          var cx = (x0 + x1) / 2f;
          var cz = (z0 + z1) / 2f;
          var h = Hash(r, c, 1);
          // 이 칸 네 모서리(남서 = 꼭짓점 (r+1,c), 북서 (r,c), 북동 (r,c+1), 남동 (r+1,c+1)). Which: 0 북서칸 1 북동칸 2 남서칸 3 남동칸 기준.
          var vx = new[] { (r + 1, c, 1), (r, c, 3), (r, c + 1, 2), (r + 1, c + 1, 0) }; // (i, j, 이 칸의 Which)
          var cut = new float[4];
          for (var k = 0; k < 4; k++) {
            var (i, j, me) = vx[k];
            var v = Vertex(i, j);
            if (v.Which == me) cut[k] = v.Kind == 1 ? RoadCut : SideCut;
          }
          var outline = Outline(x0, z0, x1, z1, cut);

          if (RoadCell(ch)) {
            // 길 칸 볼록 모서리 → 인도 삼각형 + 대각 연석.
            for (var k = 0; k < 4; k++) {
              if (cut[k] <= 0f) continue;
              var p = Corners(x0, z0, x1, z1)[k];
              var a = Vector2.MoveTowards(p, Corners(x0, z0, x1, z1)[(k + 3) % 4], cut[k]);
              var b = Vector2.MoveTowards(p, Corners(x0, z0, x1, z1)[(k + 1) % 4], cut[k]);
              kit.Poly(new[] { p, a, b }, .10f, Sidewalk);
              kit.Strip(a, b, Curb, .13f, CurbTop);
            }
            if (ch == 'R') {
              RoadMarks(kit, map, r, c, cx, cz, size);
              // 길 가장자리 흰 실선(연석 0.75m 안쪽, 깎은 모서리만큼 줄임).
              var edges = new[] { (0, -1, r + 1, c, r + 1, c + 1), (1, 0, r, c + 1, r + 1, c + 1), (0, 1, r, c, r, c + 1), (-1, 0, r, c, r + 1, c) };
              foreach (var (dx, dz, i0, j0, i1, j1) in edges) {
                if (!IsPaved(r - dz, c + dx)) continue;
                var a = Corner(i0, j0);
                var b = Corner(i1, j1);
                var dir = (b - a).normalized;
                var inward = new Vector2(-dx, -dz) * .75f;
                kit.Strip(a + dir * (Trim(i0, j0) + .9f) + inward, b - dir * (Trim(i1, j1) + .9f) + inward, .14f, .047f, White);
              }
              // 넓은 아스팔트 한가운데(8방이 모두 간선): 노란 정차 금지 격자, 해시로 절반.
              var interior = true;
              for (var dr = -1; dr <= 1 && interior; dr++) for (var dc = -1; dc <= 1; dc++) if (Cell(r + dr, c + dc) != 'R') { interior = false; break; }
              if (interior && h % 2 == 0) {
                var q = size / 2f - .9f;
                kit.Strip(new Vector2(cx - q, cz - q), new Vector2(cx + q, cz - q), .16f, .048f, Yellow);
                kit.Strip(new Vector2(cx + q, cz - q), new Vector2(cx + q, cz + q), .16f, .048f, Yellow);
                kit.Strip(new Vector2(cx + q, cz + q), new Vector2(cx - q, cz + q), .16f, .048f, Yellow);
                kit.Strip(new Vector2(cx - q, cz + q), new Vector2(cx - q, cz - q), .16f, .048f, Yellow);
                for (var k = -2; k <= 2; k++) {
                  var o = k * 2.2f;
                  kit.Strip(new Vector2(cx - q + Mathf.Max(0, o), cz - q + Mathf.Max(0, -o)), new Vector2(cx + q - Mathf.Max(0, -o), cz + q - Mathf.Max(0, o)), .14f, .049f, Yellow);
                }
              } else if (interior && h % 3 == 1) {
                Manhole(kit, new Vector2(cx + 1.6f, cz - 1.1f));
              }
            }
            else if (ch == 's') {
              // 골목: 양옆 배수로(길 아닌 이웃 쪽) + 맨홀·땜질.
              if (!RoadCell(Cell(r, c - 1))) kit.Rect(x0 + .1f, z0, x0 + .6f, z1, .045f, AlleyGutter);
              if (!RoadCell(Cell(r, c + 1))) kit.Rect(x1 - .6f, z0, x1 - .1f, z1, .045f, AlleyGutter);
              if (!RoadCell(Cell(r - 1, c))) kit.Rect(x0, z1 - .6f, x1, z1 - .1f, .046f, AlleyGutter);
              if (!RoadCell(Cell(r + 1, c))) kit.Rect(x0, z0 + .1f, x1, z0 + .6f, .046f, AlleyGutter);
            }
            if (h % 5 == 0) {
              // 땜질: 기울어진 직사각형(도로 보수 자국).
              var pc = new Vector2(cx + (h % 5 - 2) * .9f, cz + (h / 5 % 5 - 2) * .9f);
              var ang = (h % 4) * 0.4f;
              var u = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
              var v = new Vector2(-u.y, u.x);
              var lu = 1.0f + h % 3 * .6f;
              var lv = .6f + h / 7 % 3 * .35f;
              kit.Poly(new[] { pc - u * lu - v * lv, pc + u * lu - v * lv, pc + u * lu + v * lv, pc - u * lu + v * lv }, .05f, Patch);
            }
            // Task 39: drawbridge deck cells are drawn by DrawbridgeView (moving leaves).
            if (ch == 'G' && DrawbridgeView.InDrawbridge(map, new Vector3((x0 + x1) / 2f, 0f, (z0 + z1) / 2f))) continue;
            if (ch == 'G') {
              kit.Rect(x0, z0, x1, z1, .04f, new Color(.46f, .45f, .43f));
              for (var k = 0; k < 4; k++) kit.Rect(x0 + .3f + k * 2f, z0, x0 + .42f + k * 2f, z1, .05f, new Color(.38f, .37f, .36f));
              if (GridMapView.Deck(map, r, c)) {
                // Task 39: deck fascia (0.7 m) on the sides that face open water; the canal runs on underneath.
                var fascia = new Color(.40f, .39f, .37f);
                if (Cell(r + 1, c) == 'W') kit.Wall(new Vector2(x0, z0), new Vector2(x1, z0), -.66f, .04f, new Vector2(0, -1), fascia);
                if (Cell(r - 1, c) == 'W') kit.Wall(new Vector2(x1, z1), new Vector2(x0, z1), -.66f, .04f, new Vector2(0, 1), fascia);
                if (Cell(r, c - 1) == 'W') kit.Wall(new Vector2(x0, z1), new Vector2(x0, z0), -.66f, .04f, new Vector2(-1, 0), fascia);
                if (Cell(r, c + 1) == 'W') kit.Wall(new Vector2(x1, z0), new Vector2(x1, z1), -.66f, .04f, new Vector2(1, 0), fascia);
              }
            }
            continue;
          }
          if (!Paved(ch)) continue;

          // 깎인 모서리(아스팔트).
          for (var k = 0; k < 4; k++) {
            if (cut[k] <= 0f) continue;
            var p = Corners(x0, z0, x1, z1)[k];
            kit.Poly(new[] { p, Vector2.MoveTowards(p, Corners(x0, z0, x1, z1)[(k + 3) % 4], cut[k]), Vector2.MoveTowards(p, Corners(x0, z0, x1, z1)[(k + 1) % 4], cut[k]) }, .045f, Asphalt);
          }
          var slab = .10f;
          switch (ch) {
            case 'H': {
              kit.Poly(outline, slab, Sidewalk * (.97f + h % 7 * .01f));
              for (var k = 1; k < 4; k++) {
                kit.Strip(new Vector2(x0 + k * 2f, z0 + .3f), new Vector2(x0 + k * 2f, z1 - .3f), .06f, slab + .004f, Joint);
                kit.Strip(new Vector2(x0 + .3f, z0 + k * 2f), new Vector2(x1 - .3f, z0 + k * 2f), .06f, slab + .004f, Joint);
              }
              break;
            }
            case 'K': {
              kit.Poly(outline, slab - .02f, Grass);
              Blob(kit, new Vector2(cx + (h % 3 - 1) * 1.4f, cz + 1.2f), 1.6f, slab - .015f, GrassDark, h);
              var horizontal = h % 2 == 0;
              if (horizontal) kit.Strip(new Vector2(x0, cz - 1.4f), new Vector2(x1, cz - 1.4f), 1.2f, slab - .01f, Path);
              else kit.Strip(new Vector2(cx + .8f, z0), new Vector2(cx + .8f, z1), 1.2f, slab - .01f, Path);
              break;
            }
            case 'L': {
              kit.Poly(outline, .06f, Lot);
              foreach (var lx in new[] { -2.6f, .05f, 2.7f }) kit.Strip(new Vector2(cx + lx, cz - 2.1f), new Vector2(cx + lx, cz + 3.0f), .13f, .065f, White);
              kit.Strip(new Vector2(x0 + .6f, cz - 2.1f), new Vector2(x1 - .6f, cz - 2.1f), .13f, .065f, White);
              if (h % 3 == 0) Blob(kit, new Vector2(cx + 1.3f, cz + .4f), .8f, .064f, Stain, h);
              break;
            }
            case 'X': {
              kit.Poly(outline, .07f, Rubble);
              Blob(kit, new Vector2(cx - 1.3f + h % 3, cz + .6f), 2.2f, .075f, Dirt, h);
              Blob(kit, new Vector2(cx + 1.8f, cz - 1.7f + h % 2), 1.3f, .076f, Dirt * 1.08f, h / 3);
              kit.Strip(new Vector2(x0 + 1f + h % 3, z0 + .8f), new Vector2(x1 - 1.5f, z1 - 1f - h % 2), .08f, .078f, Stain);
              break;
            }
            case 'Y': case 'M': {
              kit.Poly(outline, .07f, ch == 'Y' ? Yard : Industrial);
              kit.Strip(new Vector2(x0, z0 + .05f), new Vector2(x1, z0 + .05f), .08f, .075f, Joint);
              kit.Strip(new Vector2(x0 + .05f, z0), new Vector2(x0 + .05f, z1), .08f, .075f, Joint);
              if (ch == 'Y') kit.Strip(new Vector2(x0 + .8f, cz - 1.2f), new Vector2(x1 - .8f, cz - 1.2f), .14f, .077f, Yellow);
              else if (h % 7 == 0) for (var k = 0; k < 3 + h % 2; k++) kit.Strip(new Vector2(x0 + 1.2f + k * 1.6f, z0 + .4f + h % 3 * 2.2f), new Vector2(x0 + 1.9f + k * 1.6f, z0 + 1.1f + h % 3 * 2.2f), .3f, .077f, Hazard);
              else if (h % 7 == 3) kit.Strip(new Vector2(x0 + .6f, cz + 1.8f), new Vector2(x1 - .6f, cz + 1.8f), .12f, .076f, Yellow);
              if (h % 4 == 1) Blob(kit, new Vector2(cx - .9f, cz + 1.1f), .9f, .076f, Stain, h);
              break;
            }
            case 'w': {
              kit.Poly(outline, slab, Stone * (.97f + h % 5 * .012f));
              for (var k = 1; k < 8; k++) kit.Strip(new Vector2(x0 + k, z0 + .2f), new Vector2(x0 + k, z1 - .2f), .05f, slab + .004f, Joint);
              // 물 쪽 갓돌.
              if (Cell(r, c - 1) == 'W') kit.Rect(x0, z0, x0 + .5f, z1, slab + .01f, Coping);
              if (Cell(r, c + 1) == 'W') kit.Rect(x1 - .5f, z0, x1, z1, slab + .01f, Coping);
              if (Cell(r - 1, c) == 'W') kit.Rect(x0, z1 - .5f, x1, z1, slab + .011f, Coping);
              if (Cell(r + 1, c) == 'W') kit.Rect(x0, z0, x1, z0 + .5f, slab + .011f, Coping);
              break;
            }
            case 'C': {
              // 요새 바닥은 칸 타일(0.03m) 위, 십자 도로(요새 장식 0.048m~)보다 아래에 칠한다.
              kit.Poly(outline, .036f, Compound * (.97f + h % 5 * .01f));
              kit.Strip(new Vector2(x0, z0 + .04f), new Vector2(x0 + size, z0 + .04f), .07f, .039f, Joint);
              kit.Strip(new Vector2(x0 + .04f, z0), new Vector2(x0 + .04f, z1), .07f, .039f, Joint);
              if (h % 5 == 0) Blob(kit, new Vector2(cx + .6f, cz - .8f), 1.1f, .040f, Stain, h);
              if (h % 9 == 4) for (var k = 0; k < 4; k++) kit.Strip(new Vector2(x0 + 1.4f + k * 1.5f, z1 - 1.2f), new Vector2(x0 + 2.0f + k * 1.5f, z1 - .6f), .3f, .041f, Hazard);
              break;
            }
          }
          // 길과 맞닿은 변의 연석(꼭짓점 깎기만큼 줄임) + 깎인 모서리 대각 연석.
          var sides = new[] { (0, -1, r + 1, c, r + 1, c + 1), (1, 0, r, c + 1, r + 1, c + 1), (0, 1, r, c, r, c + 1), (-1, 0, r, c, r + 1, c) };
          foreach (var (dx, dz, i0, j0, i1, j1) in sides) {
            var nr = r - dz;
            var nc = c + dx;
            if (!IsRoad(nr, nc)) continue;
            var a = Corner(i0, j0);
            var b = Corner(i1, j1);
            var inward = new Vector2(-dx, -dz) * (Curb / 2f);
            var ta = Trim(i0, j0);
            var tb = Trim(i1, j1);
            var dir = (b - a).normalized;
            kit.Strip(a + dir * ta + inward, b - dir * tb + inward, Curb, .13f, CurbTop);
          }
          for (var k = 0; k < 4; k++) {
            if (cut[k] <= 0f || cut[k] != SideCut) continue;
            var p = Corners(x0, z0, x1, z1)[k];
            kit.Strip(Vector2.MoveTowards(p, Corners(x0, z0, x1, z1)[(k + 3) % 4], cut[k]), Vector2.MoveTowards(p, Corners(x0, z0, x1, z1)[(k + 1) % 4], cut[k]), Curb, .13f, CurbTop);
          }
        }
      }

      // 수로 벽면: 물때 띠 + 2m 이음줄 + 위 갓돌(물 쪽을 보는 면).
      var waterTop = TerrainKit.WaterLevel;
      for (var r = 0; r < n; r++) {
        for (var c = 0; c < n; c++) {
          var ch = Cell(r, c);
          if (ch == 'W' || GridMapView.Deck(map, r, c)) continue;
          if (map.HighGroundAt(new Vector3(GridMapView.CellX(map, c), 0, GridMapView.CellZ(map, r)).ToFPVector3()) >= 0) continue;
          var kit = kits[r / Chunk, c / Chunk];
          var x0 = -half + c * size;
          var z1 = half - r * size;
          var x1 = x0 + size;
          var z0 = z1 - size;
          var walls = new[] { (r + 1, c, new Vector2(x0, z0), new Vector2(x1, z0), new Vector2(0, -1)), (r - 1, c, new Vector2(x0, z1), new Vector2(x1, z1), new Vector2(0, 1)),
            (r, c - 1, new Vector2(x0, z0), new Vector2(x0, z1), new Vector2(-1, 0)), (r, c + 1, new Vector2(x1, z0), new Vector2(x1, z1), new Vector2(1, 0)) };
          foreach (var (nr, nc, a, b, normal) in walls) {
            if (Cell(nr, nc) != 'W' && !GridMapView.Deck(map, nr, nc)) continue;
            kit.Wall(a, b, waterTop - .35f, waterTop + .3f, normal, WallStain);
            kit.Wall(a, b, -.35f, .02f, normal, Coping * .9f);
            var len = (b - a).magnitude;
            var dir = (b - a) / len;
            for (var t = 2f; t < len - .1f; t += 2f) kit.Wall(a + dir * (t - .07f), a + dir * (t + .07f), waterTop + .3f, -.35f, normal * 1.5f, WallJoint);
          }
        }
      }

      var material = new Material(source) { name = "GridPaint" };
      if (material.HasProperty("_GridStrength")) material.SetFloat("_GridStrength", 0f);
      var parent = new GameObject("GridPaint").transform;
      parent.SetParent(root, false);
      for (var i = 0; i < chunks; i++) {
        for (var j = 0; j < chunks; j++) {
          var kit = kits[i, j];
          if (kit.T.Count == 0) continue;
          var mesh = new Mesh { name = $"grid_paint_{i}_{j}", indexFormat = kit.V.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
          mesh.SetVertices(kit.V);
          mesh.SetColors(kit.C);
          mesh.SetTriangles(kit.T, 0);
          var normals = new Vector3[kit.V.Count];
          for (var k = 0; k < normals.Length; k++) normals[k] = Vector3.up;
          mesh.normals = normals;
          mesh.RecalculateBounds();
          var go = new GameObject(mesh.name, typeof(MeshFilter), typeof(MeshRenderer));
          go.transform.SetParent(parent, false);
          go.isStatic = true;
          go.GetComponent<MeshFilter>().sharedMesh = mesh;
          var renderer = go.GetComponent<MeshRenderer>();
          renderer.sharedMaterial = material;
          renderer.shadowCastingMode = ShadowCastingMode.Off;
        }
      }
    }

    static void Manhole(Kit kit, Vector2 p) {
      var pts = new Vector2[10];
      for (var i = 0; i < pts.Length; i++) pts[i] = p + new Vector2(Mathf.Cos(i * Mathf.PI / 5f), Mathf.Sin(i * Mathf.PI / 5f)) * .45f;
      kit.Poly(pts, .049f, new Color(.33f, .33f, .33f));
    }

    /// <summary>칸 사각형 모서리(남서, 북서, 북동, 남동).</summary>
    static Vector2[] Corners(float x0, float z0, float x1, float z1) => new[] { new Vector2(x0, z0), new Vector2(x0, z1), new Vector2(x1, z1), new Vector2(x1, z0) };

    static Vector2[] Outline(float x0, float z0, float x1, float z1, float[] cut) {
      var corners = Corners(x0, z0, x1, z1);
      var list = new List<Vector2>(8);
      for (var k = 0; k < 4; k++) {
        if (cut[k] <= 0f) { list.Add(corners[k]); continue; }
        list.Add(Vector2.MoveTowards(corners[k], corners[(k + 3) % 4], cut[k]));
        list.Add(Vector2.MoveTowards(corners[k], corners[(k + 1) % 4], cut[k]));
      }
      return list.ToArray();
    }

    /// <summary>얼룩·땜질: 해시로 찌그러뜨린 8각형.</summary>
    static void Blob(Kit kit, Vector2 center, float radius, float y, Color color, int h) {
      var pts = new Vector2[8];
      for (var i = 0; i < 8; i++) {
        var a = (i + (h % 3) * .2f) * Mathf.PI / 4f;
        var rr = radius * (.7f + ((h >> (i * 2)) & 3) * .12f);
        pts[i] = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr;
      }
      kit.Poly(pts, y, color);
    }

    /// <summary>간선(R) 차선: 직선 구간 = 노란 가운데 점선, 대각 띠 = 흰 점선, 교차로 옆 = 횡단보도.</summary>
    static void RoadMarks(Kit kit, MapSpec map, int r, int c, float cx, float cz, float size) {
      bool R(int rr, int cc) => GridMapView.Cell(map, rr, cc) == 'R' || GridMapView.Cell(map, rr, cc) == 'G';
      bool Junction(int rr, int cc) => R(rr, cc) && (R(rr - 1, cc) ? 1 : 0) + (R(rr + 1, cc) ? 1 : 0) + (R(rr, cc - 1) ? 1 : 0) + (R(rr, cc + 1) ? 1 : 0) >= 3;
      var n = R(r - 1, c);
      var s = R(r + 1, c);
      var w = R(r, c - 1);
      var e = R(r, c + 1);
      var h = size / 2f;
      if (w && e && !n && !s) {
        if (Junction(r, c - 1) || Junction(r, c + 1)) {
          var side = Junction(r, c - 1) ? -1f : 1f;
          var x = cx + side * (h - 1.6f);
          for (var k = 0; k < 6; k++) kit.Strip(new Vector2(x - 1.1f, cz - h + .9f + k * 1.25f), new Vector2(x + 1.1f, cz - h + .9f + k * 1.25f), .55f, .048f, White);
          kit.Strip(new Vector2(cx - side * 1.2f - 1.2f, cz), new Vector2(cx - side * 1.2f + 1.2f, cz), .16f, .047f, Yellow);
        } else {
          kit.Strip(new Vector2(cx - h + .5f, cz), new Vector2(cx - .5f, cz), .16f, .047f, Yellow);
          kit.Strip(new Vector2(cx + .5f, cz), new Vector2(cx + h - .5f, cz), .16f, .047f, Yellow);
        }
        return;
      }
      if (n && s && !w && !e) {
        if (Junction(r - 1, c) || Junction(r + 1, c)) {
          var side = Junction(r - 1, c) ? 1f : -1f;
          var z = cz + side * (h - 1.6f);
          for (var k = 0; k < 6; k++) kit.Strip(new Vector2(cx - h + .9f + k * 1.25f, z - 1.1f), new Vector2(cx - h + .9f + k * 1.25f, z + 1.1f), .55f, .048f, White);
          kit.Strip(new Vector2(cx, cz - side * 1.2f - 1.2f), new Vector2(cx, cz - side * 1.2f + 1.2f), .16f, .047f, Yellow);
        } else {
          kit.Strip(new Vector2(cx, cz - h + .5f), new Vector2(cx, cz - .5f), .16f, .047f, Yellow);
          kit.Strip(new Vector2(cx, cz + .5f), new Vector2(cx, cz + h - .5f), .16f, .047f, Yellow);
        }
        return;
      }
      var ne = R(r - 1, c + 1);
      var sw = R(r + 1, c - 1);
      var nw = R(r - 1, c - 1);
      var se = R(r + 1, c + 1);
      var d = h * .55f;
      if (ne && sw && !(nw && se)) {
        kit.Strip(new Vector2(cx - d, cz - d), new Vector2(cx - .6f, cz - .6f), .16f, .047f, White);
        kit.Strip(new Vector2(cx + .6f, cz + .6f), new Vector2(cx + d, cz + d), .16f, .047f, White);
      } else if (nw && se && !(ne && sw)) {
        kit.Strip(new Vector2(cx - d, cz + d), new Vector2(cx - .6f, cz + .6f), .16f, .047f, White);
        kit.Strip(new Vector2(cx + .6f, cz - .6f), new Vector2(cx + d, cz - d), .16f, .047f, White);
      }
    }
  }
}
