namespace FactoryWars.Tests {
  using System.Collections;
  using System.Collections.Generic;
  using System.Linq;
  using FactoryWars.View;
  using NUnit.Framework;
  using Assert = NUnit.Framework.Assert;
  using Photon.Deterministic;
  using Quantum;
  using Quantum.FactoryWars;
  using UnityEngine;
  using UnityEngine.TestTools;
  using UnityEngine.UI;
  using Button = UnityEngine.UI.Button;

  /// <summary>제스처 판별, 탭 해석, 카메라, HUD. 입력은 장치 대신 값으로 넣는다.</summary>
  public unsafe partial class LocalMatchTests {
    static readonly Vector2 A = new Vector2(100, 100);

    static List<Gesture> Feed(GestureRecognizer recognizer, params (int Fingers, Vector2 First, Vector2 Second, float Time)[] steps) {
      var results = new List<Gesture>();
      foreach (var step in steps) {
        recognizer.Update(step.Fingers, step.First, step.Second, step.Time, results);
      }
      return results;
    }

    [Test]
    public void Gesture_ShortPressIsTap() {
      var result = Feed(new GestureRecognizer(), (1, A, A, 0f), (1, A, A, 0.1f), (0, A, A, 0.15f));
      Assert.AreEqual(1, result.Count);
      Assert.AreEqual(GestureKind.Tap, result[0].Kind);
    }

    [Test]
    public void Gesture_TwoQuickTapsAreDoubleTap() {
      var result = Feed(new GestureRecognizer(), (1, A, A, 0f), (0, A, A, 0.1f), (1, A, A, 0.2f), (0, A, A, 0.3f));
      Assert.AreEqual(new[] { GestureKind.Tap, GestureKind.DoubleTap }, result.Select(g => g.Kind).ToArray());
    }

    [Test]
    public void Gesture_SlowSecondTapIsNotDouble() {
      var result = Feed(new GestureRecognizer(), (1, A, A, 0f), (0, A, A, 0.1f), (1, A, A, 1f), (0, A, A, 1.1f));
      Assert.AreEqual(new[] { GestureKind.Tap, GestureKind.Tap }, result.Select(g => g.Kind).ToArray());
    }

    [Test]
    public void Gesture_HoldIsLongPressWithoutTap() {
      var result = Feed(new GestureRecognizer(), (1, A, A, 0f), (1, A, A, 0.6f), (1, A, A, 0.7f), (0, A, A, 0.8f));
      Assert.AreEqual(new[] { GestureKind.LongPress }, result.Select(g => g.Kind).ToArray());
    }

    [Test]
    public void Gesture_MoveIsDragWithoutTap() {
      var b = A + new Vector2(50, 0);
      var c = A + new Vector2(80, 0);
      var result = Feed(new GestureRecognizer(), (1, A, A, 0f), (1, b, b, 0.1f), (1, c, c, 0.2f), (0, c, c, 0.3f));
      Assert.AreEqual(new[] { GestureKind.DragBegin, GestureKind.Drag, GestureKind.DragEnd }, result.Select(g => g.Kind).ToArray());
      Assert.AreEqual(new Vector2(30, 0), result[1].Delta);
      Assert.AreEqual(A, result[2].Start);
    }

    [Test]
    public void Gesture_TwoFingersPinchAndRemainingFingerDoesNotDrag() {
      var far = A + new Vector2(200, 0);
      var farther = A + new Vector2(400, 0);
      var result = Feed(new GestureRecognizer(), (2, A, far, 0f), (2, A, farther, 0.1f), (1, A, A, 0.2f), (1, A + new Vector2(90, 0), A, 0.3f), (0, A, A, 0.4f));
      Assert.AreEqual(new[] { GestureKind.Pinch }, result.Select(g => g.Kind).ToArray());
      Assert.AreEqual(2f, result[0].Scale, 0.001f);
    }

