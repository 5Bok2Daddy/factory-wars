namespace FactoryWars.View {
  using System.Linq;
  using Photon.Deterministic;
  using Quantum;
  using UnityEngine;
  using UnityEngine.SceneManagement;
  using UnityEngine.UI;

  /// <summary>경기 시작 조건 적용, 일시정지, 결과 화면.</summary>
  public unsafe class MatchFlow : MonoBehaviour {
    public static MatchFlow Current;

    public bool IsPaused { get; private set; }
    public bool ResultVisible => _result != null && _result.gameObject.activeSelf;
    public string ResultText => _resultText != null ? _resultText.text : "";

    MatchController _controller;
    RectTransform _pause;
    RectTransform _settings;
    RectTransform _result;
    Text _resultText;
    bool _built;

    void Awake() {
      Current = this;
      _controller = GetComponent<MatchController>();
      if (MatchSetup.Pending != null && MatchSetup.Pending.IsOnline && OnlineRoom.Instance != null) {
        // 온라인(항목 30): 씬의 로컬 러너는 시작하지 않고(Start 전에 끈다), 그 설정을 본으로 온라인 세션을 연다.
        IsOnline = true;
        var local = FindFirstObjectByType<QuantumRunnerLocalDebug>();
        local.enabled = false;
        var factions = local.LocalPlayers.Take(2).Select(p => p.Faction).ToArray();
        var template = local.RuntimeConfig;
        var mapData = FindAnyObjectByType<QuantumMapData>();
        if (mapData != null) {
          template.Map = mapData.AssetRef;
        }
        MatchSetup.Pending = null;
        _ = OnlineRoom.Instance.StartSession(template, factions);
      } else if (MatchSetup.Pending != null) {
        _tutorial = MatchSetup.Pending.Tutorial;
        _fastForwardFrames = MatchSetup.Pending.FastForwardSeconds * 20;
        MatchSetup.Apply(FindFirstObjectByType<QuantumRunnerLocalDebug>(), MatchSetup.Pending);
        MatchSetup.Pending = null;
      }
    }

    public bool IsOnline { get; private set; }
    bool _tutorial;

    int _fastForwardFrames;

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
      }
      _built = true;
      Build(_controller.Hud.Root);
      if (_tutorial) {
        Tutorial.Attach(_controller);
      }
      QuantumEvent.Subscribe<EventGameOver>(this, e => ShowResult());
      QuantumEvent.Subscribe<EventPlayerDefeated>(this, e => {
        if (e.Player == _controller.Player) {
          ShowResult();
        }
      });
    }

    void Build(RectTransform root) {
      _pause = Ui.Panel("PauseMenu", root);
      Ui.Place(_pause, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620, 760));
      _pause.pivot = new Vector2(0.5f, 0.5f);
      void Add(string label, float y, UnityEngine.Events.UnityAction action) {
        var button = Ui.Button(label, _pause, label, action, 34);
        var rect = (RectTransform)button.transform;
        Ui.Place(rect, new Vector2(0.5f, 0.5f), new Vector2(0, y), new Vector2(460, Ui.MinButtonSize));
        rect.pivot = new Vector2(0.5f, 0.5f);
      }
      Add("계속", 255, TogglePause);
      Add("설정", 85, () => _settings.gameObject.SetActive(true));
      _surrender = Ui.Button("기권", _pause, "기권", Surrender, 34);
      var surrenderRect = (RectTransform)_surrender.transform;
      Ui.Place(surrenderRect, new Vector2(0.5f, 0.5f), new Vector2(0, -85), new Vector2(460, Ui.MinButtonSize));
      surrenderRect.pivot = new Vector2(0.5f, 0.5f);
      Add("시작 화면으로", -255, Leave);
      _pause.gameObject.SetActive(false);

      _settings = SettingsPanel.Build(root, () => _settings.gameObject.SetActive(false));
      _settings.gameObject.SetActive(false);

      _result = Ui.Panel("Result", root);
      Ui.Place(_result, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1100, 860));
      _result.pivot = new Vector2(0.5f, 0.5f);
      _resultText = Ui.Label("Text", _result, "", 40, TextAnchor.UpperCenter);
      Ui.Place(_resultText.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -30), new Vector2(1020, 620));
      var leave = Ui.Button("Leave", _result, "시작 화면으로", Leave, 34);
      var rectLeave = (RectTransform)leave.transform;
      Ui.Place(rectLeave, new Vector2(0.5f, 0), new Vector2(240, 30), new Vector2(440, Ui.MinButtonSize));
      // 다시 하기: AI 대전만(같은 조건으로 새 경기). 온라인은 방에서 다시 모인다.
      var again = Ui.Button("Again", _result, "다시 하기", PlayAgain, 34);
      var rectAgain = (RectTransform)again.transform;
      Ui.Place(rectAgain, new Vector2(0.5f, 0), new Vector2(-240, 30), new Vector2(440, Ui.MinButtonSize));
      again.gameObject.SetActive(IsOnline == false && MatchSetup.Last != null);
      _result.gameObject.SetActive(false);
    }

    /// <summary>AI 대전에서만 시뮬레이션을 멈춘다. 온라인에서는 메뉴만 연다.</summary>
    public void TogglePause() {
      IsPaused = IsPaused == false;
      _pause.gameObject.SetActive(IsPaused);
      var runner = QuantumRunner.Default;
      if (runner != null && runner.Session.GameMode == DeterministicGameMode.Local) {
        runner.IsSessionUpdateDisabled = IsPaused;
        // 로컬 러너는 자기 Update에서 직접 세션을 돌린다.
        var local = FindFirstObjectByType<QuantumRunnerLocalDebug>();
        if (local != null) {
          local.enabled = IsPaused == false;
        }
      }
    }

    public static string Summary(bool won, float seconds, int produced, int unitsLost, int buildingsLost) {
      var minutes = Mathf.FloorToInt(seconds / 60f);
      var rest = Mathf.FloorToInt(seconds % 60f);
      return $"<size=72>{(won ? "승리" : "패배")}</size>\n\n경기 시간 {minutes}:{rest:00}\n생산 {produced}\n유닛 손실 {unitsLost}\n건물 손실 {buildingsLost}";
    }

    /// <summary>결과 화면 전체 통계(항목 30): 생산·손실·처치·파괴, 번 자원, 점령 시간, 상자.</summary>
    public static string FullSummary(bool won, float seconds, PlayerState state, int fps) {
      var text = Summary(won, seconds, state.UnitsProduced, state.UnitsLost, state.BuildingsLost);
      var captureSeconds = state.CaptureFrames / Mathf.Max(1, fps);
      return text + $"\n처치 {state.UnitsKilled}   파괴한 건물 {state.BuildingsDestroyed}"
        + $"\n번 자원  철 {state.Earned.Iron.AsInt}  목재 {state.Earned.Wood.AsInt}  강철 {state.Earned.Steel.AsInt}"
        + $"\n점령 {state.Captures}회 ({captureSeconds / 60}:{captureSeconds % 60:00})   보급 상자 {state.CratesPicked}";
    }

    UnityEngine.UI.Button _surrender;
    float _surrenderArmedUntil;

    /// <summary>기권: 한 번 누르면 확인을 묻고, 3초 안에 다시 누르면 확정한다.</summary>
    public void Surrender() {
      if (Time.unscaledTime > _surrenderArmedUntil) {
        _surrenderArmedUntil = Time.unscaledTime + 3f;
        Ui.SetLabel(_surrender, "정말 기권? 다시 누르기");
        return;
      }
      _surrenderArmedUntil = 0f;
      Ui.SetLabel(_surrender, "기권");
      if (IsPaused) {
        TogglePause();
      }
      _controller.Send(new Quantum.FactoryWars.SurrenderCommand());
    }

    public void PlayAgain() {
      var last = MatchSetup.Last;
      if (last == null) {
        Leave();
        return;
      }
      QuantumRunner.ShutdownAll();
      MatchSetup.Pending = last;
      SceneManager.LoadScene(last.Scene);
    }

    /// <summary>앱 전환(항목 30): AI 대전은 멈추고, 온라인은 돌아왔을 때 연결이 끊겼으면 같은 방에 다시 들어간다.</summary>
    void OnApplicationPause(bool paused) {
      if (_built == false) {
        return;
      }
      if (IsOnline == false) {
        if (paused && IsPaused == false) {
          TogglePause();
        }
        return;
      }
      var room = OnlineRoom.Instance;
      if (paused == false && room != null && room.Client != null && room.Client.IsConnected == false) {
        var local = FindFirstObjectByType<QuantumRunnerLocalDebug>(FindObjectsInactive.Include);
        if (local != null) {
          _ = room.Reconnect(local.RuntimeConfig, local.LocalPlayers.Take(2).Select(p => p.Faction).ToArray());
        }
      }
    }

    public void ShowResult() {
      var frame = _controller.Game?.Frames?.Verified;
      if (frame == null || _result == null) {
        return;
      }
      var state = frame.Global->Players.GetPointer(_controller.Player);
      var won = state->Defeated == false && (frame.Global->GameEnded == false || (frame.Global->WinnerMask & (1 << (int)_controller.Player)) != 0);
      var end = frame.Global->GameEnded ? frame.Global->EndFrame : frame.Number;
      var summary = FullSummary(won, end / (float)frame.SessionConfig.UpdateFPS, *state, frame.SessionConfig.UpdateFPS);
      var mvpName = "없음";
      var mvpKills = -1;
      var mvpRank = 0;
      var units = frame.Filter<Unit>();
      while (units.NextUnsafe(out _, out var u)) {
        if (u->Owner == _controller.Player && (u->Kills > mvpKills || (u->Kills == mvpKills && u->VetRank > mvpRank))) {
          mvpKills = u->Kills;
          mvpRank = u->VetRank;
          mvpName = Texts.NameOf(frame.FindAsset(u->Spec).name);
        }
      }
      var mvpLine = mvpKills >= 0
        ? $"\nMVP 유닛: {mvpName} {(mvpRank > 0 ? new string('★', mvpRank) + " " : "")}(처치 {mvpKills})"
        : "";
      var tacticalLine = $"\n전술 요약: 항공 출격 {state->SortiesCompleted}회 · 블랙박스 {state->BlackBoxes} · 교량 파괴/복구 {state->BridgesDown}/{state->BridgesRepaired} · 잔해 회수 {state->WrecksSalvaged}";
      _resultText.text = summary + mvpLine + tacticalLine;
      _result.gameObject.SetActive(true);
      GameAudio.Play(won ? SoundEvent.Victory : SoundEvent.Defeat);
    }

    public void Leave() {
      if (OnlineRoom.Instance != null) {
        _ = OnlineRoom.Instance.Leave();
      }
      QuantumRunner.ShutdownAll();
      SceneManager.LoadScene(MenuController.SceneName);
    }

    void OnDestroy() {
      if (Current == this) {
        Current = null;
      }
      QuantumEvent.UnsubscribeListener(this);
    }
  }
}
