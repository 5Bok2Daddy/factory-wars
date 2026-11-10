namespace FactoryWars.View {
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
      if (MatchSetup.Pending != null) {
        _fastForwardFrames = MatchSetup.Pending.FastForwardSeconds * 20;
        MatchSetup.Apply(FindFirstObjectByType<QuantumRunnerLocalDebug>(), MatchSetup.Pending);
        MatchSetup.Pending = null;
      }
    }

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
      QuantumEvent.Subscribe<EventGameOver>(this, e => ShowResult());
      QuantumEvent.Subscribe<EventPlayerDefeated>(this, e => {
        if (e.Player == _controller.Player) {
          ShowResult();
        }
      });
    }

    void Build(RectTransform root) {
      _pause = Ui.Panel("PauseMenu", root);
      Ui.Place(_pause, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620, 640));
      _pause.pivot = new Vector2(0.5f, 0.5f);
      void Add(string label, float y, UnityEngine.Events.UnityAction action) {
        var button = Ui.Button(label, _pause, label, action, 34);
        var rect = (RectTransform)button.transform;
        Ui.Place(rect, new Vector2(0.5f, 0.5f), new Vector2(0, y), new Vector2(460, Ui.MinButtonSize));
        rect.pivot = new Vector2(0.5f, 0.5f);
      }
      Add("계속", 170, TogglePause);
      Add("설정", 0, () => _settings.gameObject.SetActive(true));
      Add("시작 화면으로", -170, Leave);
      _pause.gameObject.SetActive(false);

      _settings = SettingsPanel.Build(root, () => _settings.gameObject.SetActive(false));
      _settings.gameObject.SetActive(false);

      _result = Ui.Panel("Result", root);
      Ui.Place(_result, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900, 640));
      _result.pivot = new Vector2(0.5f, 0.5f);
      _resultText = Ui.Label("Text", _result, "", 40, TextAnchor.UpperCenter);
      Ui.Place(_resultText.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -40), new Vector2(820, 400));
      var leave = Ui.Button("Leave", _result, "시작 화면으로", Leave, 34);
      var rectLeave = (RectTransform)leave.transform;
      Ui.Place(rectLeave, new Vector2(0.5f, 0), new Vector2(0, 30), new Vector2(460, Ui.MinButtonSize));
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

    public void ShowResult() {
      var frame = _controller.Game?.Frames?.Verified;
      if (frame == null || _result == null) {
        return;
      }
      var state = frame.Global->Players.GetPointer(_controller.Player);
      var won = state->Defeated == false && (frame.Global->GameEnded == false || (frame.Global->WinnerMask & (1 << (int)_controller.Player)) != 0);
      var end = frame.Global->GameEnded ? frame.Global->EndFrame : frame.Number;
      _resultText.text = Summary(won, end / (float)frame.SessionConfig.UpdateFPS, state->UnitsProduced, state->UnitsLost, state->BuildingsLost);
      _result.gameObject.SetActive(true);
    }

    public void Leave() {
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