    [Test]
    public void Gesture_InputStartedOnUiIsIgnored() {
      var recognizer = new GestureRecognizer();
      recognizer.IgnoreUntilRelease();
      var result = Feed(recognizer, (1, A, A, 0f), (0, A, A, 0.1f));
      Assert.AreEqual(0, result.Count);
    }

    [Test]
    public void Camera_StaysInsideMapAndZoomRange() {
      var min = new Vector2(-80, -80);
      var max = new Vector2(80, 80);
      Assert.AreEqual(max, RtsCamera.ClampFocus(new Vector2(500, 500), min, max));
      Assert.AreEqual(new Vector2(-80, 10), RtsCamera.ClampFocus(new Vector2(-90, 10), min, max));
      Assert.AreEqual(RtsCamera.MaxHeight, RtsCamera.ClampHeight(9999f));
      Assert.AreEqual(RtsCamera.MinHeight, RtsCamera.ClampHeight(1f));
    }

    [Test]
    public void Texts_CostMarksMissingInRed() {
      var cost = new ResourceCost { Iron = 100, Steel = 50 };
      var stock = new ResourceCost { Iron = 500, Steel = 10 };
      var text = Texts.Cost(cost, stock);
      Assert.IsTrue(text.StartsWith("철 100"), text);
      Assert.IsTrue(text.Contains("<color=#FF5A4F>강철 50</color>"), text);
      Assert.IsFalse(text.Contains("목재"), "비용이 없는 자원은 표시하지 않음");
    }

    [Test]
    public void Texts_RejectionNamesEveryMissingResource() {
      var both = MissingResource.Iron | MissingResource.Power | MissingResource.Population;
      Assert.AreEqual("철·전기 부족, 인구 한도", Texts.Rejected(RejectReason.InsufficientResources, both));
      Assert.AreEqual("인구 한도", Texts.Rejected(RejectReason.PopulationLimit, MissingResource.Population));
      Assert.AreEqual("건설 불가 지형", Texts.Rejected(RejectReason.BlockedLocation, MissingResource.None));
    }

    [Test]
    public void Texts_NoteHasAPhraseForEveryRuleSlot() {
      Assert.AreEqual(12, Texts.Phrases.Length);
    }

    static Vector3 W(int x, int z) => new Vector3(x, 0, z);

    [UnityTest]
    public IEnumerator Input_TapSelectsOwnUnitThenMovesIt() {
      yield return StartMatch();
      var input = new MatchInput();
      var tank = UnitsNamed(0, "ff_eagle_tank")[0];
      var position = Verified.Get<Transform3D>(tank).Position.ToUnityVector3();
      Assert.IsNull(input.Tap(Verified, 0, position), "선택만 한다");
      Assert.AreEqual(new[] { tank }, input.Selection.Units.ToArray());

      var command = input.Tap(Verified, 0, W(0, 0)) as MoveUnitsCommand;
      Assert.IsNotNull(command, "땅을 누르면 이동");
      Assert.IsFalse(command.AttackMove);

      var attackMove = input.LongPress(Verified, W(0, 0)) as MoveUnitsCommand;
      Assert.IsTrue(attackMove.AttackMove, "길게 누르면 공격 이동");
    }

    [UnityTest]
    public IEnumerator Input_TapOnEnemyAttacksOnlyWithSelection() {
      yield return StartMatch();
      var input = new MatchInput();
      var enemy = Verified.Get<Transform3D>(Headquarters(1)).Position.ToUnityVector3();
      Assert.IsNull(input.Tap(Verified, 0, enemy), "선택 없이는 아무 일 없음");

      input.Selection.SelectUnit(UnitsNamed(0, "ff_eagle_tank")[0]);
      var command = input.Tap(Verified, 0, enemy) as AttackTargetCommand;
      Assert.IsNotNull(command);
      Assert.AreEqual(Headquarters(1), command.Target);
    }

