namespace Quantum {
  public enum UnitSize { Engineer, Small, Medium, Large }

  /// <summary>항목 34-D 작전기 종류: 기총소사·폭격·공수 투하. None이면 일반 유닛.</summary>
  public enum SortieKind { None, Strafe, Bomb, Drop }

  public enum BuildingRole {
    Headquarters, PowerPlant, Processor, SupplyDepot, Barracks, ResearchLab, VehicleFactory, Airfield, HeavyWorks, GroundTower, CollectionYard, AirDefense, Shipyard,
    // 항목 32
    Sandbags, Bunker, MedicTent, RepairBay, Radar, Helipad, Artillery,
    // 항목 33: 궁극 전력 전용 건물(항모 조립창·미사일 격납고·강습 도크).
    UltimateSite,
    // 패트리어트 대탄도 요격 포대(T2.5: 전략 미사일 공중 요격 + 대공·항모 저격).
    PatriotTurret,
    // 자원 노드 직결 자동 채굴 공장(공병 0~3명 주둔 배치).
    LumberMill,
    IronExtractor
  }

  /// <summary>항목 32: 건물이 주변 유닛을 고치는 대상.</summary>
  public enum HealTarget { None, Infantry, Vehicles }

  /// <summary>ActionRejected.Missing의 비트.</summary>
  [System.Flags]
  public enum MissingResource { None = 0, Iron = 1, Wood = 2, Steel = 4, Power = 8, Population = 16 }

  /// <summary>건물이 언제 유지비를 내는가. 값이 있으면 정전 때 멈춘다.</summary>
  public enum UpkeepMode { None, WhileProducing, WhileResearching, Always, WhileUltimate }

  public enum ExpansionKind { IronForecourt, WoodForecourt, Shared, Center }

  public enum TerrainKind { Rock, Hill, CanyonWall, Cliff, River }

  public enum AttackType { None, AntiInfantry, AntiArmor, AntiAir, Area }

  public enum SpecialAbility {
    None,
    EngineerWork,
    TargetDesignator,
    AirPriority,
    FirstShotDouble,
    GroundArmorResist,
    CarpetBomb,
    MilitiaPair,
    FlameBurst,
    SwarmSpeed,
    MultiAirTarget,
    LongRangeBarrage,
    /// <summary>항목 31 야전 정비차: 주변 아군 지상 차량 수리.</summary>
    FieldRepair,
    /// <summary>대전차 로켓병: 전차·차량 대상 +60% 추가 피해.</summary>
    AntiArmorBonus,
    /// <summary>화염 돌격병: 엄폐·주둔 무시 및 주둔 보병·건물 대상 +50% 추가 피해.</summary>
    GarrisonBreach,
    /// <summary>다연장 로켓포차: 4발 연속 스플래시 로켓 일제 사격.</summary>
    RocketSalvo,
    /// <summary>기동 대공차: 이동 사격 추적 및 공중 항모 대상 +50% 추가 대공 피해.</summary>
    CarrierHunter
  }

  /// <summary>연구 종류(항목 31). 공격·방어는 크기별 단계(Mk.2/Mk.3 진화), 정예화는 진영의 소형 1종 강화, Special은 진영 특화 업그레이드.</summary>
  public enum ResearchKind { Attack, Armor, Elite, Ultimate, Special }

  /// <summary>진영 특화 업그레이드 효과.</summary>
  public enum FactionSpecial { None, RapidCycleBolts, StealthCoating, ReactiveArmorEra, HighExplosiveAp, RapidDeployment, TacticalSmoke }

  /// <summary>궁극 전력을 시작하지 못하는 이유(항목 33). 여러 개가 함께 올 수 있다.</summary>
  [System.Flags]
  public enum UltimateLock { None = 0, Tier = 1, Site = 2, Research = 4, Resources = 8, Busy = 16, Cooldown = 32, NoSpec = 64 }

  public enum ModuleEffect { None, PiercingRounds, ReactiveArmor }

  public enum DiplomacyAction { Propose, Accept, Reject, Break }

  public enum NoteTarget { Player, Allies, Everyone }
}
