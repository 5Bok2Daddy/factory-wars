namespace Quantum {
  using Photon.Deterministic;

  /// <summary>
  /// Task 46: outer naval waterway. Per player sector a barrier boom across the waterway (MapSpec.SeaGates) with a watchtower and
  /// a sentry, a gate that opens for the owner's ships, and shipyard zones (MapSpec.ShipyardZones). All numbers are data.
  /// </summary>
  public partial class WaterwaySpec : AssetObject {
    /// <summary>Water NavMesh region name prefix; region "{SeaGateRegion}{gate}" is the passage under barrier gate i.</summary>
    public string SeaGateRegion = "SeaGate";
    /// <summary>An owner ship must wait this long inside the trigger zone before the gate opens.</summary>
    public FP OpenSeconds = 2;
    /// <summary>The gate takes this long to close after the last owner ship left the trigger zone; the passage stays open meanwhile.</summary>
    public FP CloseSeconds = 10;
    public AssetRef<BuildingSpec> Barrier;
    public AssetRef<BuildingSpec> Tower;
    /// <summary>After a hit on the barrier or the tower, a new alert fires only after this much quiet time (one alert per attack window).</summary>
    public FP AlertQuietSeconds = 12;
    /// <summary>Shipyards in a small-vessel zone (canal banks) cannot build ships with a hull taller than this.</summary>
    public FP SmallVesselMaxHull = 3;
  }
}
