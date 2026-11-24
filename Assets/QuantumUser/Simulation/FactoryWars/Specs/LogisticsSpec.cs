namespace Quantum {
  using Photon.Deterministic;

  /// <summary>
  /// Task 41: expansion-site logistics. Used on maps with MapSpec.LogisticsEconomy (GameRulesSpec.Logistics). Only the edge
  /// expansion sites (ExpansionKind.Shared) get a yard; base extraction and starting resources are unchanged.
  /// </summary>
  public partial class LogisticsSpec : AssetObject {
    /// <summary>Drop-off buildings within this radius of a site deliver into the site buffer.</summary>
    public FP SiteRadius = 44;
    /// <summary>Buffer capacity (iron + wood) per site; workers wait at the drop-off when it is full.</summary>
    public FP BufferCapacity = 600;
    /// <summary>One truck load.</summary>
    public FP ShipmentSize = 200;
    public FP LoadSeconds = 6;
    public FP UnloadSeconds = 5;
    /// <summary>The truck waits in the yard until this much is buffered (or the buffer is full).</summary>
    public FP MinLoad = 60;
    public AssetRef<UnitSpec> Truck;
    public FP RespawnSeconds = 30;
    /// <summary>Arrival radius at the yard bay and the dock bay.</summary>
    public FP ArriveRadius = 6;
    /// <summary>Dock bay: from the seat's start position toward the map centre, then sideways per site so trucks never share a bay.</summary>
    public FP DockForward = -20;
    public FP DockSpacing = 7;
    /// <summary>Yard bay: from the site position toward its owner's base (inside the site, off the ring road).</summary>
    public FP YardInset = 10;
    /// <summary>No progress for this long while driving → "waiting for route" and re-order.</summary>
    public FP StuckSeconds = 6;
    /// <summary>Share of the cargo dropped as a lootable crate when a loaded truck is destroyed.</summary>
    public FP CrateShare = FP._0_50;
    /// <summary>Task 46: a visible hostile armed ship whose range + this margin covers the truck's leg makes the truck detour inland.</summary>
    public FP ThreatMargin = 6;
    /// <summary>Task 46: the inland waypoint is never further than this from the threatened point of the leg (keeps detours short).</summary>
    public FP DetourMax = 48;
  }
}
