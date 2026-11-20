namespace FactoryWars.View {
  using System;
  using System.Collections.Generic;
  using UnityEngine;

  /// <summary>Kenney 모델로 바꿀 수 있는 화면 요소 종류(항목 19B). 판정과 무관한 겉모습 분류다.</summary>
  public enum DressKind {
    Conifer,
    Autumn,
    Broadleaf,
    TreeSparse,
    Stump,
    Bush,
    RockLarge,
    RockSmall,
    Cliff,
    CitySmall,
    CityMedium,
    CityIndustrial,
    CityTower,
    CityGarrison,
    Container,
    Crystal,
    Barrier,
    Chimney,
    Tank,
    Crane,
    Hopper,
    Ruin,
    Prop,
    Grass,
    Wreck,
    Debris,
    Worker,
    RoadProp,
    BridgePillar,
    BridgeDeck,
    SpaceProp,
    GarrisonBoard,
    BaseWall,
    SuburbanProp,
    Furniture,
    ScrapSkip,
    SpaceGate,
    SpaceFloor,
    BuildIndicator,
    RallyFlag,
    HardHat,
    GarageDoor,
    GroundPatch,
    BaseTent,
    SupplyCrate,
    StreetMachine,
    WorkVehicle,
    TrainTrack,
    TrainWreck,
    AnimatedWorker,
    /// <summary>항목 36 수문: 팩 옹벽·다리 기둥·철제 문을 우리 폴더 프리팹으로 조립한 것(WaterGateRig). 칸이 비어 있으면 수문 장식을 놓지 않는다.</summary>
    WaterGate,
    /// <summary>항목 36 시각 교정: 해안 옹벽 모듈(기둥·난간·계선주)과 점령 거점 소품(관측 초소·낮은 방벽).</summary>
    QuayPillar,
    QuayRail,
    QuayBollard,
    OutpostTower,
    OutpostWall,
    /// <summary>시가지 목재 자원지: 상자 더미 대신 단일 창고 건물(채집이 끝나면 사라진다).</summary>
    LumberYard,
    /// <summary>야전 룩 검문소 방벽(팩 군용 벽·철조망 울타리, 공사용 줄무늬 아님).</summary>
    Checkpoint,
    /// <summary>중앙 암시장 주둔지(사각 철조망 칸·차단기·방벽·막사·감시탑·소품). 팩 모델 그대로, 배율은 칸 데이터에만.</summary>
    TraderFence,
    TraderBoom,
    TraderSandbag,
    TraderHQ,
    TraderTower,
    TraderPad,
    TraderDish,
    TraderLight,
    TraderConsole,
    TraderNet,
    TraderRack,
    TraderAmmo,
    TraderBarrel,
    /// <summary>야전 길가(2026-11-16): 가로등·전신주·표지판·쓰레기통/소화전·버스 정류장·엄폐(방벽·헤스코·상자)·버려진 차량.</summary>
    RoadLight,
    RoadPole,
    RoadSign,
    RoadBin,
    RoadBusStop,
    RoadCover,
    RoadWreck,
    /// <summary>전진(확장) 사령부 표시: 이동식 레이더.</summary>
    ForwardRadar,
    /// <summary>보병이 드는 팩 소총(분대원·경비) / 저격 소총 / 대전차 RPG / 돌격 유탄발사기 / 공병 카빈.</summary>
    Rifle,
    SniperRifle,
    RocketLauncher,
    BreacherWeapon,
    EngineerCarbine,
    CityCafe,
  }

  /// <summary>
  /// 게임 요소 종류 → Kenney 프리팹 후보(가중치)·스케일·회전·틴트. 화면 전용이다.
  /// 파일 이름은 코드에 쓰지 않는다. `Assets/ExternalAssets/Kenney/kenney-map.json`이 원본이고
  /// `Tools/Factory Wars/Kenney/Build Map`이 이 에셋을 다시 만든다(모델 크기·바닥 보정도 그때 잰다).
  /// VisualProfile.Dressing이 이 맵을 가리키면 절차 메쉬 대신 이 모델을 쓴다.
  /// 변종·회전은 오브젝트 번호·좌표 해시로 고르므로 같은 장면은 항상 같은 화면이다.
  /// </summary>
  [CreateAssetMenu(menuName = "FactoryWars/Kenney Asset Map")]
  public class KenneyAssetMap : ScriptableObject {
    public const string ResourcePath = "FactoryWars/Visual/KenneyAssetMap";

    [Serializable]
    public class Variant {
      public GameObject Prefab;
      public float Weight = 1f;
      /// <summary>모델 좌표의 바닥 가운데를 원점으로 옮기는 보정(-중심 x, -바닥 y, -중심 z).</summary>
      public Vector3 Offset;
      /// <summary>모델 좌표의 크기(바운드).</summary>
      public Vector3 Size = Vector3.one;
    }

    [Serializable]
    public class Entry {
      public DressKind Kind;
      public List<Variant> Variants = new List<Variant>();
      /// <summary>모델 1단위 → 월드 미터. 전차 기준(나무 높이 전차 2~3배, 건물 1층 전차 1.5배)으로 맞춘다. Fit이면 칸에 맞춘 뒤 곱한다.</summary>
      public float Scale = 1f;
      public Vector2 ScaleJitter = new Vector2(0.9f, 1.1f);
      /// <summary>Y축 회전 범위(도). SnapYaw90이면 90도 단위다.</summary>
      public Vector2 Yaw = new Vector2(0f, 360f);
      public bool SnapYaw90;
      /// <summary>건물 부지처럼 정해진 칸에 넣을 때: 칸 안에 들어가는 가장 큰 배율로 맞춘다.</summary>
      public bool Fit;
      public bool Shadows = true;
      /// <summary>품질 낮음에서 남기는 비율(나무·소품 수 줄이기).</summary>
      [Range(0f, 1f)] public float LowKeep = 1f;
      /// <summary>품질 보통에서 남기는 비율(흩뿌리기 소품은 0.7).</summary>
      [Range(0f, 1f)] public float MediumKeep = 1f;
    }

    public List<Entry> Entries = new List<Entry>();

    /// <summary>군용 재도색(항목 19C)을 프로필로 남긴 것(항목 27). 머티리얼마다 군용 아틀라스 또는 바꾼 색. 기본(원본) 머티리얼은 그대로 둔다.</summary>
    [Serializable]
    public class MilitarySwap {
      public Material Material;
      public Texture2D Texture;
      public bool HasColor;
      public Color Color = Color.white;
    }

    public List<MilitarySwap> Military = new List<MilitarySwap>();
    public AnimationClip WorkerIdle;
    public AnimationClip WorkerRun;

    [NonSerialized] Dictionary<DressKind, Entry> _lookup;

    public Entry Get(DressKind kind) {
      if (_lookup == null) {
        _lookup = new Dictionary<DressKind, Entry>();
        foreach (var entry in Entries) {
          if (entry.Variants.Exists(v => v.Prefab != null)) {
            _lookup[entry.Kind] = entry;
          }
        }
      }
      _lookup.TryGetValue(kind, out var found);
      // 항목 30: VisualCatalog 슬롯이 채워져 있으면 Kenney 대신 그 프리팹을 쓴다.
      return VisualCatalog.DressOverride(kind, found) ?? found;
    }

    public bool Has(DressKind kind) => Get(kind) != null;

    /// <summary>해시(0~1) 하나로 가중치에 맞는 변종을 고른다.</summary>
    public static Variant Pick(Entry entry, float hash) {
      var total = 0f;
      foreach (var variant in entry.Variants) {
        total += Mathf.Max(0f, variant.Weight);
      }
      var target = hash * total;
      foreach (var variant in entry.Variants) {
        target -= Mathf.Max(0f, variant.Weight);
        if (target <= 0f) {
          return variant;
        }
      }
      return entry.Variants[entry.Variants.Count - 1];
    }

    /// <summary>개체 하나의 배치: 변종, 회전, 배율. seed는 오브젝트 번호나 좌표에서 만든다.</summary>
    public static (Variant Variant, Quaternion Rotation, float Scale) Place(Entry entry, int seed) {
      var variant = Pick(entry, MeshKit.Hash(seed, 1));
      var yaw = Mathf.Lerp(entry.Yaw.x, entry.Yaw.y, MeshKit.Hash(seed, 2));
      if (entry.SnapYaw90) {
        yaw = Mathf.Round(yaw / 90f) * 90f;
      }
      var scale = entry.Scale * Mathf.Lerp(entry.ScaleJitter.x, entry.ScaleJitter.y, MeshKit.Hash(seed, 3));
      return (variant, Quaternion.Euler(0f, yaw, 0f), scale);
    }

    /// <summary>칸(가로 length × 세로 width) 안에 들어가는 배율. 모델을 90도 돌렸으면 turned를 준다.</summary>
    public static float FitScale(Variant variant, float length, float width, bool turned) {
      var x = turned ? variant.Size.z : variant.Size.x;
      var z = turned ? variant.Size.x : variant.Size.z;
      return Mathf.Min(length / Mathf.Max(0.01f, x), width / Mathf.Max(0.01f, z));
    }

    /// <summary>모델 좌표 → 월드: 위치·회전·배율 뒤에 바닥 보정을 곱한다.</summary>
    public static Matrix4x4 Matrix(Variant variant, Vector3 position, Quaternion rotation, Vector3 scale) => Matrix4x4.TRS(position, rotation, scale) * Matrix4x4.Translate(variant.Offset);

    /// <summary>좌표 해시. 같은 자리면 같은 값이다.</summary>
    public static int Seed(Vector3 position, int salt) => Mathf.RoundToInt(position.x * 7.31f) * 73856093 ^ Mathf.RoundToInt(position.z * 5.17f) * 19349663 ^ salt * 83492791;
  }
}
