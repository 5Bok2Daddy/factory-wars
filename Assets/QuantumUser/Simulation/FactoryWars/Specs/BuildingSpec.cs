namespace Quantum {
  using Photon.Deterministic;

  public partial class BuildingSpec : AssetObject {
    public BuildingRole Role;
    /// <summary>짓는 데 필요한 기지 단계(항목 31).</summary>
    public int RequiredTier = 1;
    public ResourceCost Cost;
    public FP BuildTimeSeconds;
    public FP MaxHealth;
    /// <summary>초당 전기 생산량.</summary>
    public FP PowerOutput;
    /// <summary>늘어나는 전기 저장 한도.</summary>
    public FP PowerStorage;
    /// <summary>가동 중 초당 전기 소모량.</summary>
    public FP PowerUpkeep;
    public UpkeepMode Upkeep;
    /// <summary>0보다 크면 가공소다. 이 간격마다 재료를 강철로 바꾼다.</summary>
    public FP ProcessSeconds;
    public ResourceCost ProcessInput;
    public FP ProcessSteel;
    public FP PopulationCapBonus;
    public int ModuleSlots;
    /// <summary>정사각형 부지의 중심에서 변까지 거리.</summary>
    public FP FootprintRadius;
    /// <summary>공병이 철·목재를 반입할 수 있는 건물.</summary>
    public bool IsDropOff;
    /// <summary>짓기 전에 모두 완공되어 있어야 하는 건물.</summary>
    public AssetRef<BuildingSpec>[] Prerequisites;
    /// <summary>사령부와 생산 건물. 이것을 모두 잃으면 패배한다.</summary>
    public bool IsCoreBuilding;
    /// <summary>이 건물에서 생산할 수 있는 유닛.</summary>
    public AssetRef<UnitSpec>[] Producible;
    public AttackType Attack;
    /// <summary>true면 지상만 쏜다. 대장갑 무기라도 저공 비행 유닛을 맞히지 않는다.</summary>
    public bool GroundOnly;
    public FP Damage;
    public FP Range;
    public FP AttackInterval;
    /// <summary>항목 32 포병 진지: 최소 사거리와 범위 피해 반지름.</summary>
    public FP MinRange;
    public FP SplashRadius;
    /// <summary>항목 32: 완공 시 둘레에 만드는 보병 엄폐점 수와 피해 감소율(0.25 낮음, 0.5 높음).</summary>
    public int CoverSpots;
    public FP CoverReduction;
    /// <summary>항목 32 의무대·정비소: 반경 안 아군을 초당 HealPerSecond만큼 HealCount기까지 고친다.</summary>
    public HealTarget Heals;
    public FP HealRadius;
    public FP HealPerSecond;
    public int HealCount;
    /// <summary>항목 32 레이더: 이 반경 안의 적은 고지대에 있어도 보이고, 들어오면 경고한다.</summary>
    public FP RevealRadius;
    /// <summary>건설 메뉴에서 "방어 건물" 묶음에 넣는다(화면 전용 분류).</summary>
    public bool Defensive;
    /// <summary>항목 33: 물가에만 지을 수 있다(조선소와 같은 규칙). 강습 도크.</summary>
    public bool NeedsWater;
    /// <summary>항목 33 벙커: 보병 분대가 이 수만큼 들어가 안에서 쏜다. 안에 있는 동안은 맞지 않고, 건물이 부서지면 튕겨 나온다.</summary>
    public int GarrisonCapacity;
  }
}
