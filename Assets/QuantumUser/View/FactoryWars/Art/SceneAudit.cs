namespace FactoryWars.View {
  using System.Collections.Generic;
  using System.Linq;
  using UnityEngine;

  /// <summary>
  /// 디자인 품질 게이트의 자동 금지 검사(2026-11-22, PROMPT-MAP4P-V4 2절). 어떤 장면에도 쓸 수 있게 "물건 목록(이름 + 월드 경계 상자)"만 받는다.
  /// - Overlaps: 물건끼리 경계 상자 겹침(맞닿음 허용 오차 tolerance).
  /// - FloatingOrSunk: 바닥 높이 함수와 물건 밑면 차이(뜸 &gt; 0.25m, 박힘 = 밑면이 바닥보다 0.6m 이상 아래이거나 키의 40% 이상 묻힘).
  /// - Intrusions: 물건 바닥 사각형이 금지 사각형(길·건설 부지·채집 길)과 겹침.
  /// - RegularRows: 같은 소품 4개 이상이 한 줄에 같은 간격(±8%)으로 떨어져 놓임(맞대 이은 벽 조각은 제외).
  /// - FlatColorRatio: 게임 카메라 캡처에서 같은 색 단색 면(16px 칸, 색 편차 작음)이 차지하는 가장 큰 비율. 0.30 초과면 실패.
  /// - LargestEmptySquare: 영역 래스터(2m)에서 물건·길이 없는 가장 큰 정사각형 한 변(m). 유닛 8기(중형 차량 3×6m 8대 = 144m², 한 변 12m) 이상이면 실패.
  /// - ScaleSanity: 건물 층 높이와 길 폭을 화면 보병(1.8m × 표시 1.5배)·차량(3.3m × 1.3배)과 비교.
  /// 화면 전용 검사 도구이고 시뮬레이션을 바꾸지 않는다.
  /// </summary>
  public static class SceneAudit {
    public struct Item {
      public string Name;
      public string Kind;
      public Bounds Bounds;
      public override string ToString() => $"{Name}@({Bounds.center.x:0.0},{Bounds.center.z:0.0})";
    }

    public const float FlatColorLimit = 0.30f;
    public const float InfantryDisplayHeight = 1.8f * 1.5f;
    public const float VehicleDisplayWidth = 3.3f * 1.3f;
    /// <summary>유닛 8기 = 중형 차량 3×6m 8대(144m²)를 담는 정사각형 한 변.</summary>
    public static readonly float EmptyLimit = Mathf.Sqrt(8f * 3f * 6f);

    /// <summary>프리팹 이름에서 번호·(Clone)을 떼어 "같은 소품" 종류를 만든다.</summary>
    public static string KindOf(string name) {
      var n = name.Replace("(Clone)", "").Trim();
      var cut = n.Length;
      while (cut > 0 && (char.IsDigit(n[cut - 1]) || n[cut - 1] == '_' || n[cut - 1] == ' ')) cut--;
      return n.Substring(0, cut);
    }

    public static Bounds BoundsOf(GameObject item) {
      var renderers = item.GetComponentsInChildren<Renderer>();
      var bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(item.transform.position, Vector3.zero);
      foreach (var r in renderers) bounds.Encapsulate(r.bounds);
      return bounds;
    }

    /// <summary>블록의 물건: 블록 바로 아래 자식(묶음이면 그 자식들)을 하나씩. 묶음 = 이름이 Buildings/Street/Parking/PerimeterWall인 자식.</summary>
    public static List<Item> ItemsOf(GameObject block) {
      var list = new List<Item>();
      void Add(Transform t) {
        if (t.GetComponentsInChildren<Renderer>().Length == 0) return;
        var source = t.GetComponentsInChildren<Transform>().Select(x => x.name).FirstOrDefault(x => x.StartsWith("SM_")) ?? t.name;
        list.Add(new Item { Name = $"{block.name}/{t.name}", Kind = KindOf(source), Bounds = BoundsOf(t.gameObject) });
      }
      foreach (Transform child in block.transform) {
        if (child.name == "Buildings" || child.name == "Street" || child.name == "PerimeterWall") {
          foreach (Transform inner in child) Add(inner);
        } else {
          Add(child);
        }
      }
      return list;
    }

    static bool Overlap2D(Bounds a, Bounds b, float tolerance) =>
      a.min.x + tolerance < b.max.x && b.min.x + tolerance < a.max.x && a.min.z + tolerance < b.max.z && b.min.z + tolerance < a.max.z;

    static bool Overlap3D(Bounds a, Bounds b, float tolerance) => Overlap2D(a, b, tolerance) && a.min.y + tolerance < b.max.y && b.min.y + tolerance < a.max.y;

    public static List<string> Overlaps(IList<Item> items, float tolerance = 0.15f, System.Func<Item, Item, bool> allowed = null) {
      var hits = new List<string>();
      for (var i = 0; i < items.Count; i++) {
        for (var j = i + 1; j < items.Count; j++) {
          if (Overlap3D(items[i].Bounds, items[j].Bounds, tolerance) == false) continue;
          if (allowed != null && (allowed(items[i], items[j]) || allowed(items[j], items[i]))) continue;
          hits.Add($"{items[i]} x {items[j]}");
        }
      }
      return hits;
    }

    public static List<string> FloatingOrSunk(IList<Item> items, System.Func<float, float, float> ground, float floatLimit = 0.25f) {
      var hits = new List<string>();
      foreach (var item in items) {
        var b = item.Bounds;
        var floor = ground(b.center.x, b.center.z);
        var gap = b.min.y - floor;
        if (gap > floatLimit) hits.Add($"{item} floats {gap:0.00}m");
        else if (gap < -0.6f && -gap > b.size.y * 0.4f) hits.Add($"{item} sunk {-gap:0.00}m");
      }
      return hits;
    }

    public static List<string> Intrusions(IList<Item> items, IList<(string Name, Rect Area)> keepOut, float tolerance = 0.1f) {
      var hits = new List<string>();
      foreach (var item in items) {
        var b = item.Bounds;
        foreach (var (name, area) in keepOut) {
          if (b.min.x + tolerance < area.xMax && area.xMin + tolerance < b.max.x && b.min.z + tolerance < area.yMax && area.yMin + tolerance < b.max.z) {
            hits.Add($"{item} in {name}");
          }
        }
      }
      return hits;
    }

    /// <summary>채집 길: 점 a→b 폭 width의 띠를 짧은 사각형들로 나눈다(Intrusions에 넣는다).</summary>
    public static IEnumerable<(string, Rect)> Corridor(string name, Vector2 a, Vector2 b, float width) {
      var steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / (width * 0.75f)));
      for (var i = 0; i <= steps; i++) {
        var p = Vector2.Lerp(a, b, i / (float)steps);
        yield return (name, new Rect(p.x - width / 2f, p.y - width / 2f, width, width));
      }
    }

    public static List<string> RegularRows(IList<Item> items, int minCount = 4, float spacingTolerance = 0.08f) {
      var hits = new List<string>();
      // 맞대 이은 조각(다른 물건과 0.5m 안으로 붙은 것)은 연속 구조물이라 뺀다.
      var loose = items.Where(item => items.All(other => ReferenceEquals(other.Name, item.Name) || other.Name == item.Name || Gap(item.Bounds, other.Bounds) > 0.5f)).ToList();
      foreach (var group in loose.GroupBy(i => i.Kind)) {
        var list = group.ToList();
        if (list.Count < minCount) continue;
        for (var a = 0; a < list.Count; a++) {
          for (var b = 0; b < list.Count; b++) {
            if (a == b) continue;
            var pa = new Vector2(list[a].Bounds.center.x, list[a].Bounds.center.z);
            var pb = new Vector2(list[b].Bounds.center.x, list[b].Bounds.center.z);
            var step = pb - pa;
            if (step.magnitude < 0.5f) continue;
            var run = 2;
            var next = pb + step;
            while (list.Any(x => Vector2.Distance(new Vector2(x.Bounds.center.x, x.Bounds.center.z), next) < step.magnitude * spacingTolerance)) {
              run++;
              next += step;
            }
            if (run >= minCount) {
              hits.Add($"{group.Key} x{run} every {step.magnitude:0.0}m from {list[a]}");
            }
          }
        }
      }
      return hits.Distinct().ToList();
    }

    static float Gap(Bounds a, Bounds b) {
      var dx = Mathf.Max(0f, Mathf.Max(a.min.x - b.max.x, b.min.x - a.max.x));
      var dz = Mathf.Max(0f, Mathf.Max(a.min.z - b.max.z, b.min.z - a.max.z));
      return Mathf.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>캡처에서 같은 색 단색 면 비율(가장 큰 색 묶음). 16px 칸마다 채널 표준편차가 2.5/255 미만이면 단색 칸이다.</summary>
    public static float FlatColorRatio(Texture2D image, int tile = 16) {
      var pixels = image.GetPixels32();
      var w = image.width;
      var h = image.height;
      var buckets = new Dictionary<int, int>();
      var total = 0;
      for (var ty = 0; ty + tile <= h; ty += tile) {
        for (var tx = 0; tx + tile <= w; tx += tile) {
          total++;
          double sr = 0, sg = 0, sb = 0, qr = 0, qg = 0, qb = 0;
          var count = tile * tile;
          for (var y = ty; y < ty + tile; y++) {
            for (var x = tx; x < tx + tile; x++) {
              var p = pixels[y * w + x];
              sr += p.r; sg += p.g; sb += p.b;
              qr += p.r * p.r; qg += p.g * p.g; qb += p.b * p.b;
            }
          }
          double Std(double s, double q) => System.Math.Sqrt(System.Math.Max(0, q / count - (s / count) * (s / count)));
          if (Std(sr, qr) > 2.5 || Std(sg, qg) > 2.5 || Std(sb, qb) > 2.5) continue;
          var key = (int)(sr / count / 12) * 10000 + (int)(sg / count / 12) * 100 + (int)(sb / count / 12);
          buckets[key] = buckets.TryGetValue(key, out var v) ? v + 1 : 1;
        }
      }
      return total == 0 || buckets.Count == 0 ? 0f : buckets.Values.Max() / (float)total;
    }

    /// <summary>영역(월드 XZ 사각형)을 cell m 칸으로 나눠, 물건·막힌 칸이 없는 가장 큰 정사각형 한 변(m).</summary>
    public static float LargestEmptySquare(Rect area, IList<Bounds> occupied, System.Func<float, float, bool> inside, float cell = 2f) {
      var nx = Mathf.CeilToInt(area.width / cell);
      var nz = Mathf.CeilToInt(area.height / cell);
      var best = 0;
      var dp = new int[nx + 1, nz + 1];
      for (var ix = 1; ix <= nx; ix++) {
        for (var iz = 1; iz <= nz; iz++) {
          var x = area.xMin + (ix - 0.5f) * cell;
          var z = area.yMin + (iz - 0.5f) * cell;
          var free = inside(x, z) && occupied.All(b => x < b.min.x || x > b.max.x || z < b.min.z || z > b.max.z);
          dp[ix, iz] = free ? 1 + Mathf.Min(dp[ix - 1, iz], Mathf.Min(dp[ix, iz - 1], dp[ix - 1, iz - 1])) : 0;
          best = Mathf.Max(best, dp[ix, iz]);
        }
      }
      return best * cell;
    }

    /// <summary>건물 층 높이(1층 높이)가 화면 보병 키 이상인지, 길 폭이 화면 차량 폭 2배 이상인지.</summary>
    public static List<string> ScaleSanity(IEnumerable<(string Name, float StoreyHeight)> buildings, IEnumerable<(string Name, float Width)> lanes) {
      var hits = new List<string>();
      foreach (var (name, storey) in buildings) {
        if (storey < InfantryDisplayHeight) hits.Add($"{name} storey {storey:0.0}m < infantry {InfantryDisplayHeight:0.0}m");
      }
      foreach (var (name, width) in lanes) {
        if (width < VehicleDisplayWidth * 1.8f) hits.Add($"{name} lane {width:0.0}m < 1.8 x vehicle {VehicleDisplayWidth:0.0}m");
      }
      return hits;
    }
  }
}
