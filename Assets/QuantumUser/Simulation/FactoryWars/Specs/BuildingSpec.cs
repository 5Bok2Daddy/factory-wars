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
  }
}
