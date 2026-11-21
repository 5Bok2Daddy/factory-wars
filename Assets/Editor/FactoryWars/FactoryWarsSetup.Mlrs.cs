namespace FactoryWars.EditorTools {
  using System.Linq;
  using Photon.Deterministic;
  using Quantum;
  using UnityEditor;

  public static partial class FactoryWarsSetup {
    [MenuItem("Tools/Factory Wars/Build MLRS Data")]
    public static void BuildMlrsData() {
      ConfigureMlrs(Asset<FactionSpec>("faction_free_federation"), "ff");
      ConfigureMlrs(Asset<FactionSpec>("faction_continental_federation"), "cf");
      ArtBaker.BakeMlrs();
      AssetDatabase.SaveAssets();
    }

    static void ConfigureMlrs(FactionSpec faction, string prefix) {
      var battery = BuildBuilding(prefix + "_mlrs_battery", R(480, 100, 160, 160), 35, 1400, 0, 0);
      battery.Role = BuildingRole.MlrsBattery;
      battery.Defensive = true;
      battery.RequiredTier = 2;
      battery.FootprintRadius = N(4.6);
      var factory = AssetDatabase.LoadAssetAtPath<BuildingSpec>($"{DataRoot}/{prefix}_vehicle_factory.asset");
      battery.Prerequisites = new[] { Ref(factory) };
      battery.SightRange = 16;
      battery.Upkeep = UpkeepMode.Always;
      battery.PowerUpkeep = N(1.5);
      battery.Attack = AttackType.Area;
      battery.GroundOnly = true;
      battery.Damage = 42;
      battery.Range = 45;
      battery.MinRange = 8;
      battery.SplashRadius = N(3.5);
      battery.AttackInterval = N(0.8);
      battery.RocketCapacity = 12;
      battery.RocketReloadSeconds = 25;
      battery.RocketTierThreeReloadMultiplier = N(0.8);
      battery.RocketAimSeconds = 1;
      battery.RocketSearchSeconds = N(0.25);
      battery.RocketSpeed = 26;
      battery.RocketArc = 7;
      battery.RocketEdgeDamage = N(0.35);
      battery.RocketElevation = 28;
      battery.RocketMuzzles = new FPVector3[battery.RocketCapacity];
      var pitch = battery.RocketElevation * FP.Deg2Rad;
      for (var tube = 0; tube < battery.RocketMuzzles.Length; ++tube) {
        var x = (tube % 2 == 0 ? -0.93 : 0.93) + (tube / 2 % 3 - 1) * 0.33;
        var y = (tube / 6 == 0 ? -0.18 : 0.18);
        battery.RocketMuzzles[tube] = new FPVector3(N(x),
          N(1.65) + N(y) * FPMath.Cos(pitch) + N(1.62) * FPMath.Sin(pitch),
          N(1.62) * FPMath.Cos(pitch) - N(y) * FPMath.Sin(pitch));
      }
      var reference = Ref(battery);
      faction.Buildings = faction.Buildings.Where(item => item != reference && item != faction.Headquarters)
        .Concat(new[] { reference, faction.Headquarters }).ToArray();
      EditorUtility.SetDirty(battery);
      EditorUtility.SetDirty(faction);
    }
  }
}
