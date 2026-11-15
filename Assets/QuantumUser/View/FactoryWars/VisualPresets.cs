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
      // 장난감 블록 입체감 복원: 태양광 윗면 100% 밝기 + 그늘진 옆면 65~70% 파스텔 블루/퍼플 그늘 (#8E96B2)
      p.SunColor = new Color32(0xFF, 0xFA, 0xEE, 0xFF);
      p.SunIntensity = 1.05f;
      p.SunElevation = 48f;
      p.SunAzimuth = -35f;
      p.AmbientSky = new Color(0.40f, 0.42f, 0.54f);
      p.AmbientEquator = new Color(0.32f, 0.34f, 0.44f);
      p.AmbientGround = new Color(0.20f, 0.22f, 0.30f);
      p.ShadowStrength = 0.88f;
      p.Tone = ToneMode.Neutral;
      p.BloomIntensity = 0f;
      p.Saturation = 0.08f;
      p.Contrast = 10f;
      p.Vignette = 0f;
      p.Ssao = true;
      p.SsaoIntensity = 3.2f;
      p.SsaoRadius = 0.9f;
      p.Outline = OutlineMode.None;
      p.VertexShade = false;
      p.KenneyMilitary = false;
      p.PaletteLift = 0.9f;
      p.WhiteLanes = true;
      p.UrbanDecor = true;
      // 항목 36 시각 교정: 건물 옥상(통과 불가 대지 윗면)에 건물만 하게 크던 상자·드럼통 묶음을 심지 않는다.
      p.PlateauTrees = 0f;
      // 항목 36: 청록 잔디 바닥을 시가지 콘크리트·아스팔트 톤으로. 바닥은 중간 회색, 본진·광장은 밝은 보도색, 길은 짙은 아스팔트.
      // 렌더 값은 이 색의 약 1.4배로 밝아진다(햇빛 1.05 + 환경광). 밝기 245 이상 픽셀 3% 이하가 되도록 낮게 둔다.
      // 항목 36 시각 교정: 도로·건물 둘레(보도)는 콘크리트, 그 바깥 빈 땅은 파스텔 민트 잔디(렌더가 약 1.4배 밝아지므로 낮게 잡음).
      // 야전 룩(2026-11-15 사용자 지시): 에란겔식 무채색 올리브·카키 잔디와 마른 흙.
      p.FieldLook = true;
      p.GroundGrass = new Color32(0x5C, 0x66, 0x49, 0xFF);
      p.GroundGrassLight = new Color32(0x64, 0x6E, 0x50, 0xFF);
      p.GroundGrassDark = new Color32(0x54, 0x5D, 0x43, 0xFF);
      p.GroundDirt = new Color32(0x78, 0x6B, 0x56, 0xFF);
      p.GroundDirtDark = new Color32(0x6A, 0x5E, 0x4B, 0xFF);
      // 길·건물 둘레(예전 보도 자리)는 밟아 다진 흙.
      p.GroundPlaza = new Color32(0x78, 0x6B, 0x56, 0xFF);
      p.GroundPlazaDark = new Color32(0x6A, 0x5E, 0x4B, 0xFF);
      // 흙길(야전 룩).
      p.RoadColor = new Color32(0x6B, 0x5E, 0x4A, 0xFF);
      p.TreeDensity = 0.5f;
      p.SolidGround = true;
      p.BackgroundColor = new Color32(0xB2, 0xBA, 0xCC, 0xFF);
      p.WaterColor = new Color32(0x4A, 0xA3, 0xD6, 0xFF);
      // 항목 36 추가: 파스텔 에메랄드 물(참고 그림의 밝고 맑은 수로), 깊이 거품 폭 0.9m.
      p.WaterShallowTint = new Color32(0x73, 0xDB, 0xBD, 0xFF);
      p.WaterDeepTint = new Color32(0x2B, 0x9E, 0xA3, 0xFF);
      p.WaterFoamTint = new Color32(0xF7, 0xFF, 0xFF, 0xFF);
      p.WaterFoamWidth = 1.3f;
      p.WaterFlowSpeed = new Vector2(0.06f, 0.03f);
      p.GateDropSeconds = 2.4f;
      p.GateDoorHeight = 3.2f;
      p.GatePostHeight = 4.6f;
      p.Outside = OutsideMode.Dark;
      p.PlateauHeight = 6f;
      // 29 2-1: 단일 슬래브 절벽으로 얼룩말 층층 줄무늬 제거
      p.CliffLayers = 1;
      p.HighGroundHeight = 5f;
      p.WaterDepth = 7f;
      p.CliffTierShade = 0f;
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
