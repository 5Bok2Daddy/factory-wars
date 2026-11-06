namespace Quantum {
  using Photon.Deterministic;

  public partial class ModuleSpec : AssetObject {
    public ModuleEffect Effect;
    public FP Cost;
    /// <summary>피해 배율. 관통탄은 가하는 피해, 반응장갑은 받는 피해에 곱한다.</summary>
    public FP DamageMultiplier;
    public FP CooldownSeconds;
  }
}
