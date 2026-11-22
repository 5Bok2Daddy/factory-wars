namespace FactoryWars.Tests {
  using System.Collections;
  using System.Collections.Generic;
  using System.IO;
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
  /// HUD 화면 캡처(전후 비교): 준비 단계(선택 없음), 공병, 건설 위치 지정, 사령부 생산 대기열, 전차 1기, 전차 6기, 연구 메뉴,
  /// 외교, 일시정지, 설정, 궁극 목표 지정을 16:9(1920x1080), 19.5:9(2340x1080), 20:9(2400x1080)으로 찍는다.
  /// FW_UI_SHOT_TAG로 폴더를 나눈다. 장면마다 HUD가 덮는 화면 비율(가림률)을 coverage.csv에 남긴다.
  /// </summary>
  public unsafe partial class LocalMatchTests {
    static readonly (string Name, int Width, int Height)[] UiAspects = { ("16x9", 1920, 1080), ("19.5x9", 2340, 1080), ("20x9", 2400, 1080) };

    static string UiShotFolder() {
      var tag = System.Environment.GetEnvironmentVariable("FW_UI_SHOT_TAG");
      var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "DevelopmentReports", "Screenshots", "StrategyUiKit", string.IsNullOrEmpty(tag) ? "current" : tag));
      Directory.CreateDirectory(dir);
      return dir;
    }

    /// <summary>
    /// HUD가 덮는 화면 비율. 켜진 그림(Image·RawImage·Text, 거의 투명한 것과 화면 전체 뒤판 제외)의 합집합을 96x54 격자로 센다.
    /// </summary>
    static float HudCoverage(RectTransform root) {
      const int W = 96, H = 54;
      var grid = new bool[W, H];
      var size = root.rect.size;
      var corners = new Vector3[4];
      foreach (var graphic in root.GetComponentsInChildren<Graphic>(false)) {
        if (graphic.color.a < 0.05f || graphic.canvasRenderer.cull) {
          continue;
        }
        if (graphic is Text text && string.IsNullOrEmpty(text.text)) {
          continue;
        }
        graphic.rectTransform.GetWorldCorners(corners);
        var a = root.InverseTransformPoint(corners[0]);
        var b = root.InverseTransformPoint(corners[2]);
        var min = new Vector2(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y)) - root.rect.min;
        var max = new Vector2(Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y)) - root.rect.min;
        if (max.x - min.x > size.x * 0.95f && max.y - min.y > size.y * 0.95f) {
          continue;
        }
        var x0 = Mathf.Clamp(Mathf.FloorToInt(min.x / size.x * W), 0, W);
        var x1 = Mathf.Clamp(Mathf.CeilToInt(max.x / size.x * W), 0, W);
        var y0 = Mathf.Clamp(Mathf.FloorToInt(min.y / size.y * H), 0, H);
        var y1 = Mathf.Clamp(Mathf.CeilToInt(max.y / size.y * H), 0, H);
        for (var x = x0; x < x1; x++) {
          for (var y = y0; y < y1; y++) {
            grid[x, y] = true;
          }
        }
      }
      var covered = 0;
      foreach (var cell in grid) {
        if (cell) {
          covered++;
        }
      }
      return covered / (float)(W * H);
    }

    readonly List<string> _coverage = new List<string>();

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
      _coverage.Add($"{name},{HudCoverage(Controller().Hud.Root):0.000}");
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

    IEnumerator ShootAll(string scene) {
      foreach (var (aspect, width, height) in UiAspects) {
        yield return ShootHud($"{scene}_{aspect}", width, height);
      }
    }

    void WriteCoverage(string file) {
      File.WriteAllLines(Path.Combine(UiShotFolder(), file), new[] { "shot,coverage" }.Concat(_coverage));
      _coverage.Clear();
    }

    Button HudButtonLabeled(string prefix) => Controller().Hud.Root.GetComponentsInChildren<Button>()
      .FirstOrDefault(b => b.GetComponentInChildren<Text>()?.text.StartsWith(prefix) == true);

    [UnityTest, Timeout(600000)]
    public IEnumerator UiKit_CaptureHudAtLandscapeAspects() {
      _coverage.Clear();
      // 생산 대기열 장면이 인구 한도에 막히지 않게 전차는 3기, 보급창 2채로 인구 한도를 올린다.
      var tanks = Enumerable.Range(0, 3).Select(i => (0, "ff_eagle_tank", -112 + i * 4, -100)).ToArray();
      yield return StartMatch(Map4PScene, 2, truce: 300, units: tanks, stock: Stock(4000, 3000, 2000, 120),
        buildings: new[] { (0, "ff_supply_depot", -101, -89), (0, "ff_supply_depot", -115, -104) });
      yield return WaitForHud();
      yield return WaitForBattle();
      // 도크트린 선택 창은 화면 버튼으로 고른다(사용자와 같은 경로).
      var doctrine = GameObject.Find("DoctrinePanel");
      if (doctrine != null) {
        doctrine.GetComponentsInChildren<Button>(true).First().onClick.Invoke();
        yield return WaitFrames(Fps / 2);
      }
      var start = Quantum.FactoryWars.FactoryWarsUtil.StartPosition(Verified, 0);
      Controller().Camera.MoveTo(new Vector2(start.X.AsFloat + 14f, start.Z.AsFloat + 14f));
      yield return WaitFrames(Fps);
      var input = Controller().Input;
      var hud = Controller().Hud;

      // 준비 단계, 선택 없음.
      input.Selection.Clear();
      yield return WaitFrames(3);
      yield return ShootAll("prep_none");

      input.Selection.SelectUnit(Engineers(0)[0]);
      yield return WaitFrames(5);
      yield return ShootAll("engineer");

      // 건설: 산업·보급 → 보급창을 누르면 위치 지정 모드.
      hud.Refresh(Controller().Frame);
      hud.PressContext(hud.Entries.FindIndex(e => e.Label == "산업·보급"));
      hud.Refresh(Controller().Frame);
      hud.PressContext(hud.Entries.FindIndex(e => e.Label.StartsWith(Texts.NameOf("ff_supply_depot"))));
      Assert.IsTrue(input.IsPlacing, "건물을 고르면 위치 지정");
      yield return WaitFrames(3);
      yield return ShootAll("construction");
      input.Placing = default;
      yield return WaitFrames(2);

      // 사령부: 공병 3기를 대기열에 넣고 고른다.
      var hq = All<Building>().First(b => b.Component.Owner == 0 && Verified.FindAsset(b.Component.Spec).Role == BuildingRole.Headquarters).Entity;
      input.Selection.SelectBuilding(hq);
      hud.Refresh(Controller().Frame);
      var engineerEntry = hud.Entries.FindIndex(e => e.Label.StartsWith(Texts.NameOf("ff_engineer")));
      Assert.GreaterOrEqual(engineerEntry, 0, "사령부 생산 칸: " + string.Join(" | ", hud.Entries.Select(e => e.Label?.Replace("\n", " "))));
      for (var i = 0; i < 3; i++) {
        hud.PressContext(engineerEntry);
      }
      yield return WaitFor(() => Verified.Get<Building>(hq).QueueCount > 0, 10f, $"생산 대기열(경고: {hud.AlertText}, 상단 {hud.TopBarText})");
      yield return ShootAll("headquarters");

      input.Selection.SelectUnit(UnitsNamed(0, "ff_eagle_tank")[0]);
      yield return WaitFrames(5);
      yield return ShootAll("tank");

      input.Selection.SelectUnits(UnitsNamed(0, "ff_eagle_tank"));
      yield return WaitFrames(5);
      yield return ShootAll("tanks");
      WriteCoverage("coverage_battle.csv");

      // 창(모달): 사령부 유닛 연구 메뉴, 외교, 일시정지, 설정. 화면 버튼과 같은 경로로 연다.
      input.Selection.SelectBuilding(hq);
      yield return WaitFrames(5);
      hud.Refresh(Controller().Frame);
      var research = HudButtonLabeled("유닛 연구");
      Assert.IsNotNull(research, "사령부 명령판에 유닛 연구 버튼 없음: " + string.Join(" | ", hud.Entries.Select(e => e.Label?.Replace("\n", " "))));
      research.onClick.Invoke();
      yield return WaitFrames(5);
      yield return ShootAll("research");
      input.Selection.Clear();
      yield return WaitFrames(3);

      hud.ToggleDiplomacy();
      yield return WaitFrames(3);
      Assert.IsTrue(hud.DiplomacyOpen);
      yield return ShootAll("diplomacy");
      hud.ToggleDiplomacy();

      Controller().TogglePause();
      yield return null;
      var pause = GameObject.Find("PauseMenu");
      Assert.IsNotNull(pause, "일시정지 창이 열리지 않음");
      yield return ShootAll("pause");
      pause.GetComponentsInChildren<Button>().First(b => b.name == "설정").onClick.Invoke();
      yield return null;
      Assert.IsNotNull(GameObject.Find("Settings"), "설정 창이 열리지 않음");
      yield return ShootAll("settings");
      GameObject.Find("Settings").SetActive(false);
      Controller().TogglePause();
      yield return null;
      Assert.IsNull(GameObject.Find("PauseMenu"), "일시정지 창이 닫히지 않음");
      _coverage.Clear();
    }

    [UnityTest, Timeout(600000)]
    public IEnumerator UiKit_CaptureUltimateTargeting() {
      _coverage.Clear();
      ShortenUltimates(prepare: 6);
      yield return StartMatch(swapFactions: true, buildings: UltimateBase(0, "cf", "cf_missile_silo"), stock: Stock(20000, 20000, 20000, 1000));
      yield return WaitForHud();
      var doctrine = GameObject.Find("DoctrinePanel");
      if (doctrine != null) {
        doctrine.GetComponentsInChildren<Button>(true).First().onClick.Invoke();
        yield return WaitFrames(5);
      }
      yield return PrepareUltimate(0, "cf", UltimatePhase.Ready);
      var input = Controller().Input;
      var hud = Controller().Hud;
      var silo = OwnBuilding(0, "cf_missile_silo");
      input.Selection.SelectBuilding(silo);
      var pos = Verified.Get<Transform3D>(silo).Position;
      Controller().Camera.MoveTo(new Vector2(pos.X.AsFloat, pos.Z.AsFloat + 10f));
      yield return WaitFrames(5);
      yield return ShootAll("ultimate_ready");
      hud.Refresh(Verified);
      hud.PressContext(hud.Entries.FindIndex(e => e.Label != null && e.Label.Contains("발사")));
      Assert.AreEqual(MatchInput.TapMode.Ultimate, input.Mode, "목표 지정 모드");
      yield return WaitFrames(5);
      yield return ShootAll("ultimate_targeting");
      WriteCoverage("coverage_ultimate.csv");
      input.Mode = MatchInput.TapMode.None;
    }
  }
}
