namespace FactoryWars.View {
  using UnityEngine;

  /// <summary>
  /// 룩 개발(항목 19A)에서 고른 후보 세 개. 에디터 설정이 이 값으로 프로필 에셋을 만든다.
  /// 1안이 기본 프로필(Resources/FactoryWars/Visual/VisualProfile)이고, 2안·3안은 VisualProfile_B·_C로 남긴다.
  /// 바꿔 끼우기: VisualProfile.Use("B") 또는 개발 빌드 설정 메뉴. 값의 근거는 LOOKDEV.md.
  /// </summary>
  public static class VisualPresets {
    public static readonly string[] Names = { "A", "B", "C" };

    public static string Title(string name) {
      switch (name) {
        case "B": return "2안 근접 강조";
        case "C": return "3안 넓은 시야";
        default: return "1안 전술 디오라마";
      }
    }

    /// <summary>세 안 모두에 공통인 값. aow3-ref 방향: 따뜻한 해, 진한 그림자, 층진 대지, 외곽선, 단순한 바닥.</summary>
    static void Common(VisualProfile p) {
      p.SunColor = new Color32(0xFF, 0xF4, 0xE0, 0xFF);
      p.SunIntensity = 1.2f;
      p.SunElevation = 40f;
      p.SunAzimuth = -35f;
      p.AmbientSky = new Color(0.52f, 0.6f, 0.72f);
      p.AmbientEquator = new Color(0.44f, 0.44f, 0.42f);
      p.AmbientGround = new Color(0.25f, 0.2f, 0.16f);
      p.ShadowStrength = 1f;
      p.Tone = ToneMode.Neutral;
      p.Saturation = 12f;
      p.Contrast = 10f;
      p.Vignette = 0.1f;
      p.Ssao = true;
      p.SsaoIntensity = 0.6f;
      p.SsaoRadius = 0.4f;
      p.BloomIntensity = 0.6f;
      p.BloomThreshold = 1.2f;
      p.Outline = OutlineMode.Hull;
      p.OutlineWidth = 0.05f;
      p.PlateauHeight = 5f;
      p.CliffLayers = 3;
      p.Jagged = 0.5f;
      p.WaterDepth = 2.5f;
      p.HighGroundHeight = 2.5f;
      p.Outside = OutsideMode.Dark;
      p.GroundPalette = 1;
      p.ZoneColors = true;
      p.PatchSize = 18f;
      p.LeafSet = 1;
      p.ConiferRatio = 0.75f;
      p.Bushes = false;
      p.NoBuild = NoBuildMode.Stripes;
      p.NoBuildAlways = false;
      p.UnitScale = 1.2f;
      p.BuildingBody = 0.78f;
      p.PlateScale = 1.05f;
    }

    static void Camera(VisualProfile p, float fov, float pitch, float height) {
      p.FieldOfView = fov;
      p.Pitch = pitch;
      p.DefaultHeight = height;
      p.MinHeight = height * 0.6f;
      p.MaxHeight = height * 2.4f;
    }

    public static void Apply(VisualProfile p, string name) {
      Common(p);
      switch (name) {
        case "B":
          // 2안: 더 가깝고 높은 각도, 큰 유닛, 굵은 외곽선, ACES.
          Camera(p, 35f, 55f, 95f);
          p.UnitScale = 1.4f;
          p.BuildingBody = 0.75f;
          p.OutlineWidth = 0.07f;
          p.Tone = ToneMode.Aces;
          p.Saturation = 20f;
          break;
        case "C":
          // 3안: 넓게 보는 시야, 외곽선 없음, 안개, 낮은 대지.
          Camera(p, 45f, 50f, 68f);
          p.BuildingBody = 0.85f;
          p.Outline = OutlineMode.None;
          p.Outside = OutsideMode.Fog;
          p.PlateauHeight = 3f;
          p.CliffLayers = 2;
          p.WaterDepth = 2f;
          p.HighGroundHeight = 1.5f;
          break;
        default:
          Camera(p, 30f, 50f, 104f);
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
