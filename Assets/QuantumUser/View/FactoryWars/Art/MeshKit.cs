namespace FactoryWars.View {
  using System.Collections.Generic;
  using UnityEngine;
  using UnityEngine.Rendering;

  /// <summary>팔레트 텍스처의 칸. 색, 금속감, 발광, 팀 색이 모두 칸으로 정해져서 메쉬는 서브메쉬 하나다.</summary>
  public enum Swatch {
    Metal, MetalDark, MetalLight, Armor, ArmorDark, Olive, OliveDark, Sand, Earth, Rubber, Glass, Black,
    Wood, WoodDark, Bark, LeafDark, Leaf, LeafLight, Grass, Rock, RockDark, Ore, OreDark, OreLight, Stone, Water, White,
    GlowCyan, GlowOrange, GlowCrystal, Hazard, Team,
    Dirt, DirtDark, GrassDark, CliffDark, CliffMid, CliffLight, CanyonDark, CanyonMid, CanyonLight,
    Boulder, BoulderDark, BoulderLight, WaterDeep, StripeYellow, GlowRed, LampDark,
    Concrete, ConcreteDark, Window,
    Asphalt, LaneYellow, LaneWhite, RockFace, RockFaceDark, RockFaceLight, LeafAutumn, Brick, Outline, GrassLight,
    ForestFloor,
  }

  public static class ArtPalette {
    public const int Size = 64;
    const float GlowBase = 0.3f;
    const float MoltenOutage = 0.2f;

    // 빛의 색은 바탕색보다 채도가 높다. 세기를 곱했을 때 한 채널만 넘쳐 주황이 노랑으로, 청록이 흰색으로 변하지 않게 한다.
    static readonly Color32 ElectricLight = new Color32(0x20, 0xD8, 0xD8, 0xFF);
    static readonly Color32 MoltenLight = new Color32(0xFF, 0x34, 0x00, 0xFF);
    static readonly Color32 CrystalLight = new Color32(0x30, 0xB4, 0xC4, 0xFF);
    static readonly Color32 WarningLight = new Color32(0xFF, 0x14, 0x0C, 0xFF);

    readonly struct Cell {
      public readonly Color32 Color;
      public readonly float Metallic;
      public readonly float Smoothness;

      public Cell(int rgb, float metallic, float smoothness) {
        Color = new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
        Metallic = metallic;
        Smoothness = smoothness;
      }
    }

    // Swatch 순서와 같다. 금속 파츠는 매끈하고 흙·고무·나무는 무광이라 재질 대비가 난다.
    static readonly Cell[] Cells = {
      new Cell(0x8E9092, 0.56f, 0.56f), new Cell(0x585A5E, 0.5f, 0.46f), new Cell(0xB4B2AC, 0.62f, 0.6f),
      new Cell(0x7C766A, 0.4f, 0.46f), new Cell(0x56524A, 0.35f, 0.38f), new Cell(0x6E6A50, 0.04f, 0.2f), new Cell(0x4C4C3A, 0.04f, 0.16f),
      new Cell(0xA89878, 0f, 0.12f), new Cell(0x7A5C3E, 0f, 0.06f), new Cell(0x262628, 0f, 0.1f), new Cell(0x303C46, 0.2f, 0.92f), new Cell(0x141416, 0f, 0.18f),
      new Cell(0x745A42, 0f, 0.12f), new Cell(0x4A382A, 0f, 0.1f), new Cell(0x4E3424, 0f, 0.06f),
      new Cell(0x2C5A2A, 0f, 0.1f), new Cell(0x467E30, 0f, 0.1f), new Cell(0x6A9A3A, 0f, 0.1f), new Cell(0x5C7038, 0f, 0.06f),
      new Cell(0x7C746A, 0f, 0.12f), new Cell(0x4A3E36, 0f, 0.1f),
      new Cell(0x4E5056, 0.3f, 0.36f), new Cell(0x3C3E44, 0.3f, 0.3f), new Cell(0x666970, 0.3f, 0.42f),
      new Cell(0x9C9C96, 0f, 0.16f), new Cell(0x1C3040, 0.1f, 0.8f), new Cell(0xE8E6E0, 0f, 0.3f),
      new Cell(0x40E8E0, 0f, 0.5f), new Cell(0xFF7A14, 0f, 0.5f), new Cell(0xA8F0E8, 0f, 0.7f),
      new Cell(0xC89A2C, 0.1f, 0.4f), new Cell(0x808080, 0.2f, 0.56f),
      new Cell(0x98835E, 0f, 0.06f), new Cell(0x857150, 0f, 0.06f), new Cell(0x526632, 0f, 0.06f),
      new Cell(0x54392A, 0f, 0.08f), new Cell(0x7A5638, 0f, 0.08f), new Cell(0x9E7A4C, 0f, 0.08f),
      new Cell(0x33261F, 0f, 0.1f), new Cell(0x4A372C, 0f, 0.1f), new Cell(0x63493A, 0f, 0.1f),
      new Cell(0x7A7670, 0f, 0.14f), new Cell(0x5C5954, 0f, 0.12f), new Cell(0x98948C, 0f, 0.16f),
      new Cell(0x16303E, 0.1f, 0.86f), new Cell(0xF2C40F, 0f, 0.24f), new Cell(0xE02018, 0f, 0.5f), new Cell(0x4A1210, 0.2f, 0.5f),
      new Cell(0xA8A39A, 0f, 0.12f), new Cell(0x5E5B57, 0f, 0.1f), new Cell(0x2E3E4C, 0.3f, 0.7f),
      new Cell(0x3C3D40, 0f, 0.18f), new Cell(0xE0B23A, 0f, 0.2f), new Cell(0xE6E2D6, 0f, 0.2f),
      new Cell(0x8A7F70, 0f, 0.08f), new Cell(0x5E554B, 0f, 0.06f), new Cell(0xA89C88, 0f, 0.1f),
      new Cell(0xC8662A, 0f, 0.1f), new Cell(0x8E4A36, 0f, 0.06f), new Cell(0x1E1A16, 0f, 0f), new Cell(0x7A8C52, 0f, 0.06f),
      new Cell(0x384530, 0f, 0.04f),
    };

    public static int CellCount => Cells.Length;

    /// <summary>항목 36: 시가지에서는 잔디 칸을 콘크리트·아스팔트 칸으로 바꾼다(숲 바닥판, 통과 불가 대지 윗면).</summary>
    public static Swatch Turf(Swatch swatch) {
      if (VisualProfile.Current.UrbanDecor == false) {
        return swatch;
      }
      return swatch == Swatch.Grass || swatch == Swatch.GrassLight ? Swatch.ConcreteDark : swatch == Swatch.GrassDark ? Swatch.Asphalt : swatch;
    }

    /// <summary>칸의 기본색(팀·발광 보정 없음).</summary>
    public static Color ColorOf(Swatch swatch) => Cells[(int)swatch].Color;

    /// <summary>밝기 단계 줄 수. 홀수라 가운데 줄 중심이 정확히 V 0.5(원래 색)다.</summary>
    public const int Rows = 17;
    public const float DarkestShade = 0.55f;
    public const float BrightestShade = 1.15f;

    /// <summary>V 좌표 → 밝기 배율. 아래 절반은 어둡게, 위 절반은 밝게.</summary>
    public static float ShadeAt(float v) => v < 0.5f ? Mathf.Lerp(DarkestShade, 1f, v / 0.5f) : Mathf.Lerp(1f, BrightestShade, (v - 0.5f) / 0.5f);

    /// <summary>밝기 배율 → V 좌표. 끝 줄의 중심을 넘지 않는다.</summary>
    public static float RowFor(float shade) {
      shade = Mathf.Clamp(shade, DarkestShade, BrightestShade);
      var v = shade < 1f ? 0.5f * (shade - DarkestShade) / (1f - DarkestShade) : 0.5f + 0.5f * (shade - 1f) / (BrightestShade - 1f);
      return Mathf.Clamp(v, 0.5f / Rows, 1f - 0.5f / Rows);
    }

    /// <summary>한 줄짜리 칸 색을 밝기 단계 줄로 늘린다. shade가 false면(발광·금속 맵) 줄마다 같다. 발광 칸은 어둡게 하지 않는다.</summary>
    public static Color32[] Expand(Color32[] row, bool shade) {
      var pixels = new Color32[Size * Rows];
      for (var r = 0; r < Rows; r++) {
        var scale = shade ? ShadeAt((r + 0.5f) / Rows) : 1f;
        for (var i = 0; i < Size; i++) {
          var c = row[i];
          var s = shade && i < Cells.Length && IsGlow((Swatch)i) == false ? scale : 1f;
          pixels[r * Size + i] = new Color32((byte)Mathf.Min(255, c.r * s), (byte)Mathf.Min(255, c.g * s), (byte)Mathf.Min(255, c.b * s), c.a);
        }
      }
      return pixels;
    }

    public static Vector2 Uv(Swatch swatch) => new Vector2(((int)swatch + 0.5f) / Size, 0.5f);

    public static bool IsGlow(Swatch swatch) => swatch == Swatch.GlowCyan || swatch == Swatch.GlowOrange || swatch == Swatch.GlowCrystal || swatch == Swatch.GlowRed;

    static Color32 Scale(Color32 color, float scale) => new Color32((byte)(color.r * scale), (byte)(color.g * scale), (byte)(color.b * scale), 255);

    /// <summary>플레이어마다 팀 칸만 다르다. 발광 칸의 바탕색은 어둡고 빛은 발광 맵이 낸다. 그래서 정전에는 맵만 바꾸면 된다.</summary>
    public static Color32[] BasePixels(Color32 team) {
      var pixels = new Color32[Size];
      for (var i = 0; i < Cells.Length; i++) {
        var swatch = (Swatch)i;
        pixels[i] = swatch == Swatch.Team ? team : IsGlow(swatch) ? Scale(Cells[i].Color, GlowBase) : Adjust(swatch, Cells[i].Color);
      }
      return pixels;
    }

    static readonly Swatch[] Lifted = { Swatch.Metal, Swatch.MetalDark, Swatch.MetalLight, Swatch.Armor, Swatch.ArmorDark, Swatch.Olive, Swatch.OliveDark, Swatch.Rubber, Swatch.Black };

    /// <summary>
    /// 프로필에 따른 칸 색 보정(항목 27 K 교정): 100% Kenney 파스텔 장난감 디오라마 팔레트.
    /// 유닛·사령부·건물은 깨끗한 화이트/크림(#EDF0F7) 베이스와 부드러운 음영, 민트 잔디(#7BE5BA), 파스텔 암벽(#9AA5B8).
    /// </summary>
    static Color32 Adjust(Swatch swatch, Color32 color) {
      var profile = VisualProfile.Current;
      if (profile.SolidGround) {
        switch (swatch) {
          case Swatch.Metal:
          case Swatch.Armor:
          case Swatch.Olive:
          case Swatch.OliveDark:
          case Swatch.Bark:
            return new Color32(0xED, 0xF0, 0xF7, 0xFF);
          case Swatch.MetalDark:
          case Swatch.ArmorDark:
            return new Color32(0xC2, 0xCB, 0xD9, 0xFF);
          case Swatch.MetalLight:
            return new Color32(0xFF, 0xFF, 0xFF, 0xFF);
          case Swatch.Rubber:
          case Swatch.Black:
            return new Color32(0x56, 0x60, 0x72, 0xFF);
          case Swatch.Glass:
            return new Color32(0x8C, 0xE8, 0xFF, 0xFF);
          case Swatch.Wood:
          case Swatch.WoodDark:
            return new Color32(0xED, 0xF0, 0xF7, 0xFF);
          case Swatch.Stone:
            return new Color32(0x9A, 0xA5, 0xB8, 0xFF);
          case Swatch.Concrete:
            return new Color32(0xED, 0xF0, 0xF7, 0xFF);
          case Swatch.ConcreteDark:
            return new Color32(0xC2, 0xCB, 0xD9, 0xFF);
          case Swatch.Grass:
          case Swatch.GrassLight:
          case Swatch.GrassDark:
            return new Color32(0x7B, 0xE5, 0xBA, 0xFF);
          case Swatch.ForestFloor:
            return new Color32(0x6A, 0xD4, 0xA8, 0xFF);
          case Swatch.RockFace:
          case Swatch.RockFaceDark:
          case Swatch.RockFaceLight:
            return new Color32(0x9A, 0xA5, 0xB8, 0xFF);
          case Swatch.GlowRed:
            return new Color32(0xFF, 0x6B, 0x6B, 0xFF);
        }
      } else if (profile.PaletteLift > 0f && System.Array.IndexOf(Lifted, swatch) >= 0) {
        var c = (Color)color;
        var lifted = Color.Lerp(c, new Color(0.93f, 0.92f, 0.9f), profile.PaletteLift);
        return (Color32)lifted;
      }
      if (profile.WaterColor.a > 0f && (swatch == Swatch.Water || swatch == Swatch.WaterDeep)) {
        return (Color32)(swatch == Swatch.WaterDeep ? profile.WaterColor * 0.82f : profile.WaterColor);
      }
      return color;
    }

    /// <summary>발광 칸 외에는 검정이다. 정전이면 전기는 꺼지고 쇳물은 어두워진다. 광석 결정은 전기와 무관하다.</summary>
    public static Color32[] EmissionPixels(bool powered) {
      var pixels = new Color32[Size];
      for (var i = 0; i < Size; i++) {
        var swatch = (Swatch)i;
        var color = new Color32(0, 0, 0, 255);
        if (swatch == Swatch.GlowRed) {
          color = WarningLight;
        } else if (swatch == Swatch.GlowCrystal) {
          color = CrystalLight;
        } else if (swatch == Swatch.GlowCyan && powered) {
          color = ElectricLight;
        } else if (swatch == Swatch.GlowOrange) {
          color = powered ? MoltenLight : Scale(MoltenLight, MoltenOutage);
        }
        pixels[i] = color;
      }
      return pixels;
    }

    /// <summary>URP Lit의 금속 맵 규칙: R이 금속, A가 매끈함이다.</summary>
    public static Color32[] SurfacePixels() {
      var pixels = new Color32[Size];
      for (var i = 0; i < Cells.Length; i++) {
        var metallic = (byte)(Cells[i].Metallic * 255f);
        pixels[i] = new Color32(metallic, metallic, metallic, (byte)(Cells[i].Smoothness * 255f));
      }
      return pixels;
    }

    public static float Smoothness(Swatch swatch) => Cells[(int)swatch].Smoothness;
    public static float Metallic(Swatch swatch) => Cells[(int)swatch].Metallic;
  }

  /// <summary>
  /// 저면수 메쉬를 코드로 조립한다. 면마다 꼭짓점을 따로 두어 평면 셰이딩이 된다.
  /// 시드는 정수 해시로 풀어서 모든 기기에서 같은 모양이 나온다.
  /// </summary>
  public class MeshKit {
    readonly List<Vector3> _vertices = new List<Vector3>();
    readonly List<Vector3> _normals = new List<Vector3>();
    readonly List<Vector2> _uvs = new List<Vector2>();
    readonly List<int> _triangles = new List<int>();
    readonly Stack<Matrix4x4> _stack = new Stack<Matrix4x4>();
    Matrix4x4 _matrix = Matrix4x4.identity;
    Vector2 _uv = ArtPalette.Uv(Swatch.Metal);
    Swatch[] _shades;
    int _shadeSeed;
    int _shadeIndex;

    public int TriangleCount => _triangles.Count / 3;

    public MeshKit Paint(Swatch swatch) {
      _uv = ArtPalette.Uv(swatch);
      return this;
    }

    public MeshKit Team() => Paint(Swatch.Team);

    public MeshKit Glow(Swatch swatch = Swatch.GlowCyan) => Paint(swatch);

    /// <summary>이후 면마다 주어진 칸 중 하나를 고른다. 같은 시드면 같은 무늬다.</summary>
    public void ShadeFaces(int seed, params Swatch[] shades) {
      _shades = shades;
      _shadeSeed = seed;
      _shadeIndex = 0;
    }

    public void EndShade() {
      _shades = null;
    }

    void NextShade() {
      if (_shades != null) {
        _uv = ArtPalette.Uv(_shades[(int)(Hash(_shadeSeed, _shadeIndex++) * _shades.Length) % _shades.Length]);
      }
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
      NextShade();
      Face(a, b, c, outward);
    }

    void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 outward) {
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
      _triangles.Add(start);
      _triangles.Add(start + 1);
      _triangles.Add(start + 2);
    }

    public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward) {
      NextShade();
      Face(a, b, c, outward);
      Face(a, c, d, outward);
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

    /// <summary>XY 볼록 단면을 Z축으로 민 기둥. 아치 지붕과 문틀에 쓴다.</summary>
    public void Prism(Vector2[] profile, float back, float front) {
      var center = Vector2.zero;
      foreach (var point in profile) {
        center += point;
      }
      center /= profile.Length;
      Vector3 Back(Vector2 p) => new Vector3(p.x, p.y, back);
      Vector3 Front(Vector2 p) => new Vector3(p.x, p.y, front);
      for (var i = 0; i < profile.Length; i++) {
        var a = profile[i];
        var b = profile[(i + 1) % profile.Length];
        var outward = (a + b) / 2 - center;
        Quad(Back(a), Back(b), Front(b), Front(a), new Vector3(outward.x, outward.y, 0));
      }
      for (var i = 1; i + 1 < profile.Length; i++) {
        Tri(Front(profile[0]), Front(profile[i]), Front(profile[i + 1]), Vector3.forward);
        Tri(Back(profile[0]), Back(profile[i]), Back(profile[i + 1]), Vector3.back);
      }
    }

    /// <summary>두 점을 잇는 각재. 철탑과 트러스에 쓴다.</summary>
    public void Beam(Vector3 from, Vector3 to, float size) {
      var direction = to - from;
      var up = Mathf.Abs(direction.normalized.y) > 0.99f ? Vector3.forward : Vector3.up;
      Push(from, Quaternion.LookRotation(direction, up).eulerAngles);
      Box(new Vector3(0, 0, direction.magnitude / 2), new Vector3(size, size, direction.magnitude));
      Pop();
    }

    /// <summary>바닥에 눕힌 고리. 선택 링에 쓴다.</summary>
    public void Annulus(Vector3 center, float inner, float outer, int sides) {
      for (var i = 0; i < sides; i++) {
        var a = i * Mathf.PI * 2f / sides;
        var b = (i + 1) * Mathf.PI * 2f / sides;
        var from = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
        var to = new Vector3(Mathf.Cos(b), 0, Mathf.Sin(b));
        Quad(center + from * inner, center + from * outer, center + to * outer, center + to * inner, Vector3.up);
      }
    }

    /// <summary>
    /// 회전체 곡면을 따라 +Z 쪽에 붙는 갈빗대. profile의 x는 반지름, y는 높이고 아래에서 위로 간다.
    /// sink만큼 곡면 안으로 파고들어 다각형 면과 뜨지 않는다.
    /// </summary>
    public void Rib(Vector2[] profile, float width, float proud, float sink) {
      var normals = new Vector2[profile.Length];
      for (var i = 0; i < profile.Length; i++) {
        var before = profile[Mathf.Max(i - 1, 0)];
        var after = profile[Mathf.Min(i + 1, profile.Length - 1)];
        var direction = (after - before).normalized;
        normals[i] = new Vector2(direction.y, -direction.x);
      }
      Vector3 Point(int i, float x, float raise) {
        var p = profile[i] + normals[i] * raise;
        return new Vector3(x, p.y, p.x);
      }
      var half = width / 2;
      for (var i = 0; i + 1 < profile.Length; i++) {
        var outward = new Vector3(0, normals[i].y, normals[i].x);
        Quad(Point(i, -half, proud), Point(i, half, proud), Point(i + 1, half, proud), Point(i + 1, -half, proud), outward);
        Quad(Point(i, -half, -sink), Point(i, -half, proud), Point(i + 1, -half, proud), Point(i + 1, -half, -sink), Vector3.left);
        Quad(Point(i, half, -sink), Point(i, half, proud), Point(i + 1, half, proud), Point(i + 1, half, -sink), Vector3.right);
      }
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

    /// <summary>서브메쉬는 하나다. 색은 UV로 칸을 고른다.</summary>
    public enum ShadeMode { None, Object, Ground }

    /// <summary>ToMesh에서 정점 밝기를 굽는 방식. 프로필의 VertexShade가 꺼져 있으면 아무것도 하지 않는다.</summary>
    public ShadeMode Shading = ShadeMode.None;

    /// <summary>
    /// 물체: 아래→위 그라데이션, 바닥 접촉부 AO, 아래를 향한 면 어둡게.
    /// 바닥: 월드 좌표 낮은 주파수 노이즈, 높은 층일수록 조금 밝게, 강바닥 쪽은 어둡게.
    /// </summary>
    void BakeShade() {
      var profile = VisualProfile.Current;
      if (Shading == ShadeMode.None || profile.VertexShade == false || _vertices.Count == 0) {
        return;
      }
      var min = float.MaxValue;
      var max = float.MinValue;
      foreach (var v in _vertices) {
        min = Mathf.Min(min, v.y);
        max = Mathf.Max(max, v.y);
      }
      var span = Mathf.Max(0.01f, max - min);
      for (var i = 0; i < _vertices.Count; i++) {
        var uv = _uvs[i];
        var cell = Mathf.FloorToInt(uv.x * ArtPalette.Size);
        if (cell >= 0 && cell < ArtPalette.CellCount && ArtPalette.IsGlow((Swatch)cell)) {
          continue;
        }
        var p = _vertices[i];
        var shade = 1f;
        if (Shading == ShadeMode.Object) {
          shade += profile.ShadeGradient * ((p.y - min) / span - 0.5f);
          var lift = p.y - min;
          if (lift < profile.ShadeAoHeight) {
            shade -= profile.ShadeAo * (1f - lift / profile.ShadeAoHeight);
          }
          if (_normals[i].y < -0.5f) {
            shade -= 0.1f;
          }
        } else {
          var noise = Mathf.PerlinNoise(p.x * 0.031f + 17.3f, p.z * 0.031f + 5.1f) * 0.7f + Mathf.PerlinNoise(p.x * 0.11f + 3.7f, p.z * 0.11f + 41f) * 0.3f;
          shade += profile.ShadeNoise * (noise * 2f - 1f);
          shade += 0.035f * Mathf.Clamp(p.y, 0f, 8f);
          if (p.y < -0.2f) {
            shade -= 0.06f * Mathf.Min(-p.y, 4f);
          }
        }
        _uvs[i] = new Vector2(uv.x, ArtPalette.RowFor(shade));
      }
    }

    public Mesh ToMesh(string name) {
      BakeShade();
      var mesh = new Mesh { name = name, indexFormat = _vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
      mesh.SetVertices(_vertices);
      mesh.SetNormals(_normals);
      mesh.SetUVs(0, _uvs);
      mesh.SetTriangles(_triangles, 0);
      mesh.RecalculateBounds();
      return mesh;
    }
  }

  /// <summary>나무와 숲. 채집량이 바뀔 때 실행 중에도 다시 합치므로 View에 둔다.</summary>
  public static class ForestKit {
    public enum TreeState { Full, Sparse, Stump }

    public static void Tree(MeshKit kit, Vector3 origin, int seed, TreeState state) {
      var profile = VisualProfile.Current;
      var size = profile.LeafSet == 0 ? 0.85f + 0.4f * MeshKit.Hash(seed, 1) : 0.8f + 0.5f * MeshKit.Hash(seed, 1);
      var conifer = MeshKit.Hash(seed, 2) < profile.ConiferRatio;
      kit.Paint(Swatch.Bark);
      if (state == TreeState.Stump) {
        kit.Frustum(origin, 0.3f * size, 0.24f * size, 0.45f, 5);
        return;
      }
      kit.Frustum(origin, 0.24f * size, 0.14f * size, 1.8f * size, 5);
      var leaf = MeshKit.Hash(seed, 3);
      if (profile.LeafSet == 0) {
        kit.Paint(leaf < 0.33f ? Swatch.LeafDark : leaf < 0.72f ? Swatch.Leaf : Swatch.LeafLight);
      } else {
        // 침엽수 위주에 단풍 포인트를 섞는다.
        kit.Paint(leaf < 0.42f ? Swatch.LeafDark : leaf < 0.78f ? Swatch.Leaf : leaf < 0.9f ? Swatch.LeafAutumn : Swatch.LeafLight);
      }
      if (conifer == false && profile.LeafSet != 0 && state == TreeState.Full) {
        // 둥근 활엽수: 줄기 위에 겹친 잎 덩어리 둘.
        kit.Frustum(origin + new Vector3(0, 1.3f * size, 0), 0.9f * size, 1.15f * size, 0.9f * size, 6);
        kit.Frustum(origin + new Vector3(0, 2.2f * size, 0), 1.15f * size, 0.45f * size, 0.9f * size, 6);
        return;
      }
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
    /// <summary>
    /// Kenney 나무로 한 그루를 놓는다(모으는 중일 때만). 침엽·단풍·짙은 활엽 비율은 프로필 값이다. 놓았으면 true.
    /// </summary>
    public static bool DressTree(Vector3 world, int seed, TreeState state) {
      if (Dressing.Collecting == false || Dressing.Has(DressKind.Conifer) == false) {
        return false;
      }
      var scar = BattleScars.Active != null ? BattleScars.Active.Level(world) : 0f;
      if (scar >= 0.7f) {
        // 부러진 나무: 그루터기·통나무를 그을린 색으로(항목 27 K-2). 채집 그루터기와 같은 모델을 쓰되 어둡다.
        return Dressing.Add(DressKind.Stump, world, seed, new Color(0.38f, 0.34f, 0.3f, 1f));
      }
      if (state == TreeState.Stump) {
        return Dressing.Add(DressKind.Stump, world, seed);
      }
      if (scar >= 0.4f) {
        var burnt = new Color(0.42f, 0.38f, 0.33f, 1f);
        return Dressing.Add(DressKind.TreeSparse, world, seed, burnt) || Dressing.Add(DressKind.Conifer, world, seed, burnt);
      }
      if (state == TreeState.Sparse) {
        return Dressing.Add(DressKind.TreeSparse, world, seed);
      }
      var profile = VisualProfile.Current;
      var pick = MeshKit.Hash(seed, 2);
      var kind = pick < profile.ConiferRatio ? DressKind.Conifer : pick < profile.ConiferRatio + profile.AutumnRatio ? DressKind.Autumn : DressKind.Broadleaf;
      return Dressing.Add(kind, world, seed) || Dressing.Add(DressKind.Conifer, world, seed);
    }

    /// <summary>Kenney 나무를 쓸 때 숲 바닥은 부엽토색이고 나무 간격이 좁다(항목 19C: 잎이 숲 면적의 85% 이상을 덮게).</summary>
    static bool Dense(Matrix4x4? toWorld) => toWorld.HasValue && Dressing.Collecting && Dressing.Has(DressKind.Conifer);

    public static void Forest(MeshKit kit, float halfLength, float halfWidth, int seed, float remaining, Matrix4x4? toWorld = null) {
      if (UrbanYard(kit, halfLength, halfWidth, seed, remaining, toWorld)) {
        return;
      }
      if (Dense(toWorld)) {
        kit.Paint(ArtPalette.Turf(Swatch.GrassDark)).Chamfer(new Vector3(0, 0.05f, 0), new Vector3(halfLength * 2, 0.1f, halfWidth * 2), 1.4f);
        kit.Paint(Swatch.ForestFloor).Chamfer(new Vector3(0, 0.09f, 0), new Vector3(Mathf.Max(2f, (halfLength - 2.2f) * 2), 0.16f, Mathf.Max(2f, (halfWidth - 2.2f) * 2)), 1.4f);
      } else {
        kit.Paint(ArtPalette.Turf(Swatch.Grass)).Box(new Vector3(0, 0.08f, 0), new Vector3(halfLength * 2, 0.16f, halfWidth * 2));
      }
      kit.Paint(Swatch.Earth);
      var spots = new List<(Vector3 Position, float Edge, int Id)>();
      var spacing = Dense(toWorld) ? 1.65f : 1.9f;
      var id = 0;
      for (var x = -halfLength + spacing / 2; x < halfLength; x += spacing) {
        for (var z = -halfWidth + spacing / 2; z < halfWidth; z += spacing) {
          var jitter = new Vector3(MeshKit.Hash(seed, id * 2) - 0.5f, 0, MeshKit.Hash(seed, id * 2 + 1) - 0.5f) * spacing * 0.4f;
          var edge = Mathf.Min(halfLength - Mathf.Abs(x), (halfWidth - Mathf.Abs(z)) * 2f);
          spots.Add((new Vector3(x, 0.16f, z) + jitter, edge, id));
          id++;
        }
      }
      Plant(kit, spots, seed, remaining, toWorld);
    }

    /// <summary>원형 군락. 나무 배치만 다르고 바깥부터 벌목되는 규칙은 같다.</summary>
    public static void Grove(MeshKit kit, float radius, int seed, float remaining, Matrix4x4? toWorld = null) {
      if (UrbanYard(kit, radius * 0.9f, radius * 0.9f, seed, remaining, toWorld)) {
        return;
      }
      var dense = Dense(toWorld);
      if (dense) {
        kit.Paint(ArtPalette.Turf(Swatch.GrassDark)).Cylinder(new Vector3(0, 0, 0), radius, 0.1f, 20);
        kit.Paint(Swatch.ForestFloor).Cylinder(new Vector3(0, 0.04f, 0), Mathf.Max(1.5f, radius - 2.2f), 0.14f, 20);
      } else {
        kit.Paint(ArtPalette.Turf(Swatch.Grass)).Cylinder(new Vector3(0, 0, 0), radius, 0.16f, 12);
      }
      kit.Paint(Swatch.Earth);
      var spots = new List<(Vector3 Position, float Edge, int Id)>();
      var spacing = dense ? 1.75f : 2.3f;
      var id = 0;
      for (var x = -radius + spacing / 2; x < radius; x += spacing) {
        for (var z = -radius + spacing / 2; z < radius; z += spacing) {
          var jitter = new Vector3(MeshKit.Hash(seed, id * 2) - 0.5f, 0, MeshKit.Hash(seed, id * 2 + 1) - 0.5f) * spacing * 0.4f;
          var edge = radius - new Vector2(x, z).magnitude;
          if (edge > 0.4f) {
            spots.Add((new Vector3(x, 0.16f, z) + jitter, edge, id));
          }
          id++;
        }
      }
      Plant(kit, spots, seed, remaining, toWorld);
    }

    /// <summary>toWorld가 있고 Kenney 나무가 있으면 모델로 놓고, 없으면 절차 나무를 kit에 굽는다. 가장자리에는 덤불·통나무·작은 바위를 섞는다.</summary>
    /// <summary>
    /// 시가지(항목 36 시각 교정): 목재 자원지를 상자·드럼통 수십 개 더미로 그리지 않고, 보도블록 패드(높이 0.15m) 위에
    /// 창고 건물 1채만 반듯하게 놓는다. 채집이 끝나면(남은 양 0) 창고가 사라지고 빈 패드만 남는다. 판정은 그대로(화면 전용).
    /// </summary>
    static bool UrbanYard(MeshKit kit, float halfLength, float halfWidth, int seed, float remaining, Matrix4x4? toWorld) {
      if (VisualProfile.Current.UrbanDecor == false || toWorld.HasValue == false || Dressing.Has(DressKind.LumberYard) == false) {
        return false;
      }
      kit.Paint(Swatch.Concrete).Box(new Vector3(0, 0.075f, 0), new Vector3(halfLength * 2 + 1.2f, 0.15f, halfWidth * 2 + 1.2f));
      kit.Paint(Swatch.ConcreteDark).Box(new Vector3(0, 0.155f, 0), new Vector3(halfLength * 2 - 0.6f, 0.01f, halfWidth * 2 - 0.6f));
      if (remaining > 0f) {
        var matrix = toWorld.Value;
        var center = matrix.MultiplyPoint3x4(new Vector3(0, 0.16f, 0));
        var yaw = matrix.rotation.eulerAngles.y + (halfWidth > halfLength ? 90f : 0f);
        Dressing.AddFitted(DressKind.LumberYard, center, yaw, Mathf.Max(halfLength, halfWidth) * 2f * 0.8f, Mathf.Min(halfLength, halfWidth) * 2f * 0.8f, seed);
      }
      return true;
    }

    static void Plant(MeshKit kit, List<(Vector3 Position, float Edge, int Id)> spots, int seed, float remaining, Matrix4x4? toWorld) {
      spots.Sort((a, b) => a.Edge != b.Edge ? a.Edge.CompareTo(b.Edge) : a.Id.CompareTo(b.Id));
      var cut = Mathf.RoundToInt(spots.Count * (1f - Mathf.Clamp01(remaining)));
      for (var i = 0; i < spots.Count; i++) {
        var state = i >= cut ? TreeState.Full : MeshKit.Hash(seed, spots[i].Id + 500) < 0.3f ? TreeState.Stump : TreeState.Sparse;
        var id = seed * 131 + spots[i].Id;
        if (toWorld.HasValue && DressTree(toWorld.Value.MultiplyPoint3x4(spots[i].Position), id, state)) {
          if (spots[i].Edge < 2.2f && MeshKit.Hash(id, 9) < 0.55f) {
            var side = new Vector3(MeshKit.Hash(id, 10) - 0.5f, 0, MeshKit.Hash(id, 11) - 0.5f) * 1.4f;
            var pick = MeshKit.Hash(id, 12);
            var edgeKind = pick < 0.5f ? DressKind.Bush : pick < 0.78f ? DressKind.Stump : DressKind.RockSmall;
            Dressing.Add(edgeKind, toWorld.Value.MultiplyPoint3x4(spots[i].Position + side), id + 7);
          }
          continue;
        }
        Tree(kit, spots[i].Position, id, state);
      }
    }
  }
}
