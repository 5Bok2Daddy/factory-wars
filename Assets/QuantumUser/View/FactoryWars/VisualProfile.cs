namespace FactoryWars.View {
  using UnityEngine;

  public enum OutlineMode { None, Hull }

  public enum OutsideMode { Dark, Fog, Sea }

  public enum NoBuildMode { Stripes, RedTint, Grid }

  public enum ToneMode { Neutral, Aces, None }

  /// <summary>
  /// 화면 설정 한 묶음. 카메라·조명·후처리·지형 높이·팔레트·외곽선·표시 크기를 모은다.
  /// 기본 프로필은 Resources/FactoryWars/Visual/VisualProfile이고, 후보마다 에셋 하나를 두어 바꿔 끼운다.
  /// 화면 전용이다. 시뮬레이션은 이 값을 읽지 않는다.
  /// </summary>
  [CreateAssetMenu(menuName = "FactoryWars/Visual Profile")]
  public class VisualProfile : ScriptableObject {
    public const string ResourcePath = "FactoryWars/Visual/VisualProfile";

    [Header("카메라")]
    public float FieldOfView = 60f;
    public float Pitch = 50f;
    public float DefaultHeight = 45f;
    public float MinHeight = 30f;
    public float MaxHeight = 110f;

    [Header("표시 크기")]
    /// <summary>유닛 표시 배율에 곱한다. 선택 반경과 체력바 높이도 따라간다.</summary>
    public float UnitScale = 1f;
    [Header("움직임 (항목 32, 화면 전용)")]
    [Tooltip("시뮬레이션 위치를 따라가는 속도(클수록 빠르게 붙는다). 20Hz 틱 사이를 부드럽게 잇는다.")]
    public float FollowRate = 16f;
    [Tooltip("차량 가감속 1m/s²당 앞뒤 기울기(도)와 최대값.")]
    public float VehicleTiltPerAccel = 1.2f;
    public float VehicleMaxTilt = 5f;
    [Tooltip("헬기·비행기: 선회 1도/초당 옆 기울기(도), 최대값, 속도 1m/s당 앞 숙임(도).")]
    public float AirBankPerTurn = 0.25f;
    public float AirMaxBank = 25f;
    public float AirPitchPerSpeed = 1.5f;
    [Tooltip("기울기가 목표로 따라가는 속도.")]
    public float TiltRate = 6f;
    /// <summary>건물 본체를 칸 크기의 이 비율로 줄이고 바닥판을 깐다. 1이면 바닥판이 없다.</summary>
    public float BuildingBody = 1f;
    public float PlateScale = 1.05f;
    public float PlateHeight = 0.15f;

    [Header("조명")]
    public Color SunColor = new Color(1f, 0.96f, 0.88f);
    public float SunIntensity = 0.98f;
    public float SunElevation = 52f;
    public float SunAzimuth = 20f;
    public Color AmbientSky = new Color(0.56f, 0.59f, 0.64f);
    public Color AmbientEquator = new Color(0.44f, 0.44f, 0.42f);
    public Color AmbientGround = new Color(0.28f, 0.26f, 0.24f);
    public float ShadowStrength = 1f;

    [Header("후처리")]
    public ToneMode Tone = ToneMode.Neutral;
    public float BloomIntensity = 0.8f;
    public float BloomThreshold = 1.2f;
    public float BloomScatter = 0.68f;
    public float Saturation = 0f;
    public float Contrast = 0f;
    public float Vignette = 0f;
    public bool Ssao = false;
    public float SsaoIntensity = 0.6f;
    public float SsaoRadius = 0.4f;
    /// <summary>발광 칸의 HDR 세기. 1이면 지금과 같다.</summary>
    public float GlowBoost = 1f;

    [Header("외곽선")]
    public OutlineMode Outline = OutlineMode.None;
    public float OutlineWidth = 0.04f;
    public Color OutlineColor = new Color32(0x1E, 0x1A, 0x16, 0xFF);

    [Header("지형")]
    /// <summary>통과 불가 대지(언덕·능선)의 높이.</summary>
    public float PlateauHeight = 4f;
    public int CliffLayers = 3;
    /// <summary>절벽 가장자리의 들쭉날쭉한 정도. 0~1.</summary>
    public float Jagged = 0.5f;
    /// <summary>고지대 윗면 높이. 판정은 2D이고 화면에서만 올린다.</summary>
    public float HighGroundHeight = 0f;
    public float WaterDepth = 0.8f;
    public OutsideMode Outside = OutsideMode.Dark;
    /// <summary>바닥 색 세트 번호. TerrainKit.GroundPalettes.</summary>
    public int GroundPalette = 0;
    public float PatchSize = 18f;
    /// <summary>0 흙길, 1 밝은 흙길.</summary>
    public int RoadStyle = 0;
    public bool ZoneColors = false;
    public Color WaterDeep = new Color(0.12f, 0.27f, 0.36f);
    public Color WaterShallow = new Color(0.36f, 0.62f, 0.62f);

    [Header("정점 음영 (구운 그라데이션·AO·색 노이즈)")]
    /// <summary>팔레트 텍스처의 세로 줄이 밝기 단계다. 메쉬를 만들 때 정점마다 밝기를 정해 V 좌표로 굽는다.</summary>
    public bool VertexShade = false;
    /// <summary>물체 아래→위 밝기 차이(전체 폭).</summary>
    public float ShadeGradient = 0.16f;
    /// <summary>바닥 접촉부 어둡게 하는 세기와 높이.</summary>
    public float ShadeAo = 0.3f;
    public float ShadeAoHeight = 1.2f;
    /// <summary>바닥·절벽의 낮은 주파수 밝기 변주.</summary>
    public float ShadeNoise = 0.07f;

    [Header("외부 모델 (항목 19B)")]
    /// <summary>비어 있으면 절차 생성 메쉬, 있으면 이 맵의 Kenney 모델로 자연물·도시 구조물을 그린다.</summary>
    public KenneyAssetMap Dressing;

    [Header("맵 경계")]
    /// <summary>0 고층 블록 벽, 1 층진 바위 절벽과 숲.</summary>
    public int Boundary = 0;
    /// <summary>항목 36 시가지 장식(길가 엄폐 소품·신호등·맵 가장자리 건물 외벽·수로 둑)을 켠다. 구입한 팩이 없으면 켜도 아무것도 놓이지 않는다.</summary>
    public bool UrbanDecor;
    /// <summary>시가지 장식 남김 비율: 품질 낮음·보통(높음은 전부).</summary>
    public float UrbanDensityLow = 0.4f;
    public float UrbanDensityMedium = 0.7f;

    [Header("바닥 정점 색 (항목 25-1)")]
    /// <summary>true면 바닥 색을 정점마다 정해 부드럽게 섞는다(삼각형 체크무늬 제거). 색은 아래 값이다.</summary>
    public bool GroundVertexColors = true;
    public Color GroundGrass = new Color32(0x5B, 0x72, 0x40, 0xFF);
    public Color GroundGrassLight = new Color32(0x6C, 0x81, 0x4A, 0xFF);
    public Color GroundGrassDark = new Color32(0x46, 0x5A, 0x31, 0xFF);
    public Color GroundDirt = new Color32(0x86, 0x72, 0x55, 0xFF);
    public Color GroundDirtDark = new Color32(0x69, 0x59, 0x45, 0xFF);

    [Header("Kenney 원본 분위기 (항목 27)")]
    /// <summary>true면 Kenney 모델을 군용 재도색(colormap_military·nature 색 바꾸기)으로 그린다. false면 원본 색이다.</summary>
    public bool KenneyMilitary;
    /// <summary>유닛·플레이어 건물 팔레트의 금속·장갑 칸을 밝게 올리는 정도(0~1). 팀 색·발광 칸은 그대로다. 팔레트를 구울 때 쓴다.</summary>
    public float PaletteLift;
    /// <summary>도로 가운데 선을 흰색으로(Kenney 도시 기준). false면 노란 점선이다.</summary>
    public bool WhiteLanes;
    /// <summary>본진 광장·보도 색(바닥 정점 색).</summary>
    public Color GroundPlaza = new Color32(0x9A, 0x98, 0x92, 0xFF);
    public Color GroundPlazaDark = new Color32(0x86, 0x84, 0x7E, 0xFF);
    /// <summary>도로 아스팔트 색(알파 0이면 팔레트 기본색). 항목 27 K-0 색 맞추기에서 정한다.</summary>
    public Color RoadColor = new Color(0f, 0f, 0f, 0f);
    /// <summary>항목 27 K: 단색 바닥. 본진·도로·도시 건물 둘레는 보도(광장) 한 색, 나머지는 잔디 한 색. 얼룩 노이즈와 흙 섞임이 없다. 숲 바닥·팔레트 잔디 칸도 프로필 잔디색을 따른다.</summary>
    public bool SolidGround;
    /// <summary>맵 밖 배경색(OutsideMode.Dark).</summary>
    public Color BackgroundColor = new Color(0.11f, 0.12f, 0.13f);
    /// <summary>강·운하·수로 물색. 알파가 0이면 팔레트 기본색을 쓴다.</summary>
    public Color WaterColor = new Color(0f, 0f, 0f, 0f);
    /// <summary>절벽 단마다 밝기 차이(아래 단이 어둡고 위 단이 밝다). 단색 면에서 층 경계가 보이게 한다.</summary>
    public float CliffTierShade;
    /// <summary>통과 불가 대지 윗면에 심는 장식 숲 밀도(0~1, 3.5m 격자에서 심을 확률).</summary>
    public float PlateauTrees;

    [Header("숲·바위")]
    public float TreeDensity = 1f;
    public float ConiferRatio = 1f;
    /// <summary>Kenney 나무에서 단풍 비율. 나머지(1 - 침엽 - 단풍)는 짙은 활엽이다.</summary>
    public float AutumnRatio = 0.25f;
    /// <summary>잎 색 조합 번호. 0 한 가지, 1 세 가지.</summary>
    public int LeafSet = 0;
    public bool Bushes = false;

    [Header("건설 금지 표시")]
    public NoBuildMode NoBuild = NoBuildMode.Stripes;
    /// <summary>false면 건설 위치를 고를 때만 보인다.</summary>
    public bool NoBuildAlways = true;

    static VisualProfile _current;

    /// <summary>개발 빌드에서 고른 후보 이름("A"·"B"·"C"). 설정 화면에서 바꾼다.</summary>
    public const string ChoiceKey = "fw_visual_profile";

    /// <summary>후보 프로필로 바꾼다. "A"는 기본 프로필, "B"·"C"는 VisualProfile_B·_C 에셋이다. 다음에 만드는 화면부터 적용된다.</summary>
    public static bool Use(string name) {
      var profile = Resources.Load<VisualProfile>(name == "A" ? ResourcePath : ResourcePath + "_" + name);
      if (profile == null) {
        return false;
      }
      _current = profile;
      return true;
    }

    /// <summary>지금 쓰는 프로필. 비교 도구가 바꿔 끼운다.</summary>
    public static VisualProfile Current {
      get {
        if (_current == null && Debug.isDebugBuild && Application.isEditor == false && PlayerPrefs.HasKey(ChoiceKey)) {
          Use(PlayerPrefs.GetString(ChoiceKey));
        }
        if (_current == null) {
          _current = Resources.Load<VisualProfile>(ResourcePath);
          if (_current == null) {
            _current = CreateInstance<VisualProfile>();
          }
        }
        return _current;
      }
      set => _current = value;
    }
  }
}
