namespace Quantum {
  using Photon.Deterministic;

  /// <summary>One shop line of the mercenary camp: a unit (Count of it) or a supply swap (Give, no unit).</summary>
  [System.Serializable]
  public struct MercOffer {
    public string Name;
    public AssetRef<UnitSpec> Unit;
    public int Count;
    public ResourceCost Cost;
    /// <summary>Resources handed over instead of a unit (supply trade).</summary>
    public ResourceCost Give;
    /// <summary>Shared stock across all players; refilled by one every MercenarySpec.RestockSeconds.</summary>
    public int MaxStock;
    /// <summary>Per-player cooldown after buying this line.</summary>
    public FP CooldownSeconds;
  }

  /// <summary>
  /// Task 39 card 3: the neutral mercenary camp in the map centre (extends NpcTraderSystem / Task 38).
  /// Before an agreement the garrison attacks intruders and attackers and returns to its posts once the threat left the leash radius.
  /// Agreement = diplomatic message (reply after MessageDelaySeconds) + an armoured truck delivering Cargo to the camp gate.
  /// With an agreement the player may enter, sees the interior and buys from the shared-stock shop. Attacking the camp revokes it.
  /// </summary>
  public partial class MercenarySpec : AssetObject {
    public FP CampRadius = 50;
    public FP LeashRadius = 72;
    /// <summary>Gate points sit on the four axes at this distance from the centre (outside the wall).</summary>
    public FP GateDistance = 58;
    public FP VisionRadius = 66;

    public int GuardCount = 8;
    public FP GuardPostRadius = 26;
    public FP GuardHealth = 400;
    public FP GuardDamage = 30;
    public FP GuardRange = 16;
    public FP GuardInterval = FP._1;
    public FP GuardSpeed = 5;
    public FP GuardRadius = FP._1;

    public FP MessageDelaySeconds = 8;
    public ResourceCost Cargo;
    public AssetRef<UnitSpec> Truck;
    public FP DeliveryRadius = 10;
    /// <summary>Share of the cargo dropped as a supply crate (anyone can grab) when the truck is destroyed.</summary>
    public FP CargoDropShare = FP._0_50;

    public MercOffer[] Offers;
    public FP RestockSeconds = 90;

    /// <summary>AI: send a message after this many seconds; dispatch when stock covers cargo + reserve; buy when stock covers cost + reserve.</summary>
    public FP AiContactAfterSeconds = 120;
    public ResourceCost AiReserve;
    public int AiEscorts = 2;
  }
}
