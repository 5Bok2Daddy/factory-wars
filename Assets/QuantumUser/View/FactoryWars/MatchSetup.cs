namespace FactoryWars.View {
  using System.Linq;
  using Quantum;

  public enum MatchMode { Duel, FreeForAll, Team }

  /// <summary>시작 화면에서 고른 경기 조건. 맵은 언제나 4인 맵 하나다(항목 17B).</summary>
  public class MatchSettings {
    public int FactionIndex;
    public int Bots = 1;
    public BotDifficulty Difficulty = BotDifficulty.Normal;
    public MatchMode Mode = MatchMode.Duel;
    public int TruceMinutes = 7;
    /// <summary>고정 시드(음수면 무작위). 개발용 "대규모 교전 장면"이 쓴다.</summary>
    public int Seed = -1;
    /// <summary>시작 뒤 이 시간(초)까지 빨리 감는다. 0이면 안 한다.</summary>
    public int FastForwardSeconds;
    /// <summary>19A 비교 환경의 고정 장면과 같은 시드.</summary>
    public const int BattleSceneSeed = 7020;
    /// <summary>2대2 동맹 배치. 기본은 대각선(0·2 대 1·3)이고, 끄면 이웃(0·1 대 2·3)이다.</summary>
    public bool DiagonalTeams = true;
    /// <summary>고른 색. 음수면 진영 기본 추천색이다.</summary>
    public int ColorIndex = -1;
    /// <summary>온라인 6자리 방 코드 대전(항목 28).</summary>
    public bool IsOnline;
    public string RoomCode = "";

    public int Color => ColorIndex >= 0 ? ColorIndex : Palette.Recommended(FactionIndex);

    public string Scene => "Map4P";

    public int Players => Bots + 1;

    /// <summary>모드·봇 수의 조합을 유효한 값으로 맞춘다. 1대1은 2명, 자유 대전은 3~4명, 팀전은 4명이다.</summary>
    public void Normalize() {
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

      int Team(int slot) => settings.Mode == MatchMode.Team ? (settings.DiagonalTeams ? slot % 2 : slot / 2) : slot;
      AssetRef<FactionSpec> Faction(int slot) => factions[(settings.FactionIndex + slot) % factions.Length];
      // AI는 사람이 고른 색을 피해 진영 추천색부터 남은 색을 쓴다.
      var used = new System.Collections.Generic.HashSet<int> { settings.Color };
      int BotColor(int slot) {
        var color = Palette.Recommended((settings.FactionIndex + slot) % factions.Length);
        while (used.Add(color) == false) {
          color = (color + 1) % Palette.Count;
        }
        return color;
      }
      config.Bots = Enumerable.Range(1, settings.Bots).Select(slot => new BotSlot {
        Player = slot, Faction = Faction(slot), Team = Team(slot), Difficulty = settings.Difficulty, ColorIndex = BotColor(slot),
      }).ToArray();
      runner.LocalPlayers = new[] { new RuntimePlayer { PlayerNickname = "지휘관", Faction = Faction(0), Team = Team(0), ColorIndex = settings.Color } };
      runner.MaxPlayerCount = settings.Players;
      runner.UseRandomSeed = settings.Seed < 0;
      if (settings.Seed >= 0) {
        config.Seed = settings.Seed;
      }
    }
  }
}
