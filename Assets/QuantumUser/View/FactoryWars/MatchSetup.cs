namespace FactoryWars.View {
  using System.Linq;
  using Quantum;

  public enum MatchMode { Duel, FreeForAll, Team }

  /// <summary>시작 화면에서 고른 경기 조건.</summary>
  public class MatchSettings {
    public int FactionIndex;
    public bool FourPlayerMap;
    public int Bots = 1;
    public BotDifficulty Difficulty = BotDifficulty.Normal;
    public MatchMode Mode = MatchMode.Duel;
    public int TruceMinutes = 7;

    public string Scene => FourPlayerMap ? "Map4P" : "Map2P";

    public int Players => Bots + 1;

    /// <summary>맵·모드·봇 수의 조합을 유효한 값으로 맞춘다. 2인 맵은 1대1뿐이고 팀전은 4명이다.</summary>
    public void Normalize() {
      if (FourPlayerMap == false) {
        Mode = MatchMode.Duel;
      }
      switch (Mode) {
        case MatchMode.Duel: Bots = 1; break;
        case MatchMode.Team: Bots = 3; break;
        default: Bots = Bots < 2 ? 2 : Bots > 3 ? 3 : Bots; break;
      }
    }
  }

  /// <summary>씬을 넘어 경기 조건을 전한다. 화면 쪽 상태라 정적 변수를 쓴다.</summary>
  public static class MatchSetup {
    public static MatchSettings Pending;

    /// <summary>러너가 시작하기 전에 경기 조건을 넣는다. 사람은 0번 자리, 나머지는 AI다.</summary>
    public static void Apply(QuantumRunnerLocalDebug runner, MatchSettings settings) {
      var factions = runner.LocalPlayers.Take(2).Select(p => p.Faction).ToArray();
      var config = runner.RuntimeConfig;
      config.TeamMode = settings.Mode == MatchMode.Team;
      config.JointVictory = settings.Mode == MatchMode.FreeForAll;
      config.OverrideStartingResources = false;
      config.OverrideTruce = true;
      config.TruceSeconds = settings.TruceMinutes * 60;
      config.ScenarioUnits = null;
      config.ScenarioBuildings = null;

      int Team(int slot) => settings.Mode == MatchMode.Team ? slot / 2 : slot;
      AssetRef<FactionSpec> Faction(int slot) => factions[(settings.FactionIndex + slot) % factions.Length];
      config.Bots = Enumerable.Range(1, settings.Bots).Select(slot => new BotSlot {
        Player = slot, Faction = Faction(slot), Team = Team(slot), Difficulty = settings.Difficulty,
      }).ToArray();
      runner.LocalPlayers = new[] { new RuntimePlayer { PlayerNickname = "지휘관", Faction = Faction(0), Team = Team(0) } };
      runner.MaxPlayerCount = settings.Players;
      runner.UseRandomSeed = true;
    }
  }
}
