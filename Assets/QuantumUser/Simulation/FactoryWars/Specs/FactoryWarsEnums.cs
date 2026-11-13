namespace Quantum {
  public enum UnitSize { Engineer, Small, Medium, Large }

  public enum BuildingRole {
    Headquarters, PowerPlant, Processor, SupplyDepot, Barracks, ResearchLab, VehicleFactory, Airfield, HeavyWorks, GroundTower, CollectionYard, AirDefense, Shipyard,
    // 항목 32
    Sandbags, Bunker, MedicTent, RepairBay, Radar, Helipad, Artillery,
    // 항목 33: 궁극 전력 전용 건물(항모 조립창·미사일 격납고·강습 도크).
    UltimateSite
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
    FieldRepair
  }

  /// <summary>연구 종류(항목 31). 공격·방어는 크기별 단계, 정예화는 진영의 소형 1종 강화.</summary>
  public enum ResearchKind { Attack, Armor, Elite, Ultimate }

  /// <summary>궁극 전력을 시작하지 못하는 이유(항목 33). 여러 개가 함께 올 수 있다.</summary>
  [System.Flags]
  public enum UltimateLock { None = 0, Tier = 1, Site = 2, Research = 4, Resources = 8, Busy = 16, Cooldown = 32, NoSpec = 64 }

  public enum ModuleEffect { None, PiercingRounds, ReactiveArmor }

  public enum DiplomacyAction { Propose, Accept, Reject, Break }

  public enum NoteTarget { Player, Allies, Everyone }
}
