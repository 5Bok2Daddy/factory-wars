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
    float _stepStarted;

    public static readonly Step[] Steps = {
      new Step { Text = "1/7 화면을 손가락으로 끌어 카메라를 움직여 보세요", Done = (t, f) => (t.Controller.Camera.Focus - t._cameraStart).magnitude > 12f },
      new Step { Text = "2/7 공병(노란 안전모)을 눌러 고르세요", Done = (t, f) => t.Controller.Input.Selection.Units.Any(e => f.Exists(e) && f.Has<Worker>(e)) },
      new Step { Text = "3/7 공병을 고른 채 철광산(주황 바위)을 눌러 채집시키세요", Done = (t, f) => Any<Worker>(f, t.Controller.Player, w => w.Task == WorkerTask.Gather || w.Task == WorkerTask.Deliver) },
      new Step { Text = "4/7 공병을 고르고 오른쪽 아래 패널에서 보급창을 고른 뒤 땅을 눌러 지으세요", Target = "ContextPanel", Done = (t, f) => t.Buildings(f) > t._buildingsStart },
      new Step { Text = "5/7 병영을 눌러 고르고 패널에서 보병을 생산하세요", Target = "ContextPanel", Done = (t, f) => f.Global->Players.GetPointer(t.Controller.Player)->UnitsProduced > t._producedStart },
      new Step { Text = "6/7 전투 유닛을 고르고 \"부대 지정\" → \"1부대\"를 누르세요. 그다음 \"공격 이동\"을 누르고 땅을 누르세요", Target = "SaveButton",
        Done = (t, f) => t.Controller.Input.Selection.GroupSize(0) > 0 && AnyUnit(f, t.Controller.Player, u => u.Order == UnitOrder.AttackMove || u.Order == UnitOrder.Patrol) },
      new Step { Text = "7/7 지도 가운데 점령지(원)로 유닛을 보내세요. 오래 머물면 점령해 자원이 들어옵니다", Done = (t, f) => NearCapture(f, t.Controller.Player) },
    };

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
      _panel = Ui.Panel("TutorialPanel", root, new Color(0.98f, 0.95f, 0.78f, 0.96f));
      Ui.Place(_panel, new Vector2(0.5f, 1), new Vector2(0, -270), new Vector2(1100, 120));
      _panel.pivot = new Vector2(0.5f, 1);
      _text = Ui.Label("TutorialText", _panel, "", 32, TextAnchor.MiddleLeft);
      _text.color = Ui.ButtonTextColor;
      Ui.Place(_text.rectTransform, new Vector2(0, 0.5f), new Vector2(20, 0), new Vector2(820, 110));
      var skip = Ui.Button("TutorialSkip", _panel, "건너뛰기", SkipAll, 26);
      Ui.Place((RectTransform)skip.transform, new Vector2(1, 0.5f), new Vector2(-10, 0), new Vector2(230, 100));
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
