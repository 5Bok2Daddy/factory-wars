import os, shutil
HERE = os.path.dirname(os.path.abspath(__file__))
os.chdir(r'C:\Users\MinGyu\FactoryWars\Game\Assets\QuantumUser\View\FactoryWars')

def edit(p, pairs):
    s = open(p, encoding='utf-8', newline='').read()
    crlf = '\r\n' in s
    s = s.replace('\r\n', '\n')
    for a, b in pairs:
        assert a in s, (p, a[:80])
        s = s.replace(a, b, 1)
    if crlf:
        s = s.replace('\n', '\r\n')
    open(p, 'w', encoding='utf-8', newline='').write(s)

shutil.copy(os.path.join(HERE, 'PerfMonitor.cs'), 'PerfMonitor.cs')

# 첫 실행 품질은 기기 정보로 고른다. 설정에서 직접 바꾸면 자동 조정을 끈다.
edit('GraphicsQuality.cs', [
('''      Apply((QualityTier)Mathf.Clamp(PlayerPrefs.GetInt(PrefsKey, (int)Default), 0, Names.Length - 1));
    }''', '''      if (PlayerPrefs.HasKey(PrefsKey) == false) {
        PlayerPrefs.SetInt(PrefsKey, (int)AutoQuality.Guess());
      }
      Apply((QualityTier)Mathf.Clamp(PlayerPrefs.GetInt(PrefsKey, (int)Default), 0, Names.Length - 1));
    }'''),
('''      PlayerPrefs.SetInt(PrefsKey, (int)next);''', '''      PlayerPrefs.SetInt(PrefsKey, (int)next);
      PlayerPrefs.SetInt(AutoQuality.DoneKey, 1);'''),
])

edit('MatchController.cs', [('''      Hud = hud;''', '''      Hud = hud;
      var perf = gameObject.AddComponent<PerfMonitor>();
      perf.Controller = this;
      perf.Build(hud.Root);''')])

# 설정 화면: 개발 빌드 전용 줄(성능 표시, 기록 공유).
edit('MenuController.cs', [
('''      Ui.Place(panel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900, 620));''', '''      Ui.Place(panel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900, Debug.isDebugBuild ? 800 : 620));'''),
('''      var close = Ui.Button("Close", panel, "닫기", onClose, 32);''', '''      if (Debug.isDebugBuild) {
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
      var close = Ui.Button("Close", panel, "닫기", onClose, 32);'''),
('''      Add(_title, "설정", -160, () => Show(Screen.Settings));''', '''      Add(_title, "설정", -160, () => Show(Screen.Settings));
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
      }'''),
('''    public void Begin() {''', '''    bool _quickTruceZero = true;

    string TruceLabel() => "휴전 0분: " + (_quickTruceZero ? "켬" : "끔");

    Button Quick(string label, Vector2 position, UnityEngine.Events.UnityAction onClick) {
      var button = Ui.Button(label, _title, label, onClick, 28);
      var rect = (RectTransform)button.transform;
      Ui.Place(rect, new Vector2(0.5f, 0.5f), position, new Vector2(290, Ui.MinButtonSize));
      rect.pivot = new Vector2(0.5f, 0.5f);
      return button;
    }

    void QuickStart(bool fourPlayer, int bots) {
      Settings.FourPlayerMap = fourPlayer;
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
      Settings.FourPlayerMap = false;
      Settings.Mode = MatchMode.Duel;
      Settings.Bots = 1;
      Settings.Difficulty = BotDifficulty.Normal;
      Settings.TruceMinutes = 0;
      Settings.Seed = MatchSettings.BattleSceneSeed;
      Settings.FastForwardSeconds = 660;
      Begin();
    }

    public void Begin() {'''),
])

edit('MatchSetup.cs', [
('''    public int TruceMinutes = 7;''', '''    public int TruceMinutes = 7;
    /// <summary>고정 시드(음수면 무작위). 개발용 "대규모 교전 장면"이 쓴다.</summary>
    public int Seed = -1;
    /// <summary>시작 뒤 이 시간(초)까지 빨리 감는다. 0이면 안 한다.</summary>
    public int FastForwardSeconds;
    /// <summary>19A 비교 환경의 고정 장면과 같은 시드.</summary>
    public const int BattleSceneSeed = 7020;'''),
('''      runner.UseRandomSeed = true;''', '''      runner.UseRandomSeed = settings.Seed < 0;
      if (settings.Seed >= 0) {
        config.Seed = settings.Seed;
      }'''),
])

edit('MatchFlow.cs', [
('''      if (MatchSetup.Pending != null) {
        MatchSetup.Apply(FindFirstObjectByType<QuantumRunnerLocalDebug>(), MatchSetup.Pending);''', '''      if (MatchSetup.Pending != null) {
        _fastForwardFrames = MatchSetup.Pending.FastForwardSeconds * 20;
        MatchSetup.Apply(FindFirstObjectByType<QuantumRunnerLocalDebug>(), MatchSetup.Pending);'''),
('''    void Update() {
      if (_built || _controller.IsReady == false) {
        return;
      }''', '''    int _fastForwardFrames;

    void Update() {
      // 개발용 빨리 감기: 정해진 프레임까지 8배속, 그 뒤 원래 속도.
      if (_fastForwardFrames > 0 && _controller.IsReady && _controller.Frame != null) {
        var reached = _controller.Frame.Number >= _fastForwardFrames;
        Time.timeScale = reached ? 1f : 8f;
        if (reached) {
          _fastForwardFrames = 0;
        }
      }
      if (_built || _controller.IsReady == false) {
        return;
      }'''),
])
print('ok')
