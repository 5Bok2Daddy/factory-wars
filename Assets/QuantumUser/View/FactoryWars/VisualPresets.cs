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
        case "B": return "B 군용 전장(19D)";
        case "C": return "C Kenney 저사양";
        default: return "A Kenney 원본";
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

    /// <summary>
    /// 항목 27 결정(K): Kenney 원본 분위기. 원본 colormap, 단색 바닥(민트 잔디·밝은 회색 보도·짙은 회색 도로와 흰 차선),
    /// 밝은 하늘빛 환경광과 연한 짧은 그림자, 연한 회청색 배경, 외곽선·블룸·안개·SSAO 없음. 카메라·높낮이는 27 2-1·2-2 값.
    /// </summary>
    static void Kenney(VisualProfile p) {
      Aow3(p);
      p.FieldOfView = 33f;
      p.Pitch = 50f;
      p.DefaultHeight = 62f;
      p.MinHeight = 35f;
      p.MaxHeight = 140f;
      // 빛·바닥색은 K-0 색 맞추기 2차(Lookdev_KenneyColorMatchNeutral, 참고 장면에서 받아 둔 Kenney 모델로 렌더) 결과다.
      // 햇빛은 약하고 환경광(HDR)이 대부분이라 그림자가 연하다(그림자/빛 약 0.65). 빛의 파랑은 그림자 비율에 필요한 만큼(+15%)만 두고,
      // 나머지 푸른 기운은 바닥 단색이 맡는다. 그래야 Kenney 모델 원래 색(벽·나무)이 보라·하늘색으로 바뀌지 않는다.
      p.SunColor = new Color32(0xFF, 0xF8, 0xEE, 0xFF);
      p.SunIntensity = 0.3f;
      p.SunElevation = 58f;
      p.SunAzimuth = -30f;
      p.AmbientSky = new Color(1.046f, 1.044f, 1.115f);
      p.AmbientEquator = new Color(0.988f, 0.985f, 1.052f);
      p.AmbientGround = new Color(0.893f, 0.891f, 0.95f);
      p.ShadowStrength = 1f;
      p.Tone = ToneMode.None;
      p.BloomIntensity = 0f;
      p.Saturation = 0f;
      p.Contrast = 0f;
      p.Vignette = 0f;
      p.Ssao = false;
      p.Outline = OutlineMode.None;
      p.VertexShade = false;
      p.KenneyMilitary = false;
      p.PaletteLift = 0.32f;
      p.WhiteLanes = true;
      p.GroundGrass = new Color32(0x62, 0xBA, 0x90, 0xFF);
      p.GroundGrassLight = new Color32(0x68, 0xBD, 0x94, 0xFF);
      p.GroundGrassDark = new Color32(0x5C, 0xAF, 0x87, 0xFF);
      // 흙은 보도와의 밝기 비를 예전 값 그대로 두고 새 빛에 맞춰 옮겼다.
      p.GroundDirt = new Color32(0x9F, 0xA8, 0xB3, 0xFF);
      p.GroundDirtDark = new Color32(0x95, 0x9D, 0xA9, 0xFF);
      p.GroundPlaza = new Color32(0x90, 0x98, 0xA5, 0xFF);
      p.GroundPlazaDark = new Color32(0x87, 0x8F, 0x9B, 0xFF);
      p.RoadColor = new Color32(0x32, 0x3A, 0x3C, 0xFF);
      p.SolidGround = true;
      p.BackgroundColor = new Color32(0xB2, 0xBA, 0xCC, 0xFF);
      p.WaterColor = new Color32(0x4A, 0xA3, 0xD6, 0xFF);
      p.Outside = OutsideMode.Dark;
      p.PlateauHeight = 11f;
      p.CliffLayers = 4;
      p.HighGroundHeight = 5f;
      p.WaterDepth = 7f;
      p.CliffTierShade = 0.22f;
      p.PlateauTrees = 0.55f;
      p.ConiferRatio = 0.6f;
      p.AutumnRatio = 0.25f;
    }

    public static void Apply(VisualProfile p, string name) {
      if (name == "B") {
        // B: 19D까지의 군용 전장 룩을 프로필로 남긴다(군용 재도색 Kenney, AoW3 빛).
        Aow3(p);
        p.KenneyMilitary = true;
        return;
      }
      Kenney(p);
      switch (name) {
        case "C":
          // C: Kenney 저사양. 그림자 조금 연하게, 장식 숲 줄임. 나머지는 A와 같다.
          p.ShadowStrength = 0.8f;
          p.PlateauTrees = 0.3f;
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
