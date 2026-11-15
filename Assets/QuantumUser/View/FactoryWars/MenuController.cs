namespace FactoryWars.View {
  using System.Linq;
  using Quantum;
  using UnityEngine;
  using UnityEngine.SceneManagement;
  using UnityEngine.UI;
  using Button = UnityEngine.UI.Button;

  /// <summary>음량 자리와 언어 표시. 음량은 챕터 6에서 소리가 들어오면 쓴다.</summary>
  public static class SettingsPanel {
    const string VolumeKey = "fw_volume";

    public static RectTransform Build(Transform parent, UnityEngine.Events.UnityAction onClose) {
      var panel = Ui.Panel("Settings", parent);
      Ui.Place(panel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900, Debug.isDebugBuild ? 800 : 620));
      panel.pivot = new Vector2(0.5f, 0.5f);
      var title = Ui.Label("Title", panel, "설정", 44, TextAnchor.MiddleCenter);
      Ui.Place(title.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -20), new Vector2(600, 70));
      var volume = Ui.Label("Volume", panel, "", 34, TextAnchor.MiddleCenter);
      Ui.Place(volume.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 90), new Vector2(360, Ui.MinButtonSize));

      void Show() {
        volume.text = $"음량 {Mathf.RoundToInt(AudioListener.volume * 100)}%";
      }
      void Change(float delta) {
        AudioListener.volume = Mathf.Clamp01(AudioListener.volume + delta);
        PlayerPrefs.SetFloat(VolumeKey, AudioListener.volume);
        Show();
      }
      AudioListener.volume = PlayerPrefs.GetFloat(VolumeKey, 1f);
      Show();

      var down = Ui.Button("VolumeDown", panel, "-", () => Change(-0.1f), 44);
      Ui.Place((RectTransform)down.transform, new Vector2(0.5f, 0.5f), new Vector2(-260, 90), new Vector2(Ui.MinButtonSize, Ui.MinButtonSize));
      var up = Ui.Button("VolumeUp", panel, "+", () => Change(0.1f), 44);
      Ui.Place((RectTransform)up.transform, new Vector2(0.5f, 0.5f), new Vector2(260, 90), new Vector2(Ui.MinButtonSize, Ui.MinButtonSize));
      Button quality = null;
      quality = Ui.Button("Quality", panel, $"그래픽: {GraphicsQuality.Name(GraphicsQuality.Current)}", () => {
        Ui.SetLabel(quality, $"그래픽: {GraphicsQuality.Name(GraphicsQuality.Cycle())}");
      }, 34);
      Ui.Place((RectTransform)quality.transform, new Vector2(0.5f, 0.5f), new Vector2(-220, -20), new Vector2(400, Ui.MinButtonSize));
      Button cameraSpeed = null;
      cameraSpeed = Ui.Button("CameraSpeed", panel, PlayerOptions.CameraLabel, () => {
        PlayerOptions.NextCameraSpeed();
        Ui.SetLabel(cameraSpeed, PlayerOptions.CameraLabel);
      }, 30);
      Ui.Place((RectTransform)cameraSpeed.transform, new Vector2(0.5f, 0.5f), new Vector2(220, -20), new Vector2(400, Ui.MinButtonSize));
      Button shake = null;
      shake = Ui.Button("Shake", panel, ScreenShake.Label, () => {
        ScreenShake.Enabled = ScreenShake.Enabled == false;
        Ui.SetLabel(shake, ScreenShake.Label);
      }, 30);
      Ui.Place((RectTransform)shake.transform, new Vector2(0.5f, 0.5f), new Vector2(-220, -110), new Vector2(400, Ui.MinButtonSize));
      Button vibration = null;
      vibration = Ui.Button("Vibration", panel, PlayerOptions.VibrationLabel, () => {
        PlayerOptions.Vibration = PlayerOptions.Vibration == false;
        Ui.SetLabel(vibration, PlayerOptions.VibrationLabel);
      }, 30);
      Ui.Place((RectTransform)vibration.transform, new Vector2(0.5f, 0.5f), new Vector2(220, -110), new Vector2(400, Ui.MinButtonSize));
      var language = Ui.Label("Language", panel, "언어: 한국어", 34, TextAnchor.MiddleCenter);
      Ui.Place(language.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, -200), new Vector2(600, 70));
      if (Debug.isDebugBuild) {
        // 개발 빌드 전용: 룩 후보 바꾸기. 다음 경기부터 적용된다.
        Ui.Place(language.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(-220, -200), new Vector2(400, 70));
        string LookLabel() => "화면: " + VisualPresets.Title(PlayerPrefs.GetString(VisualProfile.ChoiceKey, "A"));
        Button look = null;
        look = Ui.Button("Look", panel, LookLabel(), () => {
          var names = VisualPresets.Names;
          var next = names[(System.Array.IndexOf(names, PlayerPrefs.GetString(VisualProfile.ChoiceKey, "A")) + 1) % names.Length];
          PlayerPrefs.SetString(VisualProfile.ChoiceKey, next);
          VisualProfile.Use(next);
          Ui.SetLabel(look, LookLabel());
        }, 28);
        Ui.Place((RectTransform)look.transform, new Vector2(0.5f, 0.5f), new Vector2(220, -200), new Vector2(400, Ui.MinButtonSize));
      }
      if (Debug.isDebugBuild) {
        // 항목 23: 실기기 시험용. 성능 표시를 켜고 끄고, 경기 기록(CSV)을 클립보드로 복사해 메신저로 보낸다.
        string PerfLabel() => "성능 표시: " + (PerfMonitor.OverlayEnabled ? "켬" : "끔");
        Button perf = null;
        perf = Ui.Button("Perf", panel, PerfLabel(), () => {
          PerfMonitor.OverlayEnabled = PerfMonitor.OverlayEnabled == false;
          Ui.SetLabel(perf, PerfLabel());
        }, 28);
        Ui.Place((RectTransform)perf.transform, new Vector2(0.5f, 0.5f), new Vector2(-220, -300), new Vector2(400, Ui.MinButtonSize));
        Button share = null;
        share = Ui.Button("ShareLog", panel, "기록 공유(복사)", () => {
          var lines = PerfMonitor.CopyLog();
          Ui.SetLabel(share, $"복사됨: 경기 {lines}판");
        }, 28);
        Ui.Place((RectTransform)share.transform, new Vector2(0.5f, 0.5f), new Vector2(220, -300), new Vector2(400, Ui.MinButtonSize));
      }
      var close = Ui.Button("Close", panel, "닫기", onClose, 32);
      Ui.Place((RectTransform)close.transform, new Vector2(0.5f, 0), new Vector2(0, 30), new Vector2(260, Ui.MinButtonSize));
      return panel;
    }
  }

  /// <summary>시작 화면 → 진영 선택 → 경기 설정 → 대전.</summary>
  public class MenuController : MonoBehaviour {
    public const string SceneName = "Menu";

    public enum Screen { Title, Faction, Options, Settings, OnlineMenu, OnlineLobby }

    public Screen Current { get; private set; }
    public MatchSettings Settings { get; } = new MatchSettings();

    RectTransform _root;
    RectTransform _title;
    RectTransform _faction;
    RectTransform _options;
    RectTransform _settings;
    RectTransform _onlineMenu;
    RectTransform _onlineLobby;
    Text _notice;
    Button _mode;
    Button _bots;
    Button _difficulty;
    Button _truce;
    Button _map;
    Button _color;

    // 온라인 방 코드 대전 UI (항목 30, 실제 Photon 방)
    Text _onlineStatus;
    InputField _codeInput;
    Button _onlineFaction;
    Button _onlineColor;
    Text _lobbyCodeText;
    Text _lobbyInfo;
    Text[] _slotTexts;
    Button _lobbyModeBtn;
    Button _lobbyTruceBtn;
    Button _lobbyBotsBtn;
    Button _lobbyDifficultyBtn;
    Button _lobbyReadyBtn;
    Button _lobbyStartBtn;
    Button _lobbyFactionBtn;
    Button _lobbyColorBtn;
    bool _busy;

    public string NoticeText => _notice != null ? _notice.text : "";

    void Awake() {
      var canvas = Ui.CreateCanvas("Menu");
      canvas.transform.SetParent(transform, false);
      _root = Ui.SafeArea(canvas);
      var background = Ui.Panel("Background", _root, new Color(0.1f, 0.13f, 0.17f, 1f));
      Ui.Stretch(background);

      _title = Page("Title");
      var name = Ui.Label("Name", _title, "팩토리 워즈", 110, TextAnchor.MiddleCenter);
      Ui.Place(name.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 240), new Vector2(1200, 180));
      // 두 줄 두 칸(항목 30: 튜토리얼 추가). 엄지가 닿는 화면 가운데 아래.
      TitleButton("튜토리얼", new Vector2(-330, 60), StartTutorial);
      TitleButton("AI 대전", new Vector2(330, 60), () => Show(Screen.Faction));
      TitleButton("온라인 방 코드 대전", new Vector2(-330, -120), () => Show(Screen.OnlineMenu));
      TitleButton("설정", new Vector2(330, -120), () => Show(Screen.Settings));
      if (Debug.isDebugBuild) {
        // 항목 23: 개발 빌드 전용 빠른 시작. 왼쪽 아래에 작게 둔다.
        Quick("AI 대전 2인", new Vector2(-640, -330), () => QuickStart(false, 1));
        Quick("AI 대전 4인", new Vector2(-330, -330), () => QuickStart(true, 3));
        Button truce = null;
        truce = Quick(TruceLabel(), new Vector2(330, -330), () => {
          _quickTruceZero = _quickTruceZero == false;
          Ui.SetLabel(truce, TruceLabel());
        });
        Quick("대규모 교전 장면", new Vector2(640, -330), LoadBattleScene);
      }

      _faction = Page("Faction");
      var pick = Ui.Label("Pick", _faction, "진영 선택", 60, TextAnchor.MiddleCenter);
      Ui.Place(pick.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 300), new Vector2(1000, 100));
      Add(_faction, "자유 연방\n고가·고성능·공중", 110, () => PickFaction(0), 620, 180);
      Add(_faction, "대륙 연방\n물량·저가", -100, () => PickFaction(1), 620, 180);
      Add(_faction, "뒤로", -300, () => Show(Screen.Title));

      _options = Page("Options");
      var match = Ui.Label("Match", _options, "경기 설정", 60, TextAnchor.MiddleCenter);
      Ui.Place(match.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 470), new Vector2(1000, 90));
      _notice = Ui.Label("Notice", _options, "", 30, TextAnchor.MiddleCenter);
      Ui.Place(_notice.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 340), new Vector2(1000, 70));
      _mode = Add(_options, "", 190, NextMode);
      _bots = Add(_options, "", 40, NextBots);
      _difficulty = Add(_options, "", -110, NextDifficulty, 300);
      ((RectTransform)_difficulty.transform).anchoredPosition = new Vector2(-160, -110);
      _map = Add(_options, "", -110, NextMap, 300);
      ((RectTransform)_map.transform).anchoredPosition = new Vector2(160, -110);
      _truce = Add(_options, "", -260, NextTruce, 300);
      ((RectTransform)_truce.transform).anchoredPosition = new Vector2(-160, -260);
      _color = Add(_options, "", -260, NextColor, 300);
      ((RectTransform)_color.transform).anchoredPosition = new Vector2(160, -260);
      var back = Ui.Button("Back", _options, "뒤로", () => Show(Screen.Faction), 34);
      Ui.Place((RectTransform)back.transform, new Vector2(0.5f, 0.5f), new Vector2(-220, -420), new Vector2(380, Ui.MinButtonSize));
      var begin = Ui.Button("Begin", _options, "대전 시작", Begin, 34);
      Ui.Place((RectTransform)begin.transform, new Vector2(0.5f, 0.5f), new Vector2(220, -420), new Vector2(380, Ui.MinButtonSize));

      BuildOnlinePages();

      _settings = SettingsPanel.Build(_root, () => Show(Screen.Title));
      Show(Screen.Title);
    }

    void TitleButton(string label, Vector2 position, UnityEngine.Events.UnityAction onClick) {
      var button = Ui.Button(label, _title, label, onClick, 34);
      var rect = (RectTransform)button.transform;
      Ui.Place(rect, new Vector2(0.5f, 0.5f), position, new Vector2(600, Ui.MinButtonSize));
      rect.pivot = new Vector2(0.5f, 0.5f);
    }

    /// <summary>튜토리얼: 쉬운 AI 1명, 휴전 10분(공격받지 않고 연습).</summary>
    public void StartTutorial() {
      Settings.Mode = MatchMode.Duel;
      Settings.Bots = 1;
      Settings.Difficulty = BotDifficulty.Easy;
      Settings.TruceMinutes = 10;
      Settings.Seed = -1;
      Settings.FastForwardSeconds = 0;
      Settings.Tutorial = true;
      Begin();
    }

    RectTransform Page(string name) {
      var page = Ui.Rect(name, _root);
      Ui.Stretch(page);
      return page;
    }

    Button Add(RectTransform page, string label, float y, UnityEngine.Events.UnityAction onClick, float width = 620, float height = Ui.MinButtonSize) {
      var button = Ui.Button(label, page, label, onClick, 34);
      var rect = (RectTransform)button.transform;
      Ui.Place(rect, new Vector2(0.5f, 0.5f), new Vector2(0, y), new Vector2(width, height));
      rect.pivot = new Vector2(0.5f, 0.5f);
      return button;
    }

    public void Show(Screen screen) {
      Current = screen;
      _title.gameObject.SetActive(screen == Screen.Title);
      _faction.gameObject.SetActive(screen == Screen.Faction);
      _options.gameObject.SetActive(screen == Screen.Options);
      _settings.gameObject.SetActive(screen == Screen.Settings);
      _onlineMenu.gameObject.SetActive(screen == Screen.OnlineMenu);
      _onlineLobby.gameObject.SetActive(screen == Screen.OnlineLobby);
      if (screen == Screen.Options) {
        RefreshOptions();
      } else if (screen == Screen.OnlineLobby) {
        RefreshLobbyUi();
      } else if (screen == Screen.OnlineMenu) {
        RefreshOnlineMenu();
      }
    }

    // ---------- 온라인 (항목 30) ----------

    void BuildOnlinePages() {
      _onlineMenu = Page("OnlineMenu");
      var onlineTitle = Ui.Label("OnlineTitle", _onlineMenu, "온라인 방 코드 대전", 60, TextAnchor.MiddleCenter);
      Ui.Place(onlineTitle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 420), new Vector2(1200, 90));
      _onlineFaction = Ui.Button("OnlineFaction", _onlineMenu, "", () => { Settings.FactionIndex = 1 - Settings.FactionIndex; Settings.ColorIndex = -1; RefreshOnlineMenu(); }, 30);
      Ui.Place((RectTransform)_onlineFaction.transform, new Vector2(0.5f, 0.5f), new Vector2(-220, 280), new Vector2(400, Ui.MinButtonSize));
      _onlineColor = Ui.Button("OnlineColor", _onlineMenu, "", () => { Settings.ColorIndex = (Settings.Color + 1) % Palette.Count; RefreshOnlineMenu(); }, 30);
      Ui.Place((RectTransform)_onlineColor.transform, new Vector2(0.5f, 0.5f), new Vector2(220, 280), new Vector2(400, Ui.MinButtonSize));
      Add(_onlineMenu, "방 만들기", 140, CreateRoom);
      _codeInput = CodeInput(_onlineMenu, new Vector2(-170, 0));
      var join = Ui.Button("JoinRoom", _onlineMenu, "코드로 참가", JoinRoom, 34);
      Ui.Place((RectTransform)join.transform, new Vector2(0.5f, 0.5f), new Vector2(230, 0), new Vector2(360, Ui.MinButtonSize));
      _onlineStatus = Ui.Label("OnlineStatus", _onlineMenu, "", 30, TextAnchor.MiddleCenter);
      Ui.Place(_onlineStatus.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, -130), new Vector2(1400, 100));
      Add(_onlineMenu, "뒤로", -280, () => Show(Screen.Title));

      _onlineLobby = Page("OnlineLobby");
      _lobbyCodeText = Ui.Label("LobbyCode", _onlineLobby, "", 56, TextAnchor.MiddleCenter);
      Ui.Place(_lobbyCodeText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(-150, 460), new Vector2(900, 80));
      var copy = Ui.Button("CopyCode", _onlineLobby, "코드 복사", () => GUIUtility.systemCopyBuffer = OnlineRoom.Instance != null ? OnlineRoom.Instance.Code : "", 28);
      Ui.Place((RectTransform)copy.transform, new Vector2(0.5f, 0.5f), new Vector2(480, 460), new Vector2(280, Ui.MinButtonSize));
      _lobbyInfo = Ui.Label("LobbyInfo", _onlineLobby, "", 24, TextAnchor.MiddleCenter);
      Ui.Place(_lobbyInfo.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 395), new Vector2(1400, 40));
      var slotsPanel = Ui.Panel("SlotsPanel", _onlineLobby, Ui.PanelColor);
      Ui.Place(slotsPanel, new Vector2(0.5f, 0.5f), new Vector2(0, 165), new Vector2(1300, 390));
      _slotTexts = new Text[4];
      for (var i = 0; i < 4; i++) {
        _slotTexts[i] = Ui.Label($"Slot{i}", slotsPanel, "", 30, TextAnchor.MiddleLeft);
        _slotTexts[i].color = Ui.ButtonTextColor;
        Ui.Place(_slotTexts[i].rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -55 - i * 92), new Vector2(1220, 80));
      }
      Button Small(string name, Vector2 position, UnityEngine.Events.UnityAction action) {
        var button = Ui.Button(name, _onlineLobby, "", action, 26);
        Ui.Place((RectTransform)button.transform, new Vector2(0.5f, 0.5f), position, new Vector2(300, Ui.MinButtonSize));
        return button;
      }
      _lobbyFactionBtn = Small("LobbyFaction", new Vector2(-480, -110), () => { Settings.FactionIndex = 1 - Settings.FactionIndex; Settings.ColorIndex = -1; PushMine(false); });
      _lobbyColorBtn = Small("LobbyColor", new Vector2(-160, -110), () => { Settings.ColorIndex = (Settings.Color + 1) % Palette.Count; PushMine(false); });
      _lobbyReadyBtn = Small("LobbyReady", new Vector2(160, -110), () => PushMine(OnlineRoom.Instance != null && OnlineRoom.Instance.MyReady == false));
      _lobbyModeBtn = Small("LobbyMode", new Vector2(-480, -230), NextLobbyMode);
      _lobbyBotsBtn = Small("LobbyBots", new Vector2(-160, -230), NextLobbyBots);
      _lobbyDifficultyBtn = Small("LobbyDifficulty", new Vector2(160, -230), NextLobbyDifficulty);
      _lobbyTruceBtn = Small("LobbyTruce", new Vector2(480, -230), NextLobbyTruce);
      var lobbyLeave = Ui.Button("LobbyLeave", _onlineLobby, "나가기", LeaveRoom, 32);
      Ui.Place((RectTransform)lobbyLeave.transform, new Vector2(0.5f, 0.5f), new Vector2(-220, -380), new Vector2(380, Ui.MinButtonSize));
      _lobbyStartBtn = Ui.Button("LobbyStart", _onlineLobby, "대전 시작", StartOnlineMatch, 32);
      Ui.Place((RectTransform)_lobbyStartBtn.transform, new Vector2(0.5f, 0.5f), new Vector2(220, -380), new Vector2(380, Ui.MinButtonSize));
    }

    InputField CodeInput(RectTransform parent, Vector2 position) {
      var box = Ui.Panel("CodeInput", parent, Color.white);
      Ui.Place(box, new Vector2(0.5f, 0.5f), position, new Vector2(440, Ui.MinButtonSize));
      var text = Ui.Label("Text", box, "", 40, TextAnchor.MiddleCenter);
      text.color = Ui.ButtonTextColor;
      Ui.Stretch(text.rectTransform, 8);
      var hint = Ui.Label("Placeholder", box, "방 코드 6자리", 32, TextAnchor.MiddleCenter);
      hint.color = new Color(0.5f, 0.55f, 0.62f, 1f);
      Ui.Stretch(hint.rectTransform, 8);
      var input = box.gameObject.AddComponent<InputField>();
      input.textComponent = text;
      input.placeholder = hint;
      input.characterLimit = OnlineMatchManager.CodeLength;
      input.contentType = InputField.ContentType.Alphanumeric;
      input.onValidateInput = (value, index, added) => char.ToUpperInvariant(added);
      return input;
    }

    public string OnlineStatusText => _onlineStatus != null ? _onlineStatus.text : "";

    public InputField CodeField => _codeInput;

    void RefreshOnlineMenu() {
      if (_onlineFaction == null) {
        return;
      }
      Ui.SetLabel(_onlineFaction, $"진영: {(Settings.FactionIndex == 0 ? "자유 연방" : "대륙 연방")}");
      Ui.SetLabel(_onlineColor, $"색: {Palette.Names[Settings.Color]}");
      var room = OnlineRoom.Instance;
      _onlineStatus.text = _busy ? "연결 중…" : room != null && room.Stage == RoomStage.Failed ? room.Error : $"버전 {BuildVersion.Current}";
    }

    public async void CreateRoom() {
      if (_busy) {
        return;
      }
      _busy = true;
      RefreshOnlineMenu();
      var ok = await OnlineRoom.Ensure().Create(Settings);
      _busy = false;
      if (ok) {
        Show(Screen.OnlineLobby);
      } else {
        RefreshOnlineMenu();
      }
    }

    public async void JoinRoom() {
      if (_busy) {
        return;
      }
      _busy = true;
      RefreshOnlineMenu();
      var ok = await OnlineRoom.Ensure().Join(_codeInput.text, Settings);
      _busy = false;
      if (ok) {
        Show(Screen.OnlineLobby);
      } else {
        RefreshOnlineMenu();
      }
    }

    async void LeaveRoom() {
      if (OnlineRoom.Instance != null) {
        await OnlineRoom.Instance.Leave();
      }
      Show(Screen.OnlineMenu);
    }

    void PushMine(bool ready) {
      OnlineRoom.Instance?.SetMine(Settings.FactionIndex, Settings.Color, ready);
    }

    void Update() {
      if (Current == Screen.OnlineLobby) {
        var room = OnlineRoom.Instance;
        if (room == null || room.Stage == RoomStage.Failed || room.Stage == RoomStage.Offline) {
          Show(Screen.OnlineMenu);
          return;
        }
        RefreshLobbyUi();
      }
    }

    static readonly MatchMode[] Modes = { MatchMode.Duel, MatchMode.FreeForAll, MatchMode.Team };

    public void NextLobbyMode() {
      var room = OnlineRoom.Instance;
      if (room == null || room.IsHost == false) {
        return;
      }
      var mode = Modes[((int)room.Mode + 1) % Modes.Length];
      // 모드에 맞게 봇 칸 수를 맞춘다(사람 수는 그대로).
      var need = mode == MatchMode.Duel ? 2 : mode == MatchMode.Team ? 4 : Mathf.Max(3, room.Total);
      var bots = Enumerable.Repeat((int)BotDifficulty.Normal, Mathf.Clamp(need - room.Humans, 0, OnlineRoom.MaxRoomPlayers - room.Humans)).ToArray();
      room.HostSet(mode, room.TruceMinutes, bots);
    }

    public void NextLobbyBots() {
      var room = OnlineRoom.Instance;
      if (room == null || room.IsHost == false) {
        return;
      }
      // 빈자리 비우기 ↔ 봇 채우기: 4명까지 하나씩 늘리고, 넘으면 0명으로.
      var count = room.Bots.Length + 1;
      if (room.Humans + count > OnlineRoom.MaxRoomPlayers) {
        count = 0;
      }
      var difficulty = room.Bots.Length > 0 ? room.Bots[0] : (int)BotDifficulty.Normal;
      room.HostSet(room.Mode, room.TruceMinutes, Enumerable.Repeat(difficulty, count).ToArray());
    }

    public void NextLobbyDifficulty() {
      var room = OnlineRoom.Instance;
      if (room == null || room.IsHost == false || room.Bots.Length == 0) {
        return;
      }
      var next = room.Bots[0] == (int)BotDifficulty.Normal ? BotDifficulty.Easy : BotDifficulty.Normal;
      room.HostSet(room.Mode, room.TruceMinutes, Enumerable.Repeat((int)next, room.Bots.Length).ToArray());
    }

    public void NextLobbyTruce() {
      var room = OnlineRoom.Instance;
      if (room == null || room.IsHost == false) {
        return;
      }
      var truce = room.TruceMinutes == 0 ? 5 : room.TruceMinutes == 5 ? 7 : room.TruceMinutes == 7 ? 10 : 0;
      room.HostSet(room.Mode, truce, room.Bots);
    }

    void RefreshLobbyUi() {
      var room = OnlineRoom.Instance;
      if (room == null || _slotTexts == null) {
        return;
      }
      _lobbyCodeText.text = $"방 코드  {room.Code}";
      _lobbyInfo.text = $"버전 {BuildVersion.Current}   핑 {(room.Ping >= 0 ? room.Ping + "ms" : "-")}   {(room.IsHost ? "내가 방장" : "방장을 기다리는 중")}";
      var entries = room.Entries();
      for (var i = 0; i < _slotTexts.Length; i++) {
        if (i >= entries.Count) {
          _slotTexts[i].text = $"{i + 1}. 빈자리";
          continue;
        }
        var e = entries[i];
        var faction = e.Faction == 0 ? "자유 연방" : "대륙 연방";
        _slotTexts[i].text = e.IsBot
          ? $"{i + 1}. {e.Name} ({(e.Difficulty == BotDifficulty.Easy ? "쉬움" : "보통")})"
          : $"{i + 1}. {(e.IsHost ? "[방장] " : "")}{e.Name}{(e.IsLocal ? " (나)" : "")} · {faction} · <color={Palette.Hex(Palette.Colors[e.Color % Palette.Count])}>{Palette.Names[e.Color % Palette.Count]}</color> · {(e.Ready ? "준비" : "대기")}";
      }
      var host = room.IsHost;
      var mode = room.Mode == MatchMode.Duel ? "1대1 (대각선)" : room.Mode == MatchMode.FreeForAll ? "자유 대전" : "2대2 (대각선 팀)";
      Ui.SetLabel(_lobbyModeBtn, $"모드: {mode}");
      Ui.SetLabel(_lobbyBotsBtn, $"봇: {room.Bots.Length}명");
      Ui.SetLabel(_lobbyDifficultyBtn, $"난이도: {(room.Bots.Length > 0 && room.Bots[0] == (int)BotDifficulty.Easy ? "쉬움" : "보통")}");
      Ui.SetLabel(_lobbyTruceBtn, room.TruceMinutes == 0 ? "휴전: 없음" : $"휴전: {room.TruceMinutes}분");
      Ui.SetLabel(_lobbyFactionBtn, $"진영: {(Settings.FactionIndex == 0 ? "자유 연방" : "대륙 연방")}");
      Ui.SetLabel(_lobbyColorBtn, $"색: {Palette.Names[Settings.Color]}");
      Ui.SetLabel(_lobbyReadyBtn, room.MyReady ? "준비 취소" : "준비");
      foreach (var button in new[] { _lobbyModeBtn, _lobbyBotsBtn, _lobbyDifficultyBtn, _lobbyTruceBtn }) {
        Ui.SetEnabled(button, host);
      }
      Ui.SetEnabled(_lobbyReadyBtn, host == false);
      var blocker = room.StartBlocker();
      Ui.SetLabel(_lobbyStartBtn, host ? (blocker ?? "대전 시작") : "방장이 시작합니다");
      Ui.SetEnabled(_lobbyStartBtn, host && blocker == null);
    }

    public void StartOnlineMatch() {
      OnlineRoom.Instance?.HostStart();
    }

    public void PickFaction(int index) {
      Settings.Tutorial = false;
      Settings.FactionIndex = index;
      Show(Screen.Options);
    }

    public void NextMode() {
      Settings.Mode = (MatchMode)(((int)Settings.Mode + 1) % 3);
      if (Settings.Mode == MatchMode.FreeForAll) {
        Settings.Bots = 3;
      }
      RefreshOptions();
    }

    public void NextBots() {
      if (Settings.Mode == MatchMode.Team) {
        Settings.DiagonalTeams = Settings.DiagonalTeams == false;
      } else {
        Settings.Bots = Settings.Bots == 2 ? 3 : 2;
      }
      RefreshOptions();
    }

    public void NextDifficulty() {
      Settings.Difficulty = Settings.Difficulty == BotDifficulty.Easy ? BotDifficulty.Normal : BotDifficulty.Easy;
      RefreshOptions();
    }

    /// <summary>휴전 시간은 0·5·7·10분 중에 고른다.</summary>
    public void NextTruce() {
      Settings.TruceMinutes = Settings.TruceMinutes == 0 ? 5 : Settings.TruceMinutes == 5 ? 7 : Settings.TruceMinutes == 7 ? 10 : 0;
      RefreshOptions();
    }

    /// <summary>항목 35: 맵을 돌려 고른다(MapCatalog).</summary>
    public void NextMap() {
      Settings.MapId = MapCatalog.Next(Settings.MapId).Id;
      RefreshOptions();
    }

    public void NextColor() {
      Settings.ColorIndex = (Settings.Color + 1) % Palette.Count;
      RefreshOptions();
    }

    void RefreshOptions() {
      Settings.Normalize();
      var mode = Settings.Mode == MatchMode.Duel ? "1대1" : Settings.Mode == MatchMode.FreeForAll ? "자유 대전" : "팀전";
      Ui.SetLabel(_mode, $"모드: {mode}");
      if (Settings.Mode == MatchMode.Team) {
        Ui.SetLabel(_bots, $"2대2: {(Settings.DiagonalTeams ? "대각선 동맹" : "이웃 동맹")}");
      } else if (Settings.Mode == MatchMode.FreeForAll) {
        Ui.SetLabel(_bots, Settings.Bots == 2 ? "봇: 2명 (빈자리 비우기)" : "봇: 3명 (봇 채우기)");
      } else {
        Ui.SetLabel(_bots, $"봇: {Settings.Bots}명");
      }
      Ui.SetEnabled(_mode, true);
      Ui.SetEnabled(_bots, Settings.Mode != MatchMode.Duel);
      if (_notice != null) {
        _notice.text = Settings.Mode == MatchMode.Duel
          ? "배치: 남서·북동 / 남동·북서 대각선 (빈 본진은 중립 대형 확장)"
          : Settings.Players == 3
            ? "3인은 자리 유불리가 있을 수 있음"
            : Settings.Mode == MatchMode.Team
              ? (Settings.DiagonalTeams ? "배치: 대각선끼리 같은 편 (기본)" : "배치: 이웃끼리 같은 편")
              : "배치: 4인 전원 본진 배치";
      }
      Ui.SetLabel(_difficulty, $"난이도: {(Settings.Difficulty == BotDifficulty.Easy ? "쉬움" : "보통")}");
      var mapEntry = MapCatalog.Maps[MapCatalog.IndexOf(Settings.MapId)];
      Ui.SetLabel(_map, $"맵: {mapEntry.Name}");
      Ui.SetLabel(_truce, Settings.TruceMinutes == 0 ? "휴전: 없음" : $"휴전: {Settings.TruceMinutes}분");
      var color = Palette.Colors[Settings.Color];
      Ui.SetLabel(_color, $"색: {Palette.Names[Settings.Color]}");
      _color.GetComponent<Image>().color = color;
      _color.GetComponentInChildren<Text>().color = Palette.IsLight(color) ? Color.black : Color.white;
    }

    bool _quickTruceZero = true;

    string TruceLabel() => "휴전 0분: " + (_quickTruceZero ? "켬" : "끔");

    Button Quick(string label, Vector2 position, UnityEngine.Events.UnityAction onClick) {
      var button = Ui.Button(label, _title, label, onClick, 28);
      var rect = (RectTransform)button.transform;
      Ui.Place(rect, new Vector2(0.5f, 0.5f), position, new Vector2(290, Ui.MinButtonSize));
      rect.pivot = new Vector2(0.5f, 0.5f);
      return button;
    }

    void QuickStart(bool fourPlayer, int bots) {
      Settings.Tutorial = false;
      Settings.Mode = fourPlayer ? MatchMode.FreeForAll : MatchMode.Duel;
      Settings.Bots = bots;
      Settings.Difficulty = BotDifficulty.Normal;
      Settings.TruceMinutes = _quickTruceZero ? 0 : 7;
      Settings.Seed = -1;
      Settings.FastForwardSeconds = 0;
      Begin();
    }

    /// <summary>19A 고정 장면과 같은 시드로 2인 경기를 시작해 11:00까지 빨리 감는다(대규모 교전 성능 확인용).</summary>
    void LoadBattleScene() {
      Settings.Mode = MatchMode.Duel;
      Settings.Bots = 1;
      Settings.Difficulty = BotDifficulty.Normal;
      Settings.TruceMinutes = 0;
      Settings.Seed = MatchSettings.BattleSceneSeed;
      Settings.FastForwardSeconds = 660;
      Begin();
    }

    public void Begin() {
      Settings.Normalize();
      MatchSetup.Pending = Settings;
      SceneManager.LoadScene(Settings.Scene);
    }
  }
}
