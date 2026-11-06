namespace Quantum {
  public enum UnitSize { Engineer, Small, Medium, Large }

  public enum BuildingRole {
    Headquarters, PowerPlant, Refinery, SupplyDepot, Barracks, ResearchLab, VehicleFactory, Airfield, HeavyWorks, DefenseTower
  }

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