    [UnityTest]
    public IEnumerator Input_EngineerTapsResourceAndBuildsWhilePlacing() {
      yield return StartMatch();
      var input = new MatchInput();
      var engineer = Engineers(0)[0];
      input.Selection.SelectUnit(engineer);
      Assert.IsInstanceOf<GatherCommand>(input.Tap(Verified, 0, W(-30, 14)), "철광산을 누르면 채집");

      input.Placing = BuildingOf(0, "_barracks");
      var build = input.Tap(Verified, 0, W(-20, 10)) as BuildCommand;
      Assert.IsNotNull(build, "건설 위치 지정");
      Assert.AreEqual(engineer, build.Engineer);
      Assert.IsFalse(input.IsPlacing, "한 번 지으면 끝");
    }

    [UnityTest]
    public IEnumerator Input_BoxAndDoubleTapAndGroups() {
      yield return StartMatch();
      var selection = new SelectionModel();
      selection.SelectBox(Verified, 0, new Rect(-50, -50, 100, 100), w => new Vector2(w.x, w.z));
      Assert.AreEqual(Army(0, false).Count, selection.Units.Count, "내 유닛만 고른다");

      var ranger = UnitsNamed(0, "ff_ranger_squad")[0];
      selection.SelectSameType(Verified, 0, ranger, w => true);
      Assert.AreEqual(2, selection.Units.Count, "같은 종류 전체");

      selection.SaveGroup(1);
      selection.Clear();
      selection.RecallGroup(1);
      Assert.AreEqual(2, selection.Units.Count, "부대 지정");
    }

    [UnityTest]
    public IEnumerator Stop_HaltsUnitAndEngineerWork() {
      yield return StartMatch();
      var engineer = Engineers(0)[0];
      yield return Send(0, new GatherCommand { Units = new[] { engineer }, Node = NodeAt(V(-30, 14)) });
      Assert.AreEqual(WorkerTask.Gather, WorkerOf(engineer).Task);
      yield return Send(0, new StopUnitsCommand { Units = new[] { engineer } });
      Assert.AreEqual(WorkerTask.Idle, WorkerOf(engineer).Task);

      var tank = UnitsNamed(0, "ff_eagle_tank")[0];
      yield return Send(0, new MoveUnitsCommand { Units = new[] { tank }, Target = V(0, 0) });
      yield return Send(0, new StopUnitsCommand { Units = new[] { tank } });
      var stopped = Verified.Get<Transform3D>(tank).Position;
      yield return WaitFrames(Fps);
      Assert.AreEqual(UnitOrder.Stop, Verified.Get<Unit>(tank).Order);
      Assert.Less((Verified.Get<Transform3D>(tank).Position - stopped).Magnitude.AsFloat, 0.5f, "제자리");
    }

    static MatchController Controller() => Object.FindAnyObjectByType<MatchController>();

    IEnumerator WaitForHud() {
      yield return WaitFor(() => Controller() != null && Controller().IsReady, 10f, "HUD 생성");
      Controller().Hud.Refresh(Controller().Frame);
    }

    [UnityTest]
    public IEnumerator Hud_TopBarShowsAllResources() {
      yield return StartMatch(stock: Stock(1234, 567, 89, 100));
      yield return WaitForHud();
      var text = Controller().Hud.TopBarText;
      foreach (var part in new[] { "철 1234", "목재 567", "강철 89", "전기 100/200", "인구 6/10" }) {
        Assert.IsTrue(text.Contains(part), $"{part} 없음: {text}");
      }
    }

    [UnityTest]
    public IEnumerator Hud_EngineerPanelShowsLockAndCost() {
      yield return StartMatch();
      yield return WaitForHud();
      var hud = Controller().Hud;
      Controller().Input.Selection.SelectUnit(Engineers(0)[0]);
      hud.Refresh(Controller().Frame);
      Assert.AreEqual(10, hud.Entries.Count, "사령부를 뺀 건물 전부");
      var depot = hud.Entries.First(e => e.Label.StartsWith("보급창"));
      Assert.IsTrue(depot.Enabled);
      Assert.IsTrue(depot.Label.Contains("철 100"), depot.Label);
      var airfield = hud.Entries.First(e => e.Label.StartsWith("비행장"));
      Assert.IsFalse(airfield.Enabled, "해금 전");
      Assert.IsTrue(airfield.Label.Contains("잠김"));

      hud.PressContext(hud.Entries.IndexOf(depot));
      Assert.IsTrue(Controller().Input.IsPlacing, "건물을 고르면 위치 지정");
    }

