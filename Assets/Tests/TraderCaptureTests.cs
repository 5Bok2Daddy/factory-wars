namespace FactoryWars.Tests {
  using System.Collections;
  using System.Linq;
  using NUnit.Framework;
  using Quantum;
  using UnityEngine;
  using UnityEngine.TestTools;
  using Assert = NUnit.Framework.Assert;

  /// <summary>중앙 암시장 캡처: 선택 안 함(깨끗한 전장) / 무기상 선택(회전 점선 고리 + 거래 패널, HUD 포함).</summary>
  public unsafe partial class LocalMatchTests {
    [UnityTest, Explicit, Category("CaptureUrban")]
    public IEnumerator CaptureTrader_SelectedAndClean() {
      yield return StartMatch(Map4PScene, 2, stock: SortieStock, units: new[] { (0, "ff_ranger_squad", 0, -18), (0, "ff_eagle_tank", 6, -20) }, truce: -1);
      yield return WaitForHud();
      yield return WaitForBattle();
      yield return Send(0, new Quantum.FactoryWars.ChooseDoctrineCommand { Doctrine = 1 });
      Time.timeScale = 1f;
      var camera = Controller().Camera;
      camera.MoveTo(new Vector2(0f, -2f));
      camera.Height = 31f;
      camera.Apply();
      yield return WaitFrames(Fps);
      ShootUrban("trader_clean");
      // 넓은 화면: 순환로에 이어진 길과 길가(가로등·전신주·엄폐·차량·숲).
      camera.MoveTo(new Vector2(-30f, -34f));
      camera.Height = 46f;
      camera.Apply();
      yield return WaitFrames(Fps / 2);
      ShootUrban("roadside_wide");
      camera.MoveTo(new Vector2(-34f, -30f));
      camera.Height = 22f;
      camera.Apply();
      yield return WaitFrames(Fps / 2);
      ShootUrban("roadside_close");
      camera.MoveTo(new Vector2(0f, -2f));
      camera.Height = 31f;
      camera.Apply();
      yield return WaitFrames(Fps / 2);
      Controller().Input.Selection.SelectBuilding(TraderEntity());
      Controller().Hud.Refresh(Controller().Frame);
      yield return WaitFrames(Fps);
      Assert.IsTrue(Battle().Trader.RingVisible, "선택하면 점선 고리");
      Assert.IsTrue(Controller().Hud.Entries.Any(e => e.Label.StartsWith("용병")), "거래 패널");
      var canvas = Controller().Hud.GetComponentInChildren<Canvas>();
      var mode = canvas.renderMode;
      canvas.renderMode = RenderMode.ScreenSpaceCamera;
      canvas.worldCamera = Camera.main;
      canvas.planeDistance = 1f;
      yield return null;
      ShootUrban("trader_selected");
      canvas.renderMode = mode;
      Controller().Input.Selection.Clear();
      yield return WaitFrames(2);
      Assert.IsFalse(Battle().Trader.RingVisible, "선택 해제하면 고리 숨김");
    }
  }
}
