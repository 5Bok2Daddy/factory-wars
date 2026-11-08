namespace FactoryWars.Tests {
  using System.Collections;
  using System.Linq;
  using FactoryWars.View;
  using NUnit.Framework;
  using Assert = NUnit.Framework.Assert;
  using Quantum;
  using Quantum.FactoryWars;
  using UnityEngine;
  using UnityEngine.SceneManagement;
  using UnityEngine.TestTools;

  /// <summary>시작 화면, 경기 조건, 일시정지, 결과 화면.</summary>
  public unsafe partial class LocalMatchTests {
    const string MenuScene = "Assets/QuantumUser/Scenes/Menu.unity";

    [Test]
    public void Settings_KeepValidCombinations() {
      var settings = new MatchSettings { Mode = MatchMode.Duel, Bots = 3 };
      settings.Normalize();
      Assert.AreEqual(MatchMode.Duel, settings.Mode, "1대1");
      Assert.AreEqual(1, settings.Bots);

      settings = new MatchSettings { Mode = MatchMode.Team, Bots = 1 };
      settings.Normalize();
      Assert.AreEqual(3, settings.Bots, "팀전은 4명");
      Assert.IsTrue(settings.DiagonalTeams, "기본은 대각선 동맹");

      settings = new MatchSettings { Mode = MatchMode.FreeForAll, Bots = 1 };
      settings.Normalize();
      Assert.AreEqual(2, settings.Bots, "자유 대전은 3인 이상");
      Assert.AreEqual("Map4P", settings.Scene);
    }

    [Test]
    public void Result_SummaryShowsTimeAndCounts() {
      var text = MatchFlow.Summary(true, 754f, 40, 12, 3);
      Assert.IsTrue(text.Contains("승리"), text);
      Assert.IsTrue(text.Contains("12:34"), text);
      Assert.IsTrue(text.Contains("생산 40"), text);
      Assert.IsTrue(text.Contains("유닛 손실 12"), text);
      Assert.IsTrue(text.Contains("건물 손실 3"), text);
      Assert.IsTrue(MatchFlow.Summary(false, 5f, 0, 0, 0).Contains("0:05"));
    }

    IEnumerator OpenMenu() {
      Time.timeScale = TimeScale;
#if UNITY_EDITOR
      yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(MenuScene, new LoadSceneParameters(LoadSceneMode.Single));
#else
      yield return null;
#endif
      yield return WaitFor(() => Object.FindAnyObjectByType<MenuController>() != null, 10f, "시작 화면");
    }

    [UnityTest]
    public IEnumerator Menu_WalksScreensAndStartsDuelAgainstBot() {
      yield return OpenMenu();
      var menu = Object.FindAnyObjectByType<MenuController>();
      Assert.AreEqual(MenuController.Screen.Title, menu.Current);
      menu.Show(MenuController.Screen.Faction);
      menu.PickFaction(1);
      Assert.AreEqual(MenuController.Screen.Options, menu.Current);
      menu.NextDifficulty();
      Assert.AreEqual(BotDifficulty.Easy, menu.Settings.Difficulty);
      StringAssert.Contains("대각선", menu.NoticeText);
      menu.Begin();

      yield return WaitFor(() => Game?.Frames?.Verified != null && Verified.ComponentCount<Building>() >= 2, 30f, "대전 시작");
      yield return WaitFrames(CommandFrames);
      Assert.AreEqual("Map4P", SceneManager.GetActiveScene().name);
      Assert.AreEqual("continental_federation", FactionOf(0).FactionId, "고른 진영");
      Assert.AreEqual("free_federation", FactionOf(1).FactionId, "상대는 다른 진영");
      Assert.IsTrue((bool)StateOf(1).IsBot);
      Assert.IsFalse((bool)StateOf(0).IsBot);
      Assert.AreEqual(BotDifficulty.Easy, StateOf(1).Difficulty);
      Assert.AreEqual(500, Funds(0), "실제 맵 시작 철");
      Assert.AreEqual(4, Engineers(0).Count, "시작 공병 4");
      Assert.AreEqual(4, Army(0, false).Count, "전투 유닛 없이 시작");
    }

    [UnityTest]
    public IEnumerator Menu_StartsFourPlayerTeamMatch() {
      yield return OpenMenu();
      var menu = Object.FindAnyObjectByType<MenuController>();
      menu.Show(MenuController.Screen.Options);
      menu.NextMode();
      Assert.AreEqual(MatchMode.FreeForAll, menu.Settings.Mode);
      menu.NextBots();
      Assert.AreEqual("3인은 자리 유불리가 있을 수 있음", menu.NoticeText);
      menu.NextMode();
      Assert.AreEqual(MatchMode.Team, menu.Settings.Mode);
      menu.Begin();

      yield return WaitFor(() => Game?.Frames?.Verified != null && Verified.ComponentCount<Building>() >= 4, 30f, "대전 시작");
      yield return WaitFrames(CommandFrames);
      Assert.AreEqual(Relation.Allied, DiplomacySystem.GetRelation(Verified, 0, 2), "0·2번 대각선이 한 팀");
      Assert.AreEqual(Relation.Allied, DiplomacySystem.GetRelation(Verified, 1, 3), "1·3번 대각선이 한 팀");
      Assert.AreEqual(Relation.Enemy, DiplomacySystem.GetRelation(Verified, 0, 1));
    }

    [UnityTest]
    public IEnumerator Pause_StopsSimulationAndResumes() {
      yield return StartMatch();
      yield return WaitForHud();
      yield return WaitFor(() => MatchFlow.Current != null && MatchFlow.Current.ResultVisible == false, 5f, "흐름 준비");
      yield return null;
      MatchFlow.Current.TogglePause();
      yield return null;
      var frame = Verified.Number;
      var end = Time.realtimeSinceStartup + 0.5f;
      yield return WaitFor(() => Time.realtimeSinceStartup > end, 5f, "대기");
      Assert.AreEqual(frame, Verified.Number, "일시정지 중 프레임이 멈춤");

      MatchFlow.Current.TogglePause();
      yield return WaitFor(() => Verified.Number > frame, 5f, "재개");
    }

    [UnityTest]
    public IEnumerator Result_ShownWhenLocalPlayerLoses() {
      var tanks = Enumerable.Range(0, 12).Select(i => (1, "cf_dragonscale_tank", -48 + i * 2, -14)).ToArray();
      yield return StartMatch(units: tanks);
      yield return WaitForHud();
      yield return null;
      var headquarters = Headquarters(0);
      var attackers = UnitsNamed(1, "cf_dragonscale_tank").Where(e => Verified.Get<Transform3D>(e).Position.Z < -10).ToArray();
      Game.SendCommand(1, new AttackTargetCommand { Units = attackers, Target = headquarters });
      yield return WaitFor(() => MatchFlow.Current.ResultVisible, 90f, "결과 화면");
      Assert.IsTrue(MatchFlow.Current.ResultText.Contains("패배"), MatchFlow.Current.ResultText);
      Assert.IsTrue(MatchFlow.Current.ResultText.Contains("경기 시간"));

      MatchFlow.Current.Leave();
      yield return WaitFor(() => SceneManager.GetActiveScene().name == MenuController.SceneName, 10f, "시작 화면 복귀");
      Assert.IsNull(QuantumRunner.Default, "세션 종료");
    }
  }
}