    [UnityTest]
    public IEnumerator Hud_BuildingPanelProducesAndEditsPreset() {
      yield return StartMatch(buildings: new[] { (0, "ff_barracks", -42, 12) });
      yield return WaitForHud();
      var hud = Controller().Hud;
      var barracks = BuildingAt(V(-42, 12));
      Controller().Input.Selection.SelectBuilding(barracks);
      hud.Refresh(Controller().Frame);

      hud.PressContext(hud.Entries.FindIndex(e => e.Label.StartsWith("레인저")));
      yield return WaitFrames(CommandFrames);
      Assert.AreEqual(1, BuildingState(barracks).QueueCount, "버튼으로 생산");

      hud.Refresh(Controller().Frame);
      hud.PressContext(hud.Entries.FindIndex(e => e.Label.StartsWith("프리셋 1")));
      yield return WaitFrames(CommandFrames);
      Assert.AreNotEqual(default(AssetRef<ModuleSpec>), BuildingState(barracks).Preset[0], "프리셋 변경");

      hud.Refresh(Controller().Frame);
      hud.PressContext(hud.Entries.FindIndex(e => e.Label.StartsWith("생산")));
      yield return WaitFrames(CommandFrames);
      Assert.AreEqual(0, BuildingState(barracks).QueueCount, "취소");
    }

    [UnityTest]
    public IEnumerator Hud_ProcessorSwitch() {
      yield return StartMatch(buildings: Processor);
      yield return WaitForHud();
      var hud = Controller().Hud;
      var processor = BuildingAt(V(-42, 12));
      Controller().Input.Selection.SelectBuilding(processor);
      hud.Refresh(Controller().Frame);
      hud.PressContext(hud.Entries.FindIndex(e => e.Label.Contains("끄기")));
      yield return WaitFrames(CommandFrames);
      Assert.IsTrue((bool)BuildingState(processor).ProcessorOff);
    }

    [UnityTest]
    public IEnumerator Hud_RejectionBecomesAlert() {
      yield return StartMatch(stock: Stock(10, 10, 0, 0));
      yield return WaitForHud();
      yield return Build(0, "_power_plant", V(-20, 10));
      yield return WaitFrames(2);
      Assert.AreEqual("철·목재 부족", Controller().Hud.AlertText);
    }

    [UnityTest]
    public IEnumerator Hud_ReceivedNoteShowsAndStaysInInbox() {
      yield return StartMatch();
      yield return WaitForHud();
      yield return Send(1, new SendNoteCommand { PhraseId = 3, TargetMode = NoteTarget.Player, Receiver = 0 });
      Assert.IsTrue(Controller().Hud.NoteText.Contains(Texts.Phrases[3]), Controller().Hud.NoteText);
      Assert.AreEqual(1, Controller().Hud.NoteInbox.Count);
    }

    [UnityTest]
    public IEnumerator Hud_ButtonsMeetMinimumSizeAtEveryAspect() {
      yield return StartMatch();
      yield return WaitForHud();
      foreach (var button in Controller().Hud.GetComponentsInChildren<Button>(true)) {
        var rect = (RectTransform)button.transform;
        var size = rect.GetComponentInParent<GridLayoutGroup>() != null ? rect.GetComponentInParent<GridLayoutGroup>().cellSize : rect.sizeDelta;
        Assert.GreaterOrEqual(Mathf.Min(size.x, size.y), button.name == "DiplomacyButton" || button.name == "MenuButton" ? 76f : Ui.MinButtonSize, button.name);
      }
      var scaler = Controller().Hud.GetComponentInChildren<CanvasScaler>();
      Assert.AreEqual(1f, scaler.matchWidthOrHeight, "높이 기준이면 16:9~21:9에서 버튼 크기가 같다");
    }
  }
}
