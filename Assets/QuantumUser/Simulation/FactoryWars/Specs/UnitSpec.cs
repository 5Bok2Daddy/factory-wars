namespace Quantum {
  using Photon.Deterministic;

  public partial class UnitSpec : AssetObject {
    public UnitSize Size;
    /// <summary>생산에 필요한 기지 단계(항목 31). 소형 1, 중형 2, 대형 3.</summary>
    public int RequiredTier = 1;
    public bool IsAir;
    /// <summary>배(항목 24). 수상 NavMesh로만 다니고 점령은 못 한다. 대상으로는 지상과 같다(공중이 아니므로 지상 무기에 맞는다).</summary>
    public bool IsNaval;
    /// <summary>Task 39: hull/mast height above the water line (m). A ship passes under a bridge only if this is not above the bridge clearance.</summary>
    public FP HullHeight;
    /// <summary>항목 32: 소형 차량(버기·오토바이). 보병이 아니라 진지·엄폐·의무대 대상이 아니다.</summary>
    public bool IsVehicle;
    public ResourceCost Cost;
    public FP BuildTimeSeconds;
    public FP MaxHealth;
    public AttackType Attack;
    public FP Damage;
    public FP Range;
    public FP MinRange;
    public FP SplashRadius;
    public FP AttackInterval;
    public bool IsRanged;
    public FP MoveSpeed;
    /// <summary>충돌·분리·대형 간격 계산용 반경(m). 보병 0.65, 경차량 1.4, 중형 전차/MLRS 2.2, 대형 중전차 2.6.</summary>
    public FP ColliderRadius;
    /// <summary>유닛 한 기가 차지하는 인구. 민병대는 2명이 인구 1이라 0.5.</summary>
    public FP Population;
    public int ModuleSlots;
    /// <summary>1회 생산으로 나오는 유닛 수.</summary>
    public int SpawnCount = 1;
    public SpecialAbility Ability;
    /// <summary>특수 기능의 배율. 기능마다 피해·속도·받는 피해 중 하나에 곱한다.</summary>
    public FP AbilityMultiplier = 1;
    /// <summary>표적 지시 유지 시간, 교전 종료로 보는 시간.</summary>
    public FP AbilitySeconds;
    public FP AbilityRadius;
    /// <summary>발동에 필요한 대수, 동시 공격 대수.</summary>
    public int AbilityCount;
    public FP AbilityCooldownSeconds;
    /// <summary>항목 34: 시야 반경(m). 0이면 시야 없음.</summary>
    public FP SightRange;

    // ---- 항목 34-C 주둔 저격수(데이터. 값이 0/false인 유닛은 영향 없음) ----
    /// <summary>주둔 중에만 사격한다. 지상에서는 쏠 수 없고 공격 명령을 거부한다(NeedGarrison).</summary>
    public bool GarrisonFireOnly;
    /// <summary>주둔 중 시야 반경(m).</summary>
    public FP GarrisonSightRange;
    /// <summary>올라간 직후 사격 못 하는 시간(올라가기 3초 + 자리 잡기 2초).</summary>
    public FP SetupSeconds;
    /// <summary>보병·공병 대비 차량(IsVehicle·중형 이상)에 주는 피해 비율(30/140).</summary>
    public FP VehicleDamageRatio = 1;
    /// <summary>플레이어당 보유 상한. 0이면 제한 없음.</summary>
    public int MaxOwned;
    /// <summary>은신: 지속·재사용 대기(초), 이동 속도 배율, 적이 이 거리 안에 오면 자동 노출(m).</summary>
    public FP StealthSeconds;
    public FP StealthCooldownSeconds;
    public FP StealthSpeedMultiplier = 1;
    public FP StealthExposeRadius;
    /// <summary>채굴 교란 장치: 설치 가능 거리(m), 설치 시간·지속(초), 장치 체력, 노드 채집 속도 배율.</summary>
    public FP JammerRange;
    public FP JammerSetupSeconds;
    public FP JammerSeconds;
    public FP JammerHealth;
    public FP JammerGatherMultiplier = 1;
    /// <summary>다른 유닛의 은신·숨긴 장치를 드러내는 반경(m). 정찰 버기·척후 오토바이 10. 0이면 없음.</summary>
    public FP DetectRadius;

    // ---- 항목 34-D 작전기(SortieKind가 None이 아니면 유닛이 아니라 비행장 격납 칸에 대기하는 작전기. 전부 데이터) ----
    public SortieKind SortieKind;
    /// <summary>비행 속도(m/s)와 체력(대공 격추 판정). 재정비 시간(초).</summary>
    public FP SortieSpeed;
    public FP SortieHealth;
    public FP SortieRearmSeconds;
    /// <summary>진입 거리 Lin, 이탈 거리 Lout, 귀환 선회 반경(m).</summary>
    public FP SortieApproach;
    public FP SortieExit;
    public FP SortieTurn;
    /// <summary>기총소사 통로 폭 / 폭탄·낙하 반경(m).</summary>
    public FP SortieWidth;
    /// <summary>기총소사 사격 간격(틱), 폭탄 수와 투하 간격(경로 거리 m), 건물 피해 배율.</summary>
    public int SortieTicks;
    public int SortieBombs;
    public FP SortieBombGap;
    public FP SortieBuildingMultiplier = 1;
    /// <summary>출격마다 내는 비용.</summary>
    public ResourceCost SortieCost;
    /// <summary>공수 투하로 내려놓는 유닛 종류와 수.</summary>
    public AssetRef<UnitSpec> SortieCargo;
    public int SortieCargoCount;

    // ---- 플레이어 조종 수송 탑승(APC · 수륙양용 상륙정 · 수송헬기) ----
    /// <summary>수송 슬롯 용량(APC 6 보병, 상륙정 6 보병 또는 전차 2대, 수송헬기 4 보병). 0이면 수송 불가.</summary>
    public int TransportCapacity;
    /// <summary>true면 보병 외에 지상 차량/전차(3슬롯)도 탑승 가능(수륙양용 상륙정).</summary>
    public bool TransportAllowsVehicles;
    /// <summary>수송기 파괴 시 승객 탈출 체력 비율(지상 APC 0.40 = 40% 체력 생존 탈출, 공중·해상은 0 = 전원 손실).</summary>
    public FP TransportEjectHealthRatio;
  }
}
