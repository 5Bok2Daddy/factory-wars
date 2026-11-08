namespace FactoryWars.View {
  using UnityEngine;

  /// <summary>
  /// 룩 개발(항목 19A)의 완성 후보 세 개. 에디터 설정(Tools/Factory Wars/Build Visual Profiles)이 이 값으로 프로필 에셋을 다시 쓴다.
  /// A가 기본 프로필(Resources/FactoryWars/Visual/VisualProfile)이고, B·C는 VisualProfile_B·VisualProfile_C로 남는다.
  /// 바꿔 끼우기: VisualProfile.Use("B"), 개발 빌드 설정 화면의 "화면" 버튼. 값의 근거는 LOOKDEV.md.
  /// </summary>
  public static class VisualPresets {
    public static readonly string[] Names = { "A", "B", "C" };

    public static string Title(string name) {
      switch (name) {
        case "B": return "B 스타일라이즈드";
        case "C": return "C 성능 우선";
        default: return "A AoW3 전장";
      }
    }

    /// <summary>
    /// A: aow3-ref 방향. 따뜻한 오후 해, 좁은 시야각, 층진 바위 절벽과 숲 경계, 정점 그라데이션·구운 AO·색 노이즈.
    /// 외곽선·채도 강화·블룸 강화는 쓰지 않는다(19A 지시).
    /// </summary>
    static void Aow3(VisualProfile p) {
      p.FieldOfView = 30f;
      p.Pitch = 50f;
      p.DefaultHeight = 104f;
      p.MinHeight = 62f;
      p.MaxHeight = 250f;
      p.UnitScale = 1.2f;
      p.BuildingBody = 0.78f;
      p.PlateScale = 1.05f;
      p.PlateHeight = 0.15f;

      p.SunColor = new Color32(0xFF, 0xF4, 0xE0, 0xFF);
      p.SunIntensity = 1.2f;
      p.SunElevation = 40f;
      p.SunAzimuth = -35f;
      p.AmbientSky = new Color(0.52f, 0.6f, 0.72f);
      p.AmbientEquator = new Color(0.44f, 0.44f, 0.42f);
      p.AmbientGround = new Color(0.25f, 0.2f, 0.16f);
      p.ShadowStrength = 0.9f;

      p.Tone = ToneMode.Neutral;
      p.BloomIntensity = 0.5f;
      p.BloomThreshold = 1.2f;
      p.BloomScatter = 0.6f;
      p.Saturation = 0f;
      p.Contrast = 6f;
      p.Vignette = 0.1f;
      p.Ssao = true;
      p.SsaoIntensity = 0.6f;
      p.SsaoRadius = 0.4f;
      p.GlowBoost = 1f;

      p.Outline = OutlineMode.None;
      p.OutlineWidth = 0.05f;

      p.VertexShade = true;
      p.ShadeGradient = 0.16f;
      p.ShadeAo = 0.3f;
      p.ShadeAoHeight = 1.2f;
      p.ShadeNoise = 0.07f;

      p.PlateauHeight = 5f;
      // Kenney 절벽 조각은 층이 낮으면 풀 턱만 보인다. 2층(층당 2.5m)이 바위 면이 잘 보인다(19B).
      p.CliffLayers = 2;
      p.Jagged = 0.5f;
      p.HighGroundHeight = 2.5f;
      p.WaterDepth = 2.5f;
      p.Outside = OutsideMode.Dark;
      p.Boundary = 1;
      p.GroundPalette = 1;
      p.PatchSize = 18f;
      p.RoadStyle = 0;
      p.ZoneColors = true;

      p.TreeDensity = 1f;
      p.ConiferRatio = 0.6f;
      p.AutumnRatio = 0.22f;
      p.LeafSet = 1;
      p.Bushes = false;

      p.NoBuild = NoBuildMode.Stripes;
      p.NoBuildAlways = false;
    }

    public static void Apply(VisualProfile p, string name) {
      Aow3(p);
      switch (name) {
        case "B":
          // B: 08_target_style 방향 비교용. 평면 색 + 외곽선, 채도·대비 강화, 조금 더 높은 각도.
          p.FieldOfView = 35f;
          p.Pitch = 55f;
          p.DefaultHeight = 95f;
          p.MinHeight = 57f;
          p.MaxHeight = 228f;
          p.BuildingBody = 0.8f;
          p.Outline = OutlineMode.Hull;
          p.OutlineWidth = 0.06f;
          p.Saturation = 12f;
          p.Contrast = 10f;
          p.BloomIntensity = 0.8f;
          p.VertexShade = false;
          p.Boundary = 0;
          break;
        case "C":
          // C: 저사양 폰 기준. A에서 SSAO·비네트·블룸을 끄고 그림자를 약하게. 정점 음영은 비용이 없어 유지한다.
          p.Ssao = false;
          p.Vignette = 0f;
          p.BloomIntensity = 0f;
          p.ShadowStrength = 0.75f;
          p.CliffLayers = 2;
          break;
      }
    }

    public static VisualProfile Create(string name) {
      var profile = ScriptableObject.CreateInstance<VisualProfile>();
      profile.name = name == "A" ? "VisualProfile" : "VisualProfile_" + name;
      Apply(profile, name);
      return profile;
    }
  }
}
