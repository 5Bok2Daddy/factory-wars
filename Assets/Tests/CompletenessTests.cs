namespace FactoryWars.Tests {
  using System.Collections;
  using System.Linq;
  using FactoryWars.View;
  using NUnit.Framework;
  using Photon.Deterministic;
  using Quantum;
  using Quantum.FactoryWars;
  using UnityEngine;
  using UnityEngine.SceneManagement;
  using UnityEngine.TestTools;
  using Assert = NUnit.Framework.Assert;

  /// <summary>항목 30-3: 모바일 RTS 기본 조작(집결·위치 고수·순찰·유휴 공병·전체 군대·알림 이동), 결과 통계, 튜토리얼, 소리 칸.</summary>
  public unsafe partial class LocalMatchTests {
    [UnityTest]
    public IEnumerator Control_RallyPointSendsNewUnits() {
      yield return StartMatch(buildings: new[] { (0, "ff_barracks", -30, -20) }, stock: Stock(5000, 5000, 5000, 200));
      var entity = OwnBuilding(0, "ff_barracks");
      Assert.AreNotEqual(EntityRef.None, entity);
      var rally = V(-10, -20);
      yield return Send(0, new RallyCommand { Building = entity, Position = rally });
      Assert.IsTrue(Verified.Get<Building>(entity).HasRally, "집결 지점 설정");
      var unitRef = Verified.FindAsset(Verified.Get<Building>(entity).Spec).Producible[0];
      var before = Verified.ComponentCount<Unit>();
      yield return Send(0, new ProduceCommand { Building = entity, Unit = unitRef, Count = 1 });
      yield return WaitFor(() => Verified.ComponentCount<Unit>() > before, 60f, "생산");
      yield return WaitFrames(15 * Fps);
      var spec = Verified.FindAsset(unitRef);
      var arrived = UnitsNamed(0, spec.name).Any(u => (Verified.Get<Transform3D>(u).Position - rally).Magnitude < 6);
      Assert.IsTrue(arrived, "새 유닛이 집결 지점으로 감");
      yield return Send(0, new RallyCommand { Building = entity, Clear = true });
      Assert.IsFalse(Verified.Get<Building>(entity).HasRally, "집결 해제");
    }

    [UnityTest]
    public IEnumerator Control_HoldDoesNotChaseAndPatrolTurnsAround() {
      yield return StartMatch();
      var tank = UnitsNamed(0, "ff_eagle_tank")[0];
      yield return Send(0, new HoldUnitsCommand { Units = new[] { tank } });
      Assert.AreEqual(UnitOrder.Hold, Verified.Get<Unit>(tank).Order);
      var held = Verified.Get<Transform3D>(tank).Position;
      yield return WaitFrames(3 * Fps);
      Assert.Less((Verified.Get<Transform3D>(tank).Position - held).Magnitude.AsFloat, 0.5f, "위치 고수는 제자리");

      var start = Verified.Get<Transform3D>(tank).Position;
      var end = start + new FPVector3(12, 0, 0);
      yield return Send(0, new PatrolUnitsCommand { Units = new[] { tank }, Target = end });
      Assert.AreEqual(UnitOrder.Patrol, Verified.Get<Unit>(tank).Order);
      yield return WaitFor(() => (Verified.Get<Unit>(tank).TargetPosition - start).Magnitude < 1, 60f, "순찰 끝에서 되돌아옴");
      Assert.AreEqual(UnitOrder.Patrol, Verified.Get<Unit>(tank).Order, "계속 순찰");
    }

    [UnityTest]
    public IEnumerator Hud_IdleEngineerArmyAndAlertJump() {
      yield return StartMatch();
      yield return WaitForHud();
      var hud = Controller().Hud;
      hud.SelectIdleEngineer();
      Assert.AreEqual(1, Controller().Input.Selection.Units.Count, "유휴 공병 하나");
      Assert.IsTrue(Verified.Has<Worker>(Controller().Input.Selection.Units[0]));
      hud.SelectArmy();
      Assert.Greater(Controller().Input.Selection.Units.Count, 0, "전체 군대");
      Assert.IsTrue(Controller().Input.Selection.Units.All(e => Verified.Has<Worker>(e) == false), "공병 제외");
      // 전투 유닛 선택: 명령 버튼과 상성 한 줄.
      hud.Refresh(Controller().Frame);
      Assert.IsTrue(hud.Entries.Any(e => e.Label.Contains("공격")), "공격 이동 버튼");
      Assert.IsTrue(hud.Entries.Any(e => e.Label.Contains("고수")), "위치 고수 버튼");
      Assert.IsTrue(hud.Entries.Any(e => e.Label.Contains("순찰")), "순찰 버튼");
      var tankSpec = Verified.FindAsset(Verified.Get<Unit>(UnitsNamed(0, "ff_eagle_tank")[0]).Spec);
      var tip = Texts.CounterTip(tankSpec, Verified.FindAsset(Verified.RuntimeConfig.Rules));
      StringAssert.Contains("강함", tip);
      StringAssert.Contains("조심", tip);
      // 알림 누르면 카메라가 그 자리로.
      hud.ShowAlert("시험");
      var field = typeof(MatchHud).GetField("_alertAt", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
      field.SetValue(hud, (Vector3?)new Vector3(30, 0, 30));
      hud.JumpToAlert();
      Assert.Less((Controller().Camera.Focus - new Vector2(30, 30)).magnitude, 15f, "알림 위치로 이동");
    }

    static EntityRef OwnBuilding(int owner, string name) {
      foreach (var pair in Verified.GetComponentIterator<Building>()) {
        if (pair.Component.Owner == owner && Verified.FindAsset(pair.Component.Spec).name == name) {
          return pair.Entity;
        }
      }
      return EntityRef.None;
    }

    [Test]
    public void Build_PanelFitsAllFactionBuildings() {
      // 항목 31·32: 명령 카드 3x3에서 건설 하위 메뉴(기본·2단계·3단계·방어)마다 8칸 + 뒤로.
      var all = Resources.LoadAll<BuildingSpec>("FactoryWars");
      foreach (var faction in Resources.LoadAll<FactionSpec>("FactoryWars")) {
        var specs = faction.Buildings.Select(b => all.First(s => s.Guid == b.Id)).ToList();
        foreach (var group in specs.GroupBy(s => s.Defensive ? 0 : Mathf.Max(1, s.RequiredTier))) {
          Assert.LessOrEqual(group.Count(), 8, $"{faction.name} 묶음 {group.Key}");
        }
      }
    }

    [Test]
    public void Result_FullSummaryHasStats() {
      var state = new PlayerState { UnitsProduced = 40, UnitsLost = 12, BuildingsLost = 3, UnitsKilled = 25, BuildingsDestroyed = 7, Captures = 2, CaptureFrames = 20 * 95, CratesPicked = 4 };
      state.Earned.Iron = 3200;
      var text = MatchFlow.FullSummary(true, 754f, state, 20);
      foreach (var part in new[] { "승리", "12:34", "처치 25", "파괴한 건물 7", "철 3200", "점령 2회 (1:35)", "보급 상자 4" }) {
        StringAssert.Contains(part, text);
      }
    }

    [Test]
    public void Audio_EmptySlotsAreSilentButHapticsCount() {
      var saved = AudioCatalog.Current;
      var catalog = ScriptableObject.CreateInstance<AudioCatalog>();
      catalog.Slots = AudioCatalog.DefaultSlots();
      AudioCatalog.Current = catalog;
      try {
        var played = GameAudio.Played;
        var vibrated = GameAudio.Vibrated;
        PlayerOptions.Vibration = true;
        GameAudio.Play(SoundEvent.Collapse);
        GameAudio.Play(SoundEvent.Click);
        Assert.AreEqual(played, GameAudio.Played, "소리 파일이 없으면 무음");
        Assert.AreEqual(vibrated + 1, GameAudio.Vibrated, "붕괴는 진동");
      } finally {
        AudioCatalog.Current = saved;
      }
      Assert.IsNotNull(Resources.Load<AudioCatalog>(AudioCatalog.ResourcePath), "Resources/FactoryWars/Audio/AudioCatalog");
    }

    [UnityTest]
    public IEnumerator Tutorial_StartsFromMenuAndStepsAdvance() {
      yield return OpenMenu();
      var menu = Object.FindAnyObjectByType<MenuController>();
      menu.StartTutorial();
      yield return WaitFor(() => Game?.Frames?.Verified != null && Verified.ComponentCount<Building>() >= 2, 30f, "튜토리얼 시작");
      yield return WaitFor(() => Object.FindAnyObjectByType<Tutorial>() != null, 10f, "튜토리얼 붙음");
      var tutorial = Object.FindAnyObjectByType<Tutorial>();
      Assert.IsTrue(FactoryWarsUtil.InTruce(Verified), "휴전 중 연습");
      Assert.AreEqual(BotDifficulty.Easy, StateOf(1).Difficulty);
      yield return WaitFor(() => tutorial.CurrentText.StartsWith("1/7"), 5f, "1단계");
      // 1단계: 카메라 이동.
      Controller().Camera.MoveTo(Controller().Camera.Focus + new Vector2(30, 0));
      yield return WaitFor(() => tutorial.Index == 1, 5f, "카메라 단계 통과");
      // 2단계: 공병 선택.
      Controller().Input.Selection.SelectUnit(Engineers(0)[0]);
      yield return WaitFor(() => tutorial.Index == 2, 5f, "선택 단계 통과");
      // 나머지는 건너뛰기 버튼.
      tutorial.SkipAll();
      Assert.IsTrue(tutorial.Finished);
      MatchFlow.Current.Leave();
      yield return WaitFor(() => SceneManager.GetActiveScene().name == MenuController.SceneName, 10f, "복귀");
    }
  }
}
