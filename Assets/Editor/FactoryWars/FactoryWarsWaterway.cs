namespace FactoryWars.EditorTools {
  using Photon.Deterministic;
  using Quantum;
  using UnityEditor;
  using UnityEngine;

  /// <summary>Task 46: naval waterway data (WaterwaySpec, sea barrier, watchtower) and the Map4P v6 bake.</summary>
  public static partial class FactoryWarsSetup {
    static BuildingSpec CloneBuilding(string from, string to, System.Action<BuildingSpec> edit) {
      var source = AssetDatabase.LoadAssetAtPath<BuildingSpec>($"{DataRoot}/{from}.asset");
      var spec = Asset<BuildingSpec>(to);
      var id = spec.Identifier;
      JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(source), spec);
      spec.Identifier = id;
      spec.name = to;
      edit?.Invoke(spec);
      EditorUtility.SetDirty(spec);
      return spec;
    }

    /// <summary>Task 46 data. Barrier HP: two artillery ships (90 damage every 4 s each, x1.0 vs buildings = 45/s) need about 53 s.</summary>
    [MenuItem("Tools/Factory Wars/Task 46/Apply waterway data")]
    public static void ApplyTask46Data() {
      var rules = AssetDatabase.LoadAssetAtPath<GameRulesSpec>($"{DataRoot}/game_rules.asset");
      var barrier = CloneBuilding("ff_ground_tower", "sea_barrier", b => {
        b.Role = BuildingRole.SeaBarrier;
        b.Cost = R(300, 0, 120);
        b.BuildTimeSeconds = 40;
        b.MaxHealth = 2400;
        // 12 m square: the boom spans the 16 m waterway; ground units on the coast strip reach it.
        b.FootprintRadius = 6;
        b.SightRange = 6;
        b.PowerUpkeep = 0;
        b.PowerOutput = 0;
        b.ModuleSlots = 0;
        b.Producible = new AssetRef<UnitSpec>[0];
        b.Prerequisites = new AssetRef<BuildingSpec>[0];
        b.Defensive = false;
        b.Attack = AttackType.None;
        b.Damage = 0;
        b.Range = 0;
        b.IsCoreBuilding = false;
        b.IsDropOff = false;
        b.RequiredTier = 1;
      });
      var tower = CloneBuilding("ff_ground_tower", "sea_watchtower", b => {
        b.Role = BuildingRole.Watchtower;
        b.SightRange = 32;
        b.PowerUpkeep = 0;
        b.ModuleSlots = 0;
        b.Prerequisites = new AssetRef<BuildingSpec>[0];
        b.IsCoreBuilding = false;
        b.IsDropOff = false;
        b.RequiredTier = 1;
      });
      var spec = Asset<WaterwaySpec>("waterway");
      spec.SeaGateRegion = SeaGateRegion;
      spec.OpenSeconds = 2;
      spec.CloseSeconds = 10;
      spec.Barrier = Ref(barrier);
      spec.Tower = Ref(tower);
      spec.AlertQuietSeconds = 12;
      // Scout boat 1.6 m and landing craft 2.8 m are small vessels; the artillery ship (6.5 m) is not.
      spec.SmallVesselMaxHull = 3;
      EditorUtility.SetDirty(spec);
      rules.Waterway = Ref(spec);
      EditorUtility.SetDirty(rules);
      AssetDatabase.SaveAssets();
      Debug.Log("[FW-T46] waterway data applied");
    }

    /// <summary>Task 46: data + Map4P v7 (four-side waterway) bake in one batch call.</summary>
    public static void BakeTask46() {
      ApplyTask46Data();
      WaterwayKit.Build();
      BakeMap4P();
      Debug.Log("[FW-T46] Map4P v7 baked");
    }
  }
}
