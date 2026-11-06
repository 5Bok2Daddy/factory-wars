namespace Quantum {
  public partial class RuntimeConfig {
    public AssetRef<GameRulesSpec> Rules;
    public bool TeamMode;
    public bool JointVictory = true;
  }

  public partial class RuntimePlayer {
    public AssetRef<FactionSpec> Faction;
    public int Team;
  }
}
