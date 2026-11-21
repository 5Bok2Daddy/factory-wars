namespace FactoryWars.Tests {
  using System.Collections;
  using System.IO;
  using System.Linq;
  using FactoryWars.View;
  using NUnit.Framework;
  using Quantum;
  using UnityEngine;
  using UnityEngine.TestTools;
  using Assert = NUnit.Framework.Assert;

  /// <summary>
  /// HUD 화면 캡처(Strategy UI Kit 적용 전후 비교): 공병 선택(건설 명령), 사령부 선택(생산), 전차 다수 선택을
  /// 16:9(1920x1080), 19.5:9(2340x1080), 20:9(2400x1080) 가로 화면으로 찍는다. FW_UI_SHOT_TAG(before/after)로 폴더를 나눈다.
  /// </summary>
  public unsafe partial class LocalMatchTests {
    static readonly (string Name, int Width, int Height)[] UiAspects = { ("16x9", 1920, 1080), ("19.5x9", 2340, 1080), ("20x9", 2400, 1080) };

    static string UiShotFolder() {
      var tag = System.Environment.GetEnvironmentVariable("FW_UI_SHOT_TAG");
      var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "DevelopmentReports", "Screenshots", "StrategyUiKit", string.IsNullOrEmpty(tag) ? "current" : tag));
      Directory.CreateDirectory(dir);
      return dir;
    }

    IEnumerator ShootHud(string name, int width, int height) {
      // HUD뿐 아니라 일시정지·설정처럼 다른 캔버스에 있는 창도 찍도록 켜진 화면 오버레이 캔버스를 모두 카메라에 붙인다.
      var canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)
        .Where(c => c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay && c.isActiveAndEnabled)
        .OrderBy(c => c.sortingOrder).ToArray();
      var camera = Camera.main;
      var surface = new RenderTexture(width, height, 24);
      camera.targetTexture = surface;
      for (var i = 0; i < canvases.Length; i++) {
        canvases[i].renderMode = RenderMode.ScreenSpaceCamera;
        canvases[i].worldCamera = camera;
        canvases[i].planeDistance = 2f - i * 0.1f;
      }
      yield return null;
      Controller().Hud.Refresh(Controller().Frame);
      Canvas.ForceUpdateCanvases();
      yield return null;
      camera.Render();
      RenderTexture.active = surface;
      var image = new Texture2D(width, height, TextureFormat.RGB24, false);
      image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
      image.Apply();
      RenderTexture.active = null;
      File.WriteAllBytes(Path.Combine(UiShotFolder(), name + ".png"), image.EncodeToPNG());
      Object.Destroy(image);
      camera.targetTexture = null;
      surface.Release();
      foreach (var canvas in canvases) {
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
      }
      yield return null;
    }

    [UnityTest, Timeout(600000)]
    public IEnumerator UiKit_CaptureHudAtLandscapeAspects() {
      var tanks = Enumerable.Range(0, 6).Select(i => (0, "ff_eagle_tank", -112 + i * 3, -100)).ToArray();
      yield return StartMatch(Map4PScene, 2, truce: 300, units: tanks, stock: Stock(4000, 3000, 2000, 120));
      yield return WaitForHud();
      yield return WaitForBattle();
      // 도크트린 선택 창은 화면 버튼으로 고른다(사용자와 같은 경로).
      var doctrine = GameObject.Find("DoctrinePanel");
      if (doctrine != null) {
        doctrine.GetComponentsInChildren<UnityEngine.UI.Button>(true).First().onClick.Invoke();
        yield return WaitFrames(Fps / 2);
      }
      var start = Quantum.FactoryWars.FactoryWarsUtil.StartPosition(Verified, 0);
      Controller().Camera.MoveTo(new Vector2(start.X.AsFloat + 14f, start.Z.AsFloat + 14f));
      yield return WaitFrames(Fps);

      var input = Controller().Input;
      input.Selection.SelectUnit(Engineers(0)[0]);
      yield return WaitFrames(5);
      foreach (var (aspect, width, height) in UiAspects) yield return ShootHud($"engineer_{aspect}", width, height);

      var hq = All<Building>().First(b => b.Component.Owner == 0 && Verified.FindAsset(b.Component.Spec).Role == BuildingRole.Headquarters).Entity;
      input.Selection.SelectBuilding(hq);
      yield return WaitFrames(5);
      foreach (var (aspect, width, height) in UiAspects) yield return ShootHud($"headquarters_{aspect}", width, height);

      input.Selection.SelectUnits(UnitsNamed(0, "ff_eagle_tank"));
      yield return WaitFrames(5);
      foreach (var (aspect, width, height) in UiAspects) yield return ShootHud($"tanks_{aspect}", width, height);
      Assert.AreEqual(9, new[] { "engineer", "headquarters", "tanks" }.Sum(s => UiAspects.Count(a => File.Exists(Path.Combine(UiShotFolder(), $"{s}_{a.Name}.png")))));

      // 창(모달): 사령부 유닛 연구 메뉴, 외교, 일시정지, 설정. 화면 버튼과 같은 경로로 연다.
      input.Selection.SelectBuilding(hq);
      yield return WaitFrames(5);
      Controller().Hud.Refresh(Controller().Frame);
      var research = Controller().Hud.Root.GetComponentsInChildren<UnityEngine.UI.Button>()
        .FirstOrDefault(b => b.GetComponentInChildren<UnityEngine.UI.Text>()?.text.StartsWith("유닛 연구") == true);
      Assert.IsNotNull(research, "사령부 명령판에 유닛 연구 버튼 없음: " + string.Join(" | ", Controller().Hud.Entries.Select(e => e.Label?.Replace("\n", " "))));
      research.onClick.Invoke();
      yield return WaitFrames(5);
      foreach (var (aspect, width, height) in UiAspects) yield return ShootHud($"research_{aspect}", width, height);
      input.Selection.Clear();
      yield return WaitFrames(3);

      Controller().Hud.ToggleDiplomacy();
      yield return WaitFrames(3);
      Assert.IsTrue(Controller().Hud.DiplomacyOpen);
      foreach (var (aspect, width, height) in UiAspects) yield return ShootHud($"diplomacy_{aspect}", width, height);
      Controller().Hud.ToggleDiplomacy();

      Controller().TogglePause();
      yield return null;
      var pause = GameObject.Find("PauseMenu");
      Assert.IsNotNull(pause, "일시정지 창이 열리지 않음");
      foreach (var (aspect, width, height) in UiAspects) yield return ShootHud($"pause_{aspect}", width, height);
      pause.GetComponentsInChildren<UnityEngine.UI.Button>().First(b => b.name == "설정").onClick.Invoke();
      yield return null;
      Assert.IsNotNull(GameObject.Find("Settings"), "설정 창이 열리지 않음");
      foreach (var (aspect, width, height) in UiAspects) yield return ShootHud($"settings_{aspect}", width, height);
      GameObject.Find("Settings").SetActive(false);
      Controller().TogglePause();
      yield return null;
      Assert.IsNull(GameObject.Find("PauseMenu"), "일시정지 창이 닫히지 않음");
    }
  }
}
