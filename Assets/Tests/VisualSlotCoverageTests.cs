namespace FactoryWars.Tests {
  using System.Collections.Generic;
  using System.Linq;
  using FactoryWars.View;
  using NUnit.Framework;
  using Quantum;
  using UnityEngine;
  using Assert = NUnit.Framework.Assert;

  /// <summary>
  /// 2026-11-24 (AGY fix A): every unit spec must show its own model. A unit listed in ViewCatalog.TemporaryModels borrows another
  /// unit's model unless its VisualCatalog slot unit/&lt;id&gt; is filled, so an empty slot there = "falls back to a different unit".
  /// </summary>
  public class VisualSlotCoverageTests {
    static VisualCatalog Catalog => Resources.Load<VisualCatalog>(VisualCatalog.ResourcePath);

    static bool Filled(string id, out GameObject prefab) {
      var slot = Catalog.Slots.FirstOrDefault(s => s != null && s.Id == id);
      prefab = slot != null && slot.Prefabs != null ? slot.Prefabs.FirstOrDefault(p => p != null) : null;
      return prefab != null;
    }

    [Test]
    public void LogisticsTruck_IsSyntyCanopyTruck() {
      Assert.IsTrue(Filled("unit/logistics_truck", out var prefab), "unit/logistics_truck slot is filled");
      var parts = prefab.GetComponentsInChildren<Transform>(true).Select(t => t.name).ToList();
      Assert.IsTrue(parts.Any(n => n.Contains("SM_Veh_Truck_01_Canopy")), "the canopy: " + string.Join(", ", parts.Take(8)));
      Assert.IsTrue(parts.Any(n => n.EndsWith("_SM_Veh_Truck_01")), "the chassis under the canopy: " + string.Join(", ", parts.Take(8)));
      Assert.IsFalse(parts.Any(n => n.Contains("Rocket")), "never the rocket truck");
      Assert.IsNotNull(prefab.transform.Find("Cargo"), "cargo crate group");
      Assert.IsFalse(prefab.transform.Find("Cargo").gameObject.activeSelf, "crates hidden while empty");
      var slot = Catalog.Slots.First(s => s != null && s.Id == "unit/logistics_truck");
      Assert.IsNotNull(slot.WreckPrefab, "destroyed state");
      var wreckParts = slot.WreckPrefab.GetComponentsInChildren<Transform>(true).Select(t => t.name).ToList();
      Assert.IsTrue(wreckParts.Any(n => n.Contains("Canopy_Destroyed")) && wreckParts.Any(n => n.EndsWith("SM_Veh_Truck_01_Destroyed")), string.Join(", ", wreckParts.Take(6)));
      Assert.IsTrue(Filled("unit/merc_cargo_truck", out var hauler) && hauler != prefab, "the PMC cargo truck keeps its own model");
    }

    [Test]
    public void Units_NeverFallBackToAnotherUnitsModel() {
      var missing = new List<string>();
      foreach (var (id, borrow, _) in ViewCatalog.TemporaryModels) {
        if (Resources.Load<UnitSpec>("FactoryWars/" + id) == null) continue; // buildings and non-unit ids
        if (Filled(VisualCatalog.UnitId(id), out _) == false) missing.Add($"{id} -> borrows {borrow}");
      }
      Debug.Log("[FW-Slots] units falling back: " + (missing.Count == 0 ? "none" : string.Join("; ", missing)));
      Assert.IsEmpty(missing, "units whose slot is empty and that show another unit's model");
    }
  }
}
