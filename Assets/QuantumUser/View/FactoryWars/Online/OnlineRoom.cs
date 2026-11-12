namespace FactoryWars.View {
  using System;
  using System.Collections.Generic;
  using System.Linq;
  using System.Threading.Tasks;
  using Photon.Client;
  using Photon.Deterministic;
  using Photon.Realtime;
  using Quantum;
  using UnityEngine;
  using UnityEngine.SceneManagement;

  public enum RoomStage { Offline, Connecting, Lobby, Starting, InGame, Reconnecting, Failed }

  /// <summary>대기실 한 줄(사람 또는 봇).</summary>
  public struct LobbyEntry {
    public bool IsBot;
    public int ActorNumber;
    public string Name;
    public int Faction;
    public int Color;
    public bool Ready;
    public bool IsHost;
    public bool IsLocal;
    public BotDifficulty Difficulty;
  }

  /// <summary>
  /// Photon Realtime 방(항목 30). 6자리 코드로 만들고 들어가며, 대기실 상태는 방·플레이어 속성에 둔다.
  /// 방장이 시작하면 모두 같은 방 속성으로 같은 RuntimeConfig를 만들어 Quantum 온라인 세션을 연다.
  /// - 방 속성: v 버전, c 코드, m 모드, t 휴전(분), b 봇 난이도 배열, go 시작, s 시드, bf·bc 봇 진영·색(시작 때 방장이 확정)
  /// - 플레이어 속성: f 진영, col 색, r 준비
  /// 화면 쪽 객체라 정적 인스턴스를 쓴다(시뮬레이션 상태가 아님).
  /// </summary>
  public class OnlineRoom : MonoBehaviour {
    public const string PluginName = "QuantumPlugin";
    public const string RoomPrefix = "FW-";
    public const int MaxRoomPlayers = 4;
    /// <summary>끊긴 사람이 방에 다시 들어올 수 있는 시간. 시뮬레이션의 기권 시간(규칙 60초)과 같게 둔다.</summary>
    public const int PlayerTtlSeconds = 60;
    public const int EmptyRoomTtlSeconds = 10;
    /// <summary>
    /// 방 코드는 한 지역 서버 안에서만 찾을 수 있어서 모두 같은 지역에 붙는다. 서울(kr). PhotonServerSettings에 고정 지역이 있으면 그것을 쓴다.
    /// </summary>
    public const string DefaultRegion = "kr";

    public const string KeyVersion = "v";
    public const string KeyCode = "c";
    public const string KeyMode = "m";
    public const string KeyTruce = "t";
    public const string KeyBots = "b";
    public const string KeyStart = "go";
    public const string KeySeed = "s";
    public const string KeyBotFactions = "bf";
    public const string KeyBotColors = "bc";
    public const string KeyFaction = "f";
    public const string KeyColor = "col";
    public const string KeyReady = "r";

    public static OnlineRoom Instance { get; private set; }

    public RealtimeClient Client { get; private set; }
    public RoomStage Stage { get; private set; } = RoomStage.Offline;
    public string Code { get; private set; } = "";
    public string Error { get; private set; } = "";
    public string UserId { get; private set; }
    /// <summary>시작한 세션의 러너. 경기 화면이 쓴다.</summary>
    public QuantumRunner Runner { get; private set; }
    /// <summary>false면 시작 신호에 경기 씬을 열지 않는다(한 프로세스에 클라이언트 둘을 띄우는 시험용).</summary>
    public bool AutoLoadScene = true;
    /// <summary>시작 신호를 받았는지(시험용).</summary>
    public bool StartReceived { get; private set; }
    public bool IsHost => Client != null && Client.InRoom && Client.LocalPlayer.IsMasterClient;
    public int Ping => Client != null && Client.InRoom ? (int)Client.RealtimePeer.Stats.RoundtripTime : -1;

    MatchmakingReconnectInformation _reconnect = new MatchmakingReconnectInformation();
    IDisposable _pluginDisconnect;
    string _lastFailure;

    /// <summary>시험용: 싱글턴이 아닌 두 번째 클라이언트.</summary>
    public static OnlineRoom CreateExtra(string name) {
      var item = new GameObject(name);
      DontDestroyOnLoad(item);
      var room = item.AddComponent<OnlineRoom>();
      room.AutoLoadScene = false;
      return room;
    }

    public static OnlineRoom Ensure() {
      if (Instance == null) {
        var item = new GameObject("OnlineRoom");
        DontDestroyOnLoad(item);
        Instance = item.AddComponent<OnlineRoom>();
      }
      return Instance;
    }

    void Update() {
      // 세션 전에는 직접 서비스한다. 세션이 열리면 Quantum 통신기가 맡는다.
      if (Client != null && Runner == null && Stage != RoomStage.Connecting && Stage != RoomStage.Reconnecting) {
        Client.Service();
      }
      if (Stage == RoomStage.Lobby && Client != null && Client.InRoom == false) {
        Fail("방과 연결이 끊어졌습니다");
        return;
      }
      if (Stage == RoomStage.Lobby && RoomBool(KeyStart)) {
        Stage = RoomStage.Starting;
        StartReceived = true;
        if (AutoLoadScene) {
          MatchSetup.Pending = LocalSettings();
          SceneManager.LoadScene(MatchSetup.Pending.Scene);
        }
      }
    }

    // ---------- 방 ----------

    MatchmakingArguments Arguments(string code, bool canOnlyJoin) {
      UserId ??= Guid.NewGuid().ToString();
      var settings = new AppSettings(PhotonServerSettings.Global.AppSettings);
      // 같은 게임 버전끼리만 같은 서버 영역을 쓴다(다른 버전은 방 이름이 같아도 못 만난다).
      settings.AppVersion = BuildVersion.Current;
      if (string.IsNullOrEmpty(settings.FixedRegion)) {
        settings.FixedRegion = DefaultRegion;
      }
      return new MatchmakingArguments {
        PhotonSettings = settings,
        PluginName = PluginName,
        MaxPlayers = MaxRoomPlayers,
        RoomName = RoomPrefix + code,
        CanOnlyJoin = canOnlyJoin,
        PlayerTtlInSeconds = PlayerTtlSeconds,
        EmptyRoomTtlInSeconds = EmptyRoomTtlSeconds,
        UserId = UserId,
        ReconnectInformation = _reconnect,
        CustomProperties = canOnlyJoin ? null : new PhotonHashtable {
          { KeyVersion, BuildVersion.Current }, { KeyCode, code }, { KeyMode, (int)MatchMode.Duel }, { KeyTruce, 7 },
          { KeyBots, new int[0] }, { KeyStart, false },
        },
      };
    }

    /// <summary>새 방을 만든다. 같은 코드의 방이 이미 있으면 다른 코드로 다시 한다.</summary>
    public async Task<bool> Create(MatchSettings local) {
      for (var attempt = 0; attempt < 3; attempt++) {
        var code = OnlineMatchManager.GenerateRoomCode();
        if (await Enter(code, false, local) == false) {
          return false;
        }
        if (Client.CurrentRoom.PlayerCount == 1 && IsHost) {
          return true;
        }
        await Leave();
      }
      Fail("방을 만들지 못했습니다");
      return false;
    }

    public Task<bool> Join(string code, MatchSettings local) {
      code = (code ?? "").Trim().ToUpperInvariant();
      if (OnlineMatchManager.ValidateRoomCode(code) == false) {
        Fail("방 코드는 6자리입니다(0·O·1·I 제외)");
        return Task.FromResult(false);
      }
      return Enter(code, true, local);
    }

    async Task<bool> Enter(string code, bool join, MatchSettings local) {
      Error = "";
      Stage = RoomStage.Connecting;
      Code = code;
      try {
        Client = await MatchmakingExtensions.ConnectToRoomAsync(Arguments(code, join));
      } catch (Exception e) {
        Client = null;
        Fail(join ? $"방에 들어가지 못했습니다: {Reason(e)}" : $"연결하지 못했습니다: {Reason(e)}");
        return false;
      }
      // 버전 확인: 다르면 바로 나간다(같은 AppVersion이라 보통 만나지 않지만, 데이터만 바뀐 경우 방 속성으로 한 번 더 막는다).
      var version = RoomString(KeyVersion);
      if (BuildVersion.Compatible(version) == false) {
        await Leave();
        Fail($"게임 버전이 다릅니다(방 {version}, 내 {BuildVersion.Current}). 같은 버전으로 업데이트하세요");
        return false;
      }
      if (RoomBool(KeyStart)) {
        await Leave();
        Fail("이미 시작한 방입니다");
        return false;
      }
      SetMine(local.FactionIndex, local.Color, false);
      // 자동 이름만 쓴다(자유 닉네임 없음, 로드맵 챕터 4).
      Client.NickName = $"지휘관 {UnityEngine.Random.Range(1000, 10000)}";
      Stage = RoomStage.Lobby;
      return true;
    }

    static string Reason(Exception e) {
      var message = e.InnerException?.Message ?? e.Message;
      if (message.Contains("32758") || message.Contains("GameDoesNotExist")) {
        return "그 코드의 방이 없습니다";
      }
      if (message.Contains("32765") || message.Contains("GameFull")) {
        return "방이 가득 찼습니다";
      }
      if (message.Contains("32764") || message.Contains("GameClosed")) {
        return "이미 시작한 방입니다";
      }
      return message;
    }

    void Fail(string message) {
      Error = message;
      _lastFailure = message;
      Stage = RoomStage.Failed;
      Debug.LogWarning($"[FW-Online] {message}");
    }

    public async Task Leave() {
      _pluginDisconnect?.Dispose();
      _pluginDisconnect = null;
      var runner = Runner;
      Runner = null;
      if (runner != null) {
        try {
          await runner.ShutdownAsync();
        } catch (Exception e) {
          Debug.LogWarning($"[FW-Online] 러너 종료: {e.Message}");
        }
      }
      var client = Client;
      Client = null;
      if (client != null) {
        try {
          await client.DisconnectAsync();
        } catch (Exception e) {
          Debug.LogWarning($"[FW-Online] 연결 종료: {e.Message}");
        }
      }
      Stage = RoomStage.Offline;
      Code = "";
    }

    // ---------- 대기실 ----------

    public PhotonHashtable RoomProps => Client?.CurrentRoom?.CustomProperties ?? new PhotonHashtable();

    string RoomString(string key) => RoomProps.TryGetValue(key, out var value) && value is string text ? text : "";
    bool RoomBool(string key) => RoomProps.TryGetValue(key, out var value) && value is bool flag && flag;
    int RoomInt(string key, int fallback) => RoomProps.TryGetValue(key, out var value) && value is int number ? number : fallback;
    int[] RoomInts(string key) => RoomProps.TryGetValue(key, out var value) && value is int[] numbers ? numbers : new int[0];

    static int PlayerInt(Player player, string key, int fallback) => player.CustomProperties.TryGetValue(key, out var value) && value is int number ? number : fallback;
    static bool PlayerBool(Player player, string key) => player.CustomProperties.TryGetValue(key, out var value) && value is bool flag && flag;

    public MatchMode Mode => (MatchMode)RoomInt(KeyMode, (int)MatchMode.Duel);
    public int TruceMinutes => RoomInt(KeyTruce, 7);
    public int[] Bots => RoomInts(KeyBots);

    public void SetMine(int faction, int color, bool ready) {
      Client?.LocalPlayer.SetCustomProperties(new PhotonHashtable { { KeyFaction, faction }, { KeyColor, color }, { KeyReady, ready } });
    }

    public bool MyReady => Client != null && PlayerBool(Client.LocalPlayer, KeyReady);

    /// <summary>방장만: 모드·휴전·봇 칸.</summary>
    public void HostSet(MatchMode mode, int truceMinutes, int[] bots) {
      if (IsHost == false) {
        return;
      }
      Client.CurrentRoom.SetCustomProperties(new PhotonHashtable { { KeyMode, (int)mode }, { KeyTruce, truceMinutes }, { KeyBots, bots } });
    }

    /// <summary>사람(입장 순) 다음 봇. 화면과 시작 설정이 같은 순서를 쓴다.</summary>
    public List<LobbyEntry> Entries() {
      var list = new List<LobbyEntry>();
      if (Client?.CurrentRoom == null) {
        return list;
      }
      foreach (var player in Client.CurrentRoom.Players.Values.OrderBy(p => p.ActorNumber)) {
        list.Add(new LobbyEntry {
          ActorNumber = player.ActorNumber, Name = string.IsNullOrEmpty(player.NickName) ? $"지휘관 {player.ActorNumber}" : player.NickName,
          Faction = PlayerInt(player, KeyFaction, 0), Color = PlayerInt(player, KeyColor, 0), Ready = PlayerBool(player, KeyReady) || player.IsMasterClient,
          IsHost = player.IsMasterClient, IsLocal = player.IsLocal,
        });
      }
      var bots = Bots;
      for (var i = 0; i < bots.Length; i++) {
        list.Add(new LobbyEntry { IsBot = true, Name = $"AI {i + 1}", Difficulty = (BotDifficulty)bots[i], Faction = (list.Count) % 2, Ready = true });
      }
      return list;
    }

    public int Humans => Client?.CurrentRoom?.PlayerCount ?? 0;
    public int Total => Humans + Bots.Length;

    /// <summary>시작 조건: 방장, 인원이 모드에 맞음(1대1 2명, 자유 3~4명, 2대2 4명), 사람 모두 준비.</summary>
    public string StartBlocker() {
      if (IsHost == false) {
        return "방장만 시작할 수 있습니다";
      }
      var total = Total;
      switch (Mode) {
        case MatchMode.Duel when total != 2: return "1대1은 2명이어야 합니다";
        case MatchMode.FreeForAll when total < 3 || total > 4: return "자유 대전은 3~4명이어야 합니다";
        case MatchMode.Team when total != 4: return "2대2는 4명이어야 합니다";
      }
      if (Entries().Any(e => e.IsBot == false && e.Ready == false)) {
        return "모두 준비해야 합니다";
      }
      return null;
    }

    /// <summary>방장: 시드·봇 진영·색을 확정하고 방을 닫는다. 모든 클라이언트가 이 값으로 같은 설정을 만든다.</summary>
    public bool HostStart() {
      if (StartBlocker() != null) {
        return false;
      }
      var entries = Entries();
      var used = new HashSet<int>(entries.Where(e => e.IsBot == false).Select(e => e.Color));
      var botFactions = new List<int>();
      var botColors = new List<int>();
      var humanFactions = entries.Where(e => e.IsBot == false).Select(e => e.Faction).ToList();
      foreach (var bot in entries.Where(e => e.IsBot)) {
        // 진영 수가 균형이 되게: 지금까지 적은 진영을 고른다.
        var free = humanFactions.Count(f => f == 0) + botFactions.Count(f => f == 0) <= humanFactions.Count(f => f == 1) + botFactions.Count(f => f == 1) ? 0 : 1;
        botFactions.Add(free);
        var color = Palette.Recommended(free);
        while (used.Add(color) == false) {
          color = (color + 1) % Palette.Count;
        }
        botColors.Add(color);
      }
      Client.CurrentRoom.IsOpen = false;
      Client.CurrentRoom.SetCustomProperties(new PhotonHashtable {
        { KeySeed, UnityEngine.Random.Range(1, int.MaxValue) }, { KeyBotFactions, botFactions.ToArray() }, { KeyBotColors, botColors.ToArray() }, { KeyStart, true },
      });
      return true;
    }

    /// <summary>이 기기의 경기 조건(화면 표시용). 실제 설정은 BuildConfig가 방 속성으로 만든다.</summary>
    public MatchSettings LocalSettings() {
      var me = Entries().FirstOrDefault(e => e.IsLocal);
      return new MatchSettings {
        IsOnline = true, RoomCode = Code, Mode = Mode, TruceMinutes = TruceMinutes, Bots = Total - 1, FactionIndex = me.Faction, ColorIndex = me.Color,
      };
    }

    /// <summary>
    /// 방 속성 → RuntimeConfig. 모든 클라이언트가 같은 값을 만든다. 사람은 서버가 0번부터 차례로 PlayerRef를 주고,
    /// 봇은 그 뒤 번호를 쓴다. 팀전은 본진 자리로 팀을 정한다(TeamsBySeat).
    /// </summary>
    public RuntimeConfig BuildConfig(RuntimeConfig template, AssetRef<FactionSpec>[] factions) {
      var config = new QuantumUnityJsonSerializer().CloneConfig(template);
      var humans = Humans;
      var bots = Bots;
      var botFactions = RoomInts(KeyBotFactions);
      var botColors = RoomInts(KeyBotColors);
      config.Seed = RoomInt(KeySeed, 1);
      config.TeamMode = Mode == MatchMode.Team;
      config.TeamsBySeat = config.TeamMode;
      config.JointVictory = Mode == MatchMode.FreeForAll;
      config.OverrideTruce = true;
      config.TruceSeconds = TruceMinutes * 60;
      config.OverrideStartingResources = false;
      config.ScenarioUnits = null;
      config.ScenarioBuildings = null;
      config.Bots = Enumerable.Range(0, bots.Length).Select(i => new BotSlot {
        Player = humans + i, Faction = factions[(i < botFactions.Length ? botFactions[i] : i) % factions.Length], Team = humans + i,
        Difficulty = (BotDifficulty)bots[i], ColorIndex = i < botColors.Length ? botColors[i] : i,
      }).ToArray();
      return config;
    }

    /// <summary>
    /// Quantum 온라인 세션을 연다. 경기 씬의 로컬 러너 설정(맵·규칙)을 본으로 쓴다.
    /// 개발 빌드는 체크섬을 켜서 디싱크를 잡는다(DesyncWatcher가 덤프를 남긴다).
    /// </summary>
    public async Task<bool> StartSession(RuntimeConfig template, AssetRef<FactionSpec>[] factions) {
      var me = Entries().FirstOrDefault(e => e.IsLocal);
      var config = BuildConfig(template, factions);
      var session = QuantumDeterministicSessionConfigAsset.DefaultConfig;
      if (Debug.isDebugBuild || Application.isEditor) {
        session = CloneSession(session);
        session.ChecksumInterval = 60;
      }
      var arguments = new SessionRunner.Arguments {
        RunnerFactory = QuantumRunnerUnityFactory.DefaultFactory,
        GameParameters = QuantumRunnerUnityFactory.CreateGameParameters,
        Communicator = new QuantumNetworkCommunicator(Client),
        GameMode = DeterministicGameMode.Multiplayer,
        PlayerCount = Total,
        ClientId = UserId,
        RuntimeConfig = config,
        SessionConfig = session,
      };
      _pluginDisconnect = QuantumCallback.SubscribeManual<CallbackPluginDisconnect>(m => {
        Debug.LogWarning($"[FW-Online] 서버가 연결을 끊음: {m.Reason}");
        Error = "서버와 연결이 끊어졌습니다";
      });
      try {
        using (new ConnectionServiceScope(Client)) {
          Runner = (QuantumRunner)await SessionRunner.StartAsync(arguments);
        }
      } catch (Exception e) {
        Fail($"경기를 시작하지 못했습니다: {e.Message}");
        return false;
      }
      Runner.Game.AddPlayer(0, new RuntimePlayer {
        PlayerNickname = Client.NickName, Faction = factions[me.Faction % factions.Length], Team = me.ActorNumber, ColorIndex = me.Color,
      });
      Stage = RoomStage.InGame;
      DesyncWatcher.Ensure();
      return true;
    }

    static DeterministicSessionConfig CloneSession(DeterministicSessionConfig source) {
      return JsonUtility.FromJson<DeterministicSessionConfig>(JsonUtility.ToJson(source));
    }

    /// <summary>앱 전환·네트워크 끊김 뒤 같은 방·같은 PlayerRef로 돌아간다(PlayerTtl 안에서만).</summary>
    public async Task<bool> Reconnect(RuntimeConfig template, AssetRef<FactionSpec>[] factions) {
      if (_reconnect == null || string.IsNullOrEmpty(Code)) {
        return false;
      }
      Stage = RoomStage.Reconnecting;
      try {
        if (Runner != null) {
          await Runner.ShutdownAsync();
          Runner = null;
        }
        Client = await MatchmakingExtensions.ReconnectToRoomAsync(Arguments(Code, true));
      } catch (Exception e) {
        Fail($"재접속 실패: {Reason(e)}");
        return false;
      }
      return await StartSession(template, factions);
    }

    void OnDestroy() {
      if (Instance == this) {
        Instance = null;
      }
      _pluginDisconnect?.Dispose();
    }
  }

  /// <summary>
  /// 디싱크 감지(항목 30). 체크섬 오류가 나면 확정 프레임 덤프를 persistentDataPath/desync에 저장하고 화면에 알린다.
  /// </summary>
  public class DesyncWatcher : MonoBehaviour {
    public static DesyncWatcher Instance { get; private set; }
    public static int Count { get; private set; }
    public static string LastDump { get; private set; }
    IDisposable _subscription;

    public static void Ensure() {
      if (Instance != null) {
        return;
      }
      var item = new GameObject("DesyncWatcher");
      DontDestroyOnLoad(item);
      Instance = item.AddComponent<DesyncWatcher>();
      Instance._subscription = QuantumCallback.SubscribeManual<CallbackChecksumError>(e => Instance.Save(e.Game, e.Frames));
    }

    void Save(QuantumGame game, Frame[] frames) {
      Count++;
      try {
        var folder = System.IO.Path.Combine(Application.persistentDataPath, "desync");
        System.IO.Directory.CreateDirectory(folder);
        var frame = frames != null && frames.Length > 0 ? frames[0] : game.Frames.Verified;
        var path = System.IO.Path.Combine(folder, $"desync-{DateTime.Now:yyyyMMdd-HHmmss}-f{frame.Number}.txt");
        System.IO.File.WriteAllText(path, frame.DumpFrame(0));
        LastDump = path;
        Debug.LogError($"[FW-Online] 디싱크(체크섬 불일치) 프레임 {frame.Number}, 덤프: {path}");
      } catch (Exception e) {
        Debug.LogError($"[FW-Online] 디싱크 덤프 실패: {e.Message}");
      }
    }

    void OnDestroy() {
      _subscription?.Dispose();
      if (Instance == this) {
        Instance = null;
      }
    }
  }
}
