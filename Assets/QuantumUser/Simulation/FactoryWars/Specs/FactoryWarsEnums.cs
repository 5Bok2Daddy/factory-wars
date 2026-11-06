namespace Quantum {
  public enum UnitSize { Engineer, Small, Medium, Large }

  public enum BuildingRole {
    Headquarters, PowerPlant, Processor, SupplyDepot, Barracks, ResearchLab, VehicleFactory, Airfield, HeavyWorks, DefenseTower, CollectionYard
  }

  /// <summary>건물이 언제 유지비를 내는가. 값이 있으면 정전 때 멈춘다.</summary>
  public enum UpkeepMode { None, WhileProducing, WhileResearching, Always }

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
    LongRangeBarrage
  }

  public enum ModuleEffect { None, PiercingRounds, ReactiveArmor }

  public enum DiplomacyAction { Propose, Accept, Reject, Break }

  public enum NoteTarget { Player, Allies, Everyone }
}
