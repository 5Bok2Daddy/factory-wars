namespace FactoryWars.View {
  using System.Collections.Generic;
  using UnityEngine;
  using UnityEngine.Rendering;

  /// <summary>서브메쉬 칸. 기본은 팔레트 텍스처, 팀은 플레이어 색, 발광은 켜고 끄는 머티리얼이다.</summary>
  public enum ArtSlot { Base, Team, Glow }

  /// <summary>팔레트 텍스처의 칸. 한 머티리얼로 여러 색을 칠해 SRP Batcher를 유지한다.</summary>
  public enum Swatch {
    Metal, MetalDark, MetalLight, Armor, ArmorDark, Olive, OliveDark, Sand, Earth, Rubber, Glass, Black,
    Wood, WoodDark, Bark, LeafDark, Leaf, LeafLight, Grass, Rock, RockDark, Ore, Stone, Water, White,
    GlowCyan, GlowOrange, GlowCrystal,
  }

  public static class ArtPalette {
    public const int Size = 32;

    static readonly Color32[] Colors = {
      new Color32(0x7E, 0x80, 0x82, 0xFF), new Color32(0x4C, 0x4E, 0x52, 0xFF), new Color32(0xA6, 0xA4, 0x9E, 0xFF),
      new Color32(0x7C, 0x76, 0x6A, 0xFF), new Color32(0x56, 0x52, 0x4A, 0xFF), new Color32(0x6E, 0x6A, 0x50, 0xFF), new Color32(0x4C, 0x4C, 0x3A, 0xFF),
      new Color32(0xA8, 0x98, 0x78, 0xFF), new Color32(0x7A, 0x5C, 0x3E, 0xFF), new Color32(0x26, 0x26, 0x28, 0xFF), new Color32(0x30, 0x3C, 0x46, 0xFF), new Color32(0x14, 0x14, 0x16, 0xFF),
      new Color32(0x74, 0x5A, 0x42, 0xFF), new Color32(0x4A, 0x38, 0x2A, 0xFF), new Color32(0x4E, 0x34, 0x24, 0xFF),
      new Color32(0x2C, 0x5A, 0x2A, 0xFF), new Color32(0x46, 0x7E, 0x30, 0xFF), new Color32(0x6A, 0x9A, 0x3A, 0xFF), new Color32(0x5C, 0x74, 0x34, 0xFF),
      new Color32(0x7C, 0x74, 0x6A, 0xFF), new Color32(0x4A, 0x3E, 0x36, 0xFF), new Color32(0x24, 0x24, 0x28, 0xFF), new Color32(0x9C, 0x9C, 0x96, 0xFF),
      new Color32(0x1C, 0x30, 0x40, 0xFF), new Color32(0xE8, 0xE6, 0xE0, 0xFF),
      new Color32(0x40, 0xE8, 0xE0, 0xFF), new Color32(0xFF, 0x8A, 0x20, 0xFF), new Color32(0xA8, 0xF0, 0xE8, 0xFF),
    };

    public static Vector2 Uv(Swatch swatch) => new Vector2(((int)swatch + 0.5f) / Size, 0.5f);

    /// <summary>한 줄짜리 팔레트 텍스처의 픽셀. 빈 칸은 검정이다.</summary>
    public static Color32[] Pixels() {
      var pixels = new Color32[Size];
      for (var i = 0; i < Size; i++) {
        pixels[i] = i < Colors.Length ? Colors[i] : new Color32(0, 0, 0, 255);
      }
      return pixels;
    }
  }

  /// <summary>
  /// 저면수 메쉬를 코드로 조립한다. 면마다 꼭짓점을 따로 두어 평면 셰이딩이 된다.
  /// 시드는 정수 해시로 풀어서 모든 기기에서 같은 모양이 나온다.
  /// </summary>
  public class MeshKit {
    readonly List<Vector3> _vertices = new List<Vector3>();
    readonly List<Vector3> _normals = new List<Vector3>();
    readonly List<Vector2> _uvs = new List<Vector2>();
    readonly List<int>[] _triangles = { new List<int>(), new List<int>(), new List<int>() };
    readonly Stack<Matrix4x4> _stack = new Stack<Matrix4x4>();
    Matrix4x4 _matrix = Matrix4x4.identity;
    ArtSlot _slot;
    Vector2 _uv = ArtPalette.Uv(Swatch.Metal);

    public int TriangleCount => (_triangles[0].Count + _triangles[1].Count + _triangles[2].Count) / 3;

    public MeshKit Paint(Swatch swatch) {
      _slot = ArtSlot.Base;
      _uv = ArtPalette.Uv(swatch);
      return this;
    }

    public MeshKit Team() {
      _slot = ArtSlot.Team;
      return this;
    }

    public MeshKit Glow(Swatch swatch = Swatch.GlowCyan) {
      _slot = ArtSlot.Glow;
      _uv = ArtPalette.Uv(swatch);
      return this;
    }

    public void Push(Vector3 position, Vector3 euler = default, Vector3? scale = null) {
      _stack.Push(_matrix);
      _matrix *= Matrix4x4.TRS(position, Quaternion.Euler(euler), scale ?? Vector3.one);
    }

    public void Pop() {
      _matrix = _stack.Pop();
    }

    /// <summary>삼각형 하나. outward 쪽을 앞면으로 맞춘다.</summary>
    public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 outward) {
      if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f) {
        (b, c) = (c, b);
      }
      var wa = _matrix.MultiplyPoint3x4(a);
      var wb = _matrix.MultiplyPoint3x4(b);
      var wc = _matrix.MultiplyPoint3x4(c);
      var normal = Vector3.Cross(wb - wa, wc - wa).normalized;
      var start = _vertices.Count;
      _vertices.Add(wa);
      _vertices.Add(wb);
      _vertices.Add(wc);
      for (var i = 0; i < 3; i++) {
        _normals.Add(normal);
        _uvs.Add(_uv);
      }
      var list = _triangles[(int)_slot];
      list.Add(start);
      list.Add(start + 1);
      list.Add(start + 2);
    }

    public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward) {
      Tri(a, b, c, outward);
      Tri(a, c, d, outward);
    }

    /// <summary>윗면과 밑면 크기가 다른 육면체. 경사 장갑과 쐐기를 만든다. center는 밑면 가운데다.</summary>
    public void Taper(Vector3 center, Vector2 bottom, Vector2 top, float height, Vector2 topShift = default) {
      Vector3 Corner(Vector2 size, float y, float sx, float sz, Vector2 shift) =>
        center + new Vector3(sx * size.x / 2 + shift.x, y, sz * size.y / 2 + shift.y);
      var b = new[] { Corner(bottom, 0, -1, -1, default), Corner(bottom, 0, 1, -1, default), Corner(bottom, 0, 1, 1, default), Corner(bottom, 0, -1, 1, default) };
      var t = new[] { Corner(top, height, -1, -1, topShift), Corner(top, height, 1, -1, topShift), Corner(top, height, 1, 1, topShift), Corner(top, height, -1, 1, topShift) };
      var middle = center + new Vector3(topShift.x / 2, height / 2, topShift.y / 2);
      for (var i = 0; i < 4; i++) {
        var j = (i + 1) % 4;
        var face = (b[i] + b[j] + t[i] + t[j]) / 4;
        Quad(b[i], b[j], t[j], t[i], face - middle);
      }
      Quad(t[0], t[1], t[2], t[3], Vector3.up);
      Quad(b[0], b[1], b[2], b[3], Vector3.down);
    }

    /// <summary>center는 상자 가운데다.</summary>
    public void Box(Vector3 center, Vector3 size) {
      Taper(center - new Vector3(0, size.y / 2, 0), new Vector2(size.x, size.z), new Vector2(size.x, size.z), size.y);
    }

    /// <summary>모서리 깎은 상자. center는 가운데다.</summary>
    public void Chamfer(Vector3 center, Vector3 size, float cut) {
      Vector3[] Ring(float y, float inset) {
        var x = size.x / 2 - inset;
        var z = size.z / 2 - inset;
        var c = cut;
        return new[] {
          new Vector3(-x + c, y, -z), new Vector3(x - c, y, -z), new Vector3(x, y, -z + c), new Vector3(x, y, z - c),
          new Vector3(x - c, y, z), new Vector3(-x + c, y, z), new Vector3(-x, y, z - c), new Vector3(-x, y, -z + c),
        };
      }
      var h = size.y / 2;
      Push(center);
      Rings(new List<Vector3[]> { Ring(-h, cut), Ring(-h + cut, 0), Ring(h - cut, 0), Ring(h, cut) });
      Pop();
    }

    /// <summary>같은 꼭짓점 수의 고리를 위로 쌓아 옆면과 뚜껑을 만든다.</summary>
    public void Rings(List<Vector3[]> rings, bool capBottom = true, bool capTop = true) {
      for (var r = 0; r + 1 < rings.Count; r++) {
        var lower = rings[r];
        var upper = rings[r + 1];
        var axis = (Average(lower) + Average(upper)) / 2;
        for (var i = 0; i < lower.Length; i++) {
          var j = (i + 1) % lower.Length;
          var face = (lower[i] + lower[j] + upper[i] + upper[j]) / 4;
          var outward = face - axis;
          if ((lower[i] - lower[j]).sqrMagnitude < 1e-8f) {
            Tri(lower[i], upper[j], upper[i], outward);
          } else if ((upper[i] - upper[j]).sqrMagnitude < 1e-8f) {
            Tri(lower[i], lower[j], upper[i], outward);
          } else {
            Quad(lower[i], lower[j], upper[j], upper[i], outward);
          }
        }
      }
      if (capBottom) {
        Cap(rings[0], Vector3.down);
      }
      if (capTop) {
        Cap(rings[rings.Count - 1], Vector3.up);
      }
    }

    void Cap(Vector3[] ring, Vector3 outward) {
      if ((ring[0] - ring[1]).sqrMagnitude < 1e-8f) {
        return;
      }
      for (var i = 1; i + 1 < ring.Length; i++) {
        Tri(ring[0], ring[i], ring[i + 1], outward);
      }
    }

    static Vector3 Average(Vector3[] ring) {
      var sum = Vector3.zero;
      foreach (var point in ring) {
        sum += point;
      }
      return sum / ring.Length;
    }

    /// <summary>회전체. profile의 x는 반지름, y는 높이다. base는 축의 밑점이고 축은 위쪽이다.</summary>
    public void Lathe(Vector3 origin, Vector2[] profile, int sides, float turn = 0f) {
      var rings = new List<Vector3[]>();
      foreach (var point in profile) {
        var ring = new Vector3[sides];
        for (var i = 0; i < sides; i++) {
          var angle = (i + turn) * Mathf.PI * 2f / sides;
          ring[i] = origin + new Vector3(Mathf.Cos(angle) * point.x, point.y, Mathf.Sin(angle) * point.x);
        }
        rings.Add(ring);
      }
      Rings(rings);
    }

    public void Cylinder(Vector3 origin, float radius, float height, int sides) {
      Lathe(origin, new[] { new Vector2(radius, 0), new Vector2(radius, height) }, sides);
    }

    public void Frustum(Vector3 origin, float bottom, float top, float height, int sides) {
      Lathe(origin, new[] { new Vector2(bottom, 0), new Vector2(top, height) }, sides);
    }

    public void Cone(Vector3 origin, float radius, float height, int sides) {
      Lathe(origin, new[] { new Vector2(radius, 0), new Vector2(0, height) }, sides);
    }

    /// <summary>반구. height를 줄이면 납작한 돔이 된다.</summary>
    public void Dome(Vector3 origin, float radius, float height, int sides, int steps) {
      var profile = new Vector2[steps + 1];
      for (var i = 0; i <= steps; i++) {
        var angle = i * Mathf.PI / 2f / steps;
        profile[i] = new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * height);
      }
      profile[steps].x = 0f;
      Lathe(origin, profile, sides);
    }

    /// <summary>얇은 원판. 포구와 발사관 구멍에 쓴다. 축은 위쪽이다.</summary>
    public void Disc(Vector3 center, float radius, int sides) {
      var ring = new Vector3[sides];
      for (var i = 0; i < sides; i++) {
        var angle = i * Mathf.PI * 2f / sides;
        ring[i] = center + new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
      }
      Cap(ring, Vector3.up);
    }

    /// <summary>볼록 다각형을 두께만큼 세운 판. 점은 XZ다.</summary>
    public void Slab(Vector2[] points, float bottom, float top) {
      var lower = new Vector3[points.Length];
      var upper = new Vector3[points.Length];
      for (var i = 0; i < points.Length; i++) {
        lower[i] = new Vector3(points[i].x, bottom, points[i].y);
        upper[i] = new Vector3(points[i].x, top, points[i].y);
      }
      Rings(new List<Vector3[]> { lower, upper });
    }

    /// <summary>끝이 뾰족한 작은 비늘 판. 앞쪽이 들려 겹쳐 보인다.</summary>
    public void Plate(Vector3 center, float width, float length, float lift) {
      var w = width / 2;
      var l = length / 2;
      var rear = center + new Vector3(0, 0, -l);
      var left = center + new Vector3(-w, lift * 0.5f, l * 0.2f);
      var right = center + new Vector3(w, lift * 0.5f, l * 0.2f);
      var tip = center + new Vector3(0, lift, l);
      var backLeft = center + new Vector3(-w, 0, -l);
      var backRight = center + new Vector3(w, 0, -l);
      Quad(backLeft, backRight, right, left, Vector3.up);
      Tri(left, right, tip, Vector3.up);
      Tri(left, tip, tip + new Vector3(0, -lift, 0), new Vector3(-1, 0, 1));
      Tri(right, tip, tip + new Vector3(0, -lift, 0), new Vector3(1, 0, 1));
    }

    /// <summary>찌그러진 저면수 바위. 같은 시드면 같은 모양이다.</summary>
    public void Rock(Vector3 origin, float radius, float height, int seed) {
      const int Sides = 6;
      float[] levels = { 0f, 0.35f, 0.72f, 1f };
      float[] widths = { 0.86f, 1f, 0.72f, 0.28f };
      var rings = new List<Vector3[]>();
      for (var r = 0; r < levels.Length; r++) {
        var ring = new Vector3[Sides];
        for (var i = 0; i < Sides; i++) {
          var angle = (i + Hash(seed, r) * 0.5f) * Mathf.PI * 2f / Sides;
          var size = radius * widths[r] * (0.82f + 0.36f * Hash(seed, r * 16 + i + 3));
          var y = height * (levels[r] + (r > 0 ? 0.08f * (Hash(seed, r * 32 + i + 7) - 0.5f) : 0f));
          ring[i] = origin + new Vector3(Mathf.Cos(angle) * size, y, Mathf.Sin(angle) * size);
        }
        rings.Add(ring);
      }
      Rings(rings, false);
    }

    /// <summary>0~1 값을 주는 정수 해시. 기기마다 같아야 해서 난수 생성기를 쓰지 않는다.</summary>
    public static float Hash(int seed, int index) {
      unchecked {
        var h = (uint)seed * 374761393u + (uint)index * 668265263u;
        h = (h ^ (h >> 13)) * 1274126177u;
        h ^= h >> 16;
        return (h & 0xFFFFFF) / (float)0x1000000;
      }
    }

    /// <summary>쓰인 칸만 서브메쉬로 만든다. slots에 서브메쉬 순서대로 칸을 돌려준다.</summary>
    public Mesh ToMesh(string name, List<ArtSlot> slots) {
      var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt16 };
      if (_vertices.Count > 65000) {
        mesh.indexFormat = IndexFormat.UInt32;
      }
      mesh.SetVertices(_vertices);
      mesh.SetNormals(_normals);
      mesh.SetUVs(0, _uvs);
      slots.Clear();
      for (var i = 0; i < _triangles.Length; i++) {
        if (_triangles[i].Count > 0) {
          slots.Add((ArtSlot)i);
        }
      }
      mesh.subMeshCount = slots.Count;
      for (var i = 0; i < slots.Count; i++) {
        mesh.SetTriangles(_triangles[(int)slots[i]], i);
      }
      mesh.RecalculateBounds();
      return mesh;
    }
  }

  /// <summary>나무와 숲. 채집량이 바뀔 때 실행 중에도 다시 합치므로 View에 둔다.</summary>
  public static class ForestKit {
    public enum TreeState { Full, Sparse, Stump }

    public static void Tree(MeshKit kit, Vector3 origin, int seed, TreeState state) {
      var size = 0.85f + 0.4f * MeshKit.Hash(seed, 1);
      var conifer = MeshKit.Hash(seed, 2) < 0.45f;
      kit.Paint(Swatch.Bark);
      if (state == TreeState.Stump) {
        kit.Frustum(origin, 0.3f * size, 0.24f * size, 0.45f, 5);
        return;
      }
      kit.Frustum(origin, 0.24f * size, 0.14f * size, 1.8f * size, 5);
      var leaf = MeshKit.Hash(seed, 3);
      kit.Paint(leaf < 0.33f ? Swatch.LeafDark : leaf < 0.72f ? Swatch.Leaf : Swatch.LeafLight);
      var tiers = state == TreeState.Sparse ? 1 : 3;
      for (var i = 0; i < tiers; i++) {
        var y = (1.0f + i * 0.85f) * size;
        var bottom = (1.25f - i * 0.28f) * size * (state == TreeState.Sparse ? 0.55f : 1f);
        var top = conifer ? bottom * 0.3f : bottom * 0.72f;
        kit.Frustum(origin + new Vector3(0, y, 0), bottom, top, 1.0f * size, 5);
      }
    }

    /// <summary>
    /// 사각형 숲. remaining은 남은 매장량 비율이다. 바깥 나무부터 앙상한 나무나 그루터기로 바뀌고 숲 모양은 그대로다.
    /// 앙상한 나무를 많이 남겨 통과할 수 있는 빈틈처럼 보이지 않게 한다.
    /// </summary>
    public static void Forest(MeshKit kit, float halfLength, float halfWidth, int seed, float remaining) {
      kit.Paint(Swatch.Grass);
      kit.Box(new Vector3(0, 0.08f, 0), new Vector3(halfLength * 2, 0.16f, halfWidth * 2));
      kit.Paint(Swatch.Earth);
      var spots = new List<(Vector3 Position, float Edge, int Id)>();
      const float Spacing = 1.9f;
      var id = 0;
      for (var x = -halfLength + Spacing / 2; x < halfLength; x += Spacing) {
        for (var z = -halfWidth + Spacing / 2; z < halfWidth; z += Spacing) {
          var jitter = new Vector3(MeshKit.Hash(seed, id * 2) - 0.5f, 0, MeshKit.Hash(seed, id * 2 + 1) - 0.5f) * 0.7f;
          var edge = Mathf.Min(halfLength - Mathf.Abs(x), (halfWidth - Mathf.Abs(z)) * 2f);
          spots.Add((new Vector3(x, 0.16f, z) + jitter, edge, id));
          id++;
        }
      }
      spots.Sort((a, b) => a.Edge != b.Edge ? a.Edge.CompareTo(b.Edge) : a.Id.CompareTo(b.Id));
      var cut = Mathf.RoundToInt(spots.Count * (1f - Mathf.Clamp01(remaining)));
      for (var i = 0; i < spots.Count; i++) {
        var state = i >= cut ? TreeState.Full : MeshKit.Hash(seed, spots[i].Id + 500) < 0.3f ? TreeState.Stump : TreeState.Sparse;
        Tree(kit, spots[i].Position, seed * 131 + spots[i].Id, state);
      }
    }
  }
}
