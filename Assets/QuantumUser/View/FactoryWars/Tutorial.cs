namespace FactoryWars.View {
  using System;
  using System.Linq;
  using Quantum;
  using UnityEngine;
  using UnityEngine.UI;
  using Button = UnityEngine.UI.Button;

  /// <summary>
  /// 튜토리얼(항목 30, 5분 이내). 쉬운 AI 1명, 긴 휴전, 넉넉한 자원으로 시작하고 단계마다 조건을 시뮬레이션 상태로 확인한다.
  /// 카메라·선택 → 채집 → 건설 → 생산 → 부대 지정·공격 이동 → 점령지 → 승리 조건. 화살표는 눌러야 할 버튼 위에 뜬다.
  /// </summary>
  public unsafe class Tutorial : MonoBehaviour {
    public struct Step {
      public string Text;
      /// <summary>화살표를 띄울 버튼 이름(없으면 화면 가운데 안내만).</summary>
      public string Target;
      public Func<Tutorial, Frame, bool> Done;
    }

    public MatchController Controller;
    public int Index { get; private set; }
    public bool Finished => Index >= Steps.Length;
    public string CurrentText => Finished ? "" : Steps[Index].Text;

    Text _text;
    RectTransform _arrow;
    RectTransform _panel;
    Vector2 _cameraStart;
    int _buildingsStart = -1;
    int _producedStart = -1;
    int _extractorsStart;
    float _earnedStart;
    float _stepStarted;

    /// <summary>
    /// 2026-11-22 개편: 준비 단계 경제 흐름 순서(공병 → 채굴 시설 → 공병 배치 → 자원 생산 → 생산 건물 → 집결 지점 → 전투 준비).
    /// 단계 판정은 시뮬레이션 상태를 읽기만 한다. 명령을 대신 보내지 않는다.
    /// </summary>
    public static readonly Step[] Steps = {
      new Step { Text = "1/8 빈 땅을 끌어 카메라를 움직이세요 (두 손가락으로 확대·축소)", Done = (t, f) => (t.Controller.Camera.Focus - t._cameraStart).magnitude > 12f },
      new Step { Text = "2/8 공병(노란 안전모)을 눌러 고르세요. 왼쪽 아래 '유휴 공병'으로도 고를 수 있습니다", Target = "IdleEngineerButton",
        Done = (t, f) => t.Controller.Input.Selection.Units.Any(e => f.Exists(e) && f.Has<Worker>(e)) },
      new Step { Text = "3/8 오른쪽 아래 '자원 채굴'에서 채굴 시설을 고르고, 녹색 고리가 뜬 자원 지점을 눌러 지으세요", Target = "ContextPanel",
        Done = (t, f) => Extractors(f, t.Controller.Player, false) > t._extractorsStart },
      new Step { Text = "4/8 다 지으면 근처 공병이 저절로 들어가 일합니다. 시설을 눌러 인원을 확인하고 공병을 더 보내세요", Target = "ContextPanel",
        Done = (t, f) => Extractors(f, t.Controller.Player, true) > 0 },
      new Step { Text = "5/8 채굴 시설이 자원을 캐면 위쪽 자원 수치가 오릅니다", Done = (t, f) => Earned(f, t.Controller.Player) > t._earnedStart },
      new Step { Text = "6/8 사령부나 병영을 눌러 고르고 전투 유닛을 생산하세요", Target = "ContextPanel",
        Done = (t, f) => f.Global->Players.GetPointer(t.Controller.Player)->UnitsProduced > t._producedStart },
      new Step { Text = "7/8 생산 건물을 고른 채 '집결 지점'을 누르고 땅을 누르세요. 새 유닛이 그곳으로 모입니다", Target = "ContextPanel",
        Done = (t, f) => AnyRally(f, t.Controller.Player) },
      new Step { Text = "8/8 전투 유닛을 고르고 '공격 이동'을 누른 뒤 땅을 눌러 전선을 준비하세요", Target = "ContextPanel",
        Done = (t, f) => AnyUnit(f, t.Controller.Player, u => u.Order == UnitOrder.AttackMove || u.Order == UnitOrder.Patrol) },
    };

    /// <summary>내 채굴 시설 수(완공). staffed면 공병이 한 명 이상 들어간 시설만 센다.</summary>
    static int Extractors(Frame frame, PlayerRef player, bool staffed) {
      var count = 0;
      foreach (var pair in frame.GetComponentIterator<Building>()) {
        var b = pair.Component;
        if (b.Owner != player || b.ConstructionRemaining > Photon.Deterministic.FP._0 || frame.FindAsset(b.Spec).RequiresResourceNode == false) {
          continue;
        }
        if (staffed == false) {
          count++;
          continue;
        }
        foreach (var unit in frame.GetComponentIterator<Unit>()) {
          if (unit.Component.Owner == player && unit.Component.Garrison == pair.Entity) {
            count++;
            break;
          }
        }
      }
      return count;
    }

    static float Earned(Frame frame, PlayerRef player) {
      var state = frame.Global->Players.GetPointer(player);
      return state->Earned.Iron.AsFloat + state->Earned.Wood.AsFloat;
    }

    static bool AnyRally(Frame frame, PlayerRef player) {
      foreach (var pair in frame.GetComponentIterator<Building>()) {
        if (pair.Component.Owner == player && pair.Component.HasRally) {
          return true;
        }
      }
      return false;
    }

    public static void Attach(MatchController controller) {
      var tutorial = controller.gameObject.AddComponent<Tutorial>();
      tutorial.Controller = controller;
    }

    void Update() {
      if (Controller == null || Controller.IsReady == false) {
        return;
      }
      var frame = Controller.Frame;
      if (frame == null) {
        return;
      }
      if (_panel == null) {
        Build();
        _cameraStart = Controller.Camera.Focus;
        _buildingsStart = Buildings(frame);
        _producedStart = frame.Global->Players.GetPointer(Controller.Player)->UnitsProduced;
        _extractorsStart = Extractors(frame, Controller.Player, false);
        _earnedStart = Earned(frame, Controller.Player);
        _stepStarted = Time.unscaledTime;
      }
      if (Finished) {
        return;
      }
      if (Steps[Index].Done(this, frame)) {
        Advance(frame);
      }
      Refresh();
    }

    void Advance(Frame frame) {
      Index++;
      _stepStarted = Time.unscaledTime;
      _buildingsStart = Buildings(frame);
      _producedStart = frame.Global->Players.GetPointer(Controller.Player)->UnitsProduced;
      _extractorsStart = Extractors(frame, Controller.Player, false);
      _earnedStart = Earned(frame, Controller.Player);
      GameAudio.Play(SoundEvent.Captured);
      if (Finished) {
        _text.text = "튜토리얼 끝! 적 사령부와 핵심 건물을 모두 부수면 승리합니다. 이대로 계속 싸워 보세요";
        Destroy(_panel.gameObject, 8f);
      }
    }

    /// <summary>시험용: 지금 단계를 건너뛴다.</summary>
    public void Skip() {
      var frame = Controller.Frame;
      if (frame != null && Finished == false) {
        Advance(frame);
        Refresh();
      }
    }

    public void SkipAll() {
      Index = Steps.Length;
      if (_panel != null) {
        Destroy(_panel.gameObject);
      }
    }

    int Buildings(Frame f) {
      var count = 0;
      foreach (var pair in f.GetComponentIterator<Building>()) {
        count += pair.Component.Owner == Controller.Player ? 1 : 0;
      }
      return count;
    }

    static bool Any<T>(Frame f, PlayerRef player, Func<T, bool> test) where T : unmanaged, IComponent {
      foreach (var pair in f.GetComponentIterator<T>()) {
        if (f.Get<Unit>(pair.Entity).Owner == player && test(pair.Component)) {
          return true;
        }
      }
      return false;
    }

    static bool AnyUnit(Frame f, PlayerRef player, Func<Unit, bool> test) {
      foreach (var pair in f.GetComponentIterator<Unit>()) {
        if (pair.Component.Owner == player && test(pair.Component)) {
          return true;
        }
      }
      return false;
    }

    static bool NearCapture(Frame f, PlayerRef player) {
      var map = f.FindAsset<MapSpec>(f.Map.UserAsset);
      if (map.CaptureSites == null) {
        return true;
      }
      foreach (var pair in f.GetComponentIterator<Unit>()) {
        if (pair.Component.Owner != player) {
          continue;
        }
        var p = f.Get<Transform3D>(pair.Entity).Position;
        foreach (var site in map.CaptureSites) {
          if (Quantum.FactoryWars.FactoryWarsUtil.FlatDistanceSqr(p, site.Center) <= site.Radius * site.Radius * 4) {
            return true;
          }
        }
      }
      return false;
    }

    void Build() {
      var root = Controller.Hud.Root;
      // 2026-11-22: 전장 가운데를 가리지 않게 위쪽 알림 두 줄 아래의 짧은 띠(테마 패널, 호박색 글자). 건너뛰기로 언제든 끈다.
      var theme = UiTheme.Current;
      _panel = Ui.Panel("TutorialPanel", root, theme.Panel);
      Ui.Place(_panel, new Vector2(0.5f, 1), new Vector2(0, -184), new Vector2(940, Ui.MinButtonSize));
      _panel.pivot = new Vector2(0.5f, 1);
      _text = Ui.Label("TutorialText", _panel, "", theme.FontSmall, TextAnchor.MiddleLeft);
      _text.color = theme.Warning;
      _text.resizeTextForBestFit = true;
      _text.resizeTextMinSize = theme.FontSmall - 8;
      _text.resizeTextMaxSize = theme.FontSmall;
      Ui.Place(_text.rectTransform, new Vector2(0, 0.5f), new Vector2(18, 0), new Vector2(940 - 18 - 200 - 18, Ui.MinButtonSize - 12));
      var skip = Ui.Button("TutorialSkip", _panel, "건너뛰기", SkipAll, theme.FontSmall);
      Ui.Place((RectTransform)skip.transform, new Vector2(1, 0.5f), new Vector2(-8, 0), new Vector2(200, Ui.MinButtonSize));
      ((RectTransform)skip.transform).pivot = new Vector2(1, 0.5f);
      _arrow = Ui.Label("TutorialArrow", root, "▼", 90, TextAnchor.MiddleCenter).rectTransform;
      _arrow.GetComponent<Text>().color = new Color(1f, 0.55f, 0.1f, 1f);
      _arrow.GetComponent<Text>().raycastTarget = false;
      _arrow.sizeDelta = new Vector2(120, 120);
      Refresh();
    }

    void Refresh() {
      if (_text == null || Finished) {
        if (_arrow != null) {
          _arrow.gameObject.SetActive(false);
        }
        return;
      }
      _text.text = Steps[Index].Text;
      var target = string.IsNullOrEmpty(Steps[Index].Target) ? null : FindDeep(Controller.Hud.Root, Steps[Index].Target);
      var show = target != null && target.gameObject.activeInHierarchy;
      _arrow.gameObject.SetActive(show);
      if (show) {
        // 대상 위쪽 가운데에 위아래로 흔들리며 뜬다.
        var corners = new Vector3[4];
        ((RectTransform)target).GetWorldCorners(corners);
        var top = (corners[1] + corners[2]) / 2;
        _arrow.position = top + Vector3.up * (50f + Mathf.Sin((Time.unscaledTime - _stepStarted) * 6f) * 12f);
      }
    }

    static Transform FindDeep(Transform root, string name) {
      if (root.name == name) {
        return root;
      }
      foreach (Transform child in root) {
        var found = FindDeep(child, name);
        if (found != null) {
          return found;
        }
      }
      return null;
    }
  }
}
