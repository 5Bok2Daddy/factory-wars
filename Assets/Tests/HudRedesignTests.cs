namespace FactoryWars.Tests {
  using System.Collections;
  using System.Linq;
  using FactoryWars.View;
  using NUnit.Framework;
  using Quantum;
  using UnityEngine;
  using UnityEngine.TestTools;
  using UnityEngine.UI;
  using Assert = NUnit.Framework.Assert;
  using Button = UnityEngine.UI.Button;

  /// <summary>
  /// 2026-11-22 HUD 개편 상호작용 검사: 부대 펼침 판, 명령 표지(이동·공격), 쓰는 줄만큼 줄어드는 명령 패널,
  /// 궁극 조준 고리(실제 폭발 반경), UI 크기 설정. 화면 캡처가 아니라 실제 버튼·제스처 경로로 확인한다.
  /// </summary>
  public unsafe partial class LocalMatchTests {
    Button RootButton(string name) => Controller().Hud.Root.GetComponentsInChildren<Button>(false).FirstOrDefault(b => b.name == name);

    [UnityTest]
    public IEnumerator Hud_GroupFlyoutOpensSelectsAndCloses() {
      yield return StartMatch();
      yield return WaitForHud();
      var hud = Controller().Hud;
      var selection = Controller().Input.Selection;
      Assert.IsFalse(hud.GroupsOpen, "부대 판은 평소 접혀 있다");
      Assert.IsNull(RootButton("ArmyButton"), "접혀 있으면 전체 군대 버튼이 보이지 않는다");
      RootButton("GroupsToggle").onClick.Invoke();
      Assert.IsTrue(hud.GroupsOpen, "부대 버튼을 누르면 펼친다");
      RootButton("ArmyButton").onClick.Invoke();
      Assert.IsFalse(hud.GroupsOpen, "고르면 접힌다");
      Assert.Greater(selection.Units.Count, 0, "전체 군대 선택");

      RootButton("GroupsToggle").onClick.Invoke();
      RootButton("BoxButton").onClick.Invoke();
      Assert.IsTrue(hud.BoxMode, "범위 선택 켬");
      Assert.IsFalse(hud.GroupsOpen);
      Assert.AreEqual("범위 중", RootButton("GroupsToggle").GetComponentInChildren<Text>().text, "접힌 뒤에도 범위 선택 중임을 보인다");
      RootButton("GroupsToggle").onClick.Invoke();
      Assert.IsFalse(hud.BoxMode, "부대 버튼을 다시 누르면 범위 선택을 끈다");
      Assert.IsFalse(hud.GroupsOpen);
      yield return null;
    }

    [UnityTest]
    public IEnumerator Input_TapOrdersShowMoveAndAttackMarkers() {
      yield return StartMatch(truce: 0, units: new[] { (0, "ff_eagle_tank", -20, 23), (1, "ff_eagle_tank", -8, 23) });
      yield return WaitForHud();
      var controller = Controller();
      var mine = UnitsNamed(0, "ff_eagle_tank").First(e => Vector3.Distance(Verified.Get<Transform3D>(e).Position.ToUnityVector3(), new Vector3(-20, 0, 23)) < 3f);
      var enemy = UnitsNamed(1, "ff_eagle_tank").First();
      controller.Camera.MoveTo(new Vector2(-14, 23));
      yield return WaitFrames(3);
      controller.Input.Selection.SelectUnit(mine);
      Vector2 Screen(Vector3 world) => Camera.main.WorldToScreenPoint(world);

      controller.Handle(new Gesture { Kind = GestureKind.Tap, Position = Screen(new Vector3(-20, 0, 32)) });
      Assert.AreEqual("move", controller.LastMarker.Kind, "빈 땅 탭 → 이동 표지");
      Assert.Less(Vector3.Distance(controller.LastMarker.Position, new Vector3(-20, 0, 32)), 1.5f, "탭한 자리");

      var enemyAt = Verified.Get<Transform3D>(enemy).Position.ToUnityVector3();
      controller.Handle(new Gesture { Kind = GestureKind.Tap, Position = Screen(enemyAt) });
      Assert.AreEqual("attack", controller.LastMarker.Kind, "적 탭 → 공격 표지(대상 자리)");
      Assert.Less(Vector3.Distance(controller.LastMarker.Position, enemyAt), 1f);
      yield return WaitFrames(Fps / 2);
      Assert.AreEqual(enemy, Verified.Get<Unit>(mine).TargetEntity, "공격 명령이 시뮬레이션에 들어갔다");
    }

    [UnityTest]
    public IEnumerator Hud_ContextPanelUsesOnlyNeededRows() {
      yield return StartMatch();
      yield return WaitForHud();
      var hud = Controller().Hud;
      var panel = (RectTransform)hud.Root.Find("ContextPanel");
      Controller().Input.Selection.SelectBuilding(Headquarters(0));
      hud.Refresh(Controller().Frame);
      var rows = Mathf.CeilToInt(hud.Entries.Count / 3f);
      var expected = 42f + 2 * 12f + rows * Ui.MinButtonSize + (rows - 1) * 8f;
      Assert.AreEqual(expected, panel.rect.height, 0.5f, $"명령 {hud.Entries.Count}개 → {rows}줄 높이");
      var header = panel.Find("CommandHeader").GetComponentInChildren<Text>().text;
      Assert.IsNotEmpty(header, "제목줄이 비지 않는다");

      Controller().Input.Selection.SelectUnit(Engineers(0)[0]);
      hud.Refresh(Controller().Frame);
      rows = Mathf.CeilToInt(hud.Entries.Count / 3f);
      Assert.AreEqual(42f + 2 * 12f + rows * Ui.MinButtonSize + (rows - 1) * 8f, panel.rect.height, 0.5f, "공병 명령 줄 수");
    }

    [UnityTest, Timeout(300000)]
    public IEnumerator Ultimate_AimRingShowsRealBlastRadiusAndTapLaunches() {
      ShortenUltimates(prepare: 6);
      yield return StartMatch(swapFactions: true, buildings: UltimateBase(0, "cf", "cf_missile_silo"), stock: Stock(20000, 20000, 20000, 1000));
      yield return WaitForHud();
      yield return PrepareUltimate(0, "cf", UltimatePhase.Ready);
      var controller = Controller();
      var battle = Object.FindAnyObjectByType<BattleView>();
      Assert.AreEqual(0f, battle.UltimateAim.Radius, "조준 전에는 고리 없음");
      controller.Input.Mode = MatchInput.TapMode.Ultimate;
      yield return WaitFrames(3);
      Assert.AreEqual(UltimateOf(0).BlastRadius.AsFloat, battle.UltimateAim.Radius, 0.01f, "고리 반경 = 실제 폭발 반경");
      // 화면 가운데(고리 자리)를 누르면 그 자리로 발사된다.
      var aim = battle.UltimateAim.Position;
      controller.Handle(new Gesture { Kind = GestureKind.Tap, Position = Camera.main.WorldToScreenPoint(aim) });
      Assert.AreEqual(MatchInput.TapMode.None, controller.Input.Mode, "한 번 쓰면 조준 해제");
      Assert.AreEqual("LaunchUltimateCommand", controller.LastMarker.Kind, "발사 표지");
      yield return WaitFor(() => Verified.ComponentCount<MissileStrike>() == 1, 10f, "발사");
      yield return WaitFrames(3);
      Assert.AreEqual(0f, battle.UltimateAim.Radius, "발사 뒤 고리 사라짐");
    }

    [UnityTest]
    public IEnumerator Ui_ScaleOptionEnlargesHudAndRestores() {
      PlayerPrefs.SetInt(PlayerOptions.UiScaleKey, 0);
      Restores.Add(() => {
        PlayerPrefs.SetInt(PlayerOptions.UiScaleKey, 0);
        Ui.ApplyScale();
      });
      yield return StartMatch();
      yield return WaitForHud();
      var scaler = Controller().Hud.GetComponentInChildren<CanvasScaler>();
      Assert.AreEqual(1080f, scaler.referenceResolution.y, 0.5f, "기본 100%");
      PlayerOptions.NextUiScale();
      Assert.AreEqual(1080f / 1.15f, scaler.referenceResolution.y, 0.5f, "115%: 같은 화면에 더 크게");
      Assert.AreEqual("UI 크기: 115%", PlayerOptions.UiScaleLabel);
      PlayerOptions.NextUiScale();
      PlayerOptions.NextUiScale();
      Assert.AreEqual(1080f, scaler.referenceResolution.y, 0.5f, "한 바퀴 돌면 100%");
      Assert.AreEqual(1f, scaler.matchWidthOrHeight, "높이 기준 유지");
    }
  }
}
