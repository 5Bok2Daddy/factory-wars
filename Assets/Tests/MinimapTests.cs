namespace FactoryWars.Tests {
  using System.Collections;
  using System.IO;
  using System.Linq;
  using FactoryWars.View;
  using NUnit.Framework;
  using Quantum;
  using Quantum.FactoryWars;
  using UnityEngine;
  using UnityEngine.TestTools;
  using Assert = NUnit.Framework.Assert;

  /// <summary>미니맵: 진영 영역(확장하면 넓어짐), 길·자원 마름모·카메라 사다리꼴, 안개 3단계, 적 유닛은 시야 안만, 오른쪽 클릭 이동, 경보 고리.</summary>
  public unsafe partial class LocalMatchTests {
    static Minimap Mini() => Object.FindAnyObjectByType<Minimap>();

    [UnityTest]
    public IEnumerator Minimap_TerritoryGrowsWithExpansionAndOrdersMove() {
      yield return StartMatch(stock: Stock(3000, 3000, 0, 0));
      yield return WaitForHud();
      yield return WaitFrames(3);
      var mini = Mini();
      mini.Refresh(Controller().Frame);
      var before = mini.TerritoryPixels[0];
      Assert.Greater(before, 0, "내 영역");
      Assert.Greater(mini.TerritoryPixels[2], 0, "적 영역(보이는 적 사령부)");
      Assert.Greater(mini.ResourceDiamonds, 0, "자원지 마름모");
      Assert.IsTrue(mini.CameraDrawn, "카메라 시야 사다리꼴");
      // 확장 사령부 착공 → 영역이 즉시 넓어진다.
      var position = V(-20, 10);
      yield return Build(0, "_headquarters", position);
      yield return WaitFor(() => BuildingAt(position) != EntityRef.None, 10f, "확장 부지");
      mini.Refresh(Controller().Frame);
      Assert.Greater(mini.TerritoryPixels[0], before, "확장하면 영역 증가");
      // 오른쪽 클릭 명령: 선택한 공병이 그 자리로 이동한다.
      var engineer = Engineers(0)[1 % Engineers(0).Count];
      Controller().Input.Selection.Clear();
      Controller().Input.Selection.SelectUnit(engineer);
      mini.OnOrder(new Vector2(-30f, -20f), false);
      yield return WaitFrames(CommandFrames + 2);
      Assert.AreEqual(UnitOrder.Move, Verified.Get<Unit>(engineer).Order, "미니맵 오른쪽 클릭 이동");
      // 경보 고리.
      var pings = mini.PingCount;
      mini.Ping(Vector3.zero, Minimap.RelationColors[2]);
      Assert.AreEqual(pings + 1, mini.PingCount, "경보 고리");
    }

    [UnityTest]
    public IEnumerator Minimap_FogHidesEnemiesAndKeepsGhosts() {
      var rules = Resources.Load<GameRulesSpec>("FactoryWars/game_rules");
      var fogBefore = rules.FogOfWar;
      Restores.Add(() => rules.FogOfWar = fogBefore);
      rules.FogOfWar = true;
      yield return StartMatch(Map4PScene, 2, truce: -1);
      yield return WaitForHud();
      yield return WaitFrames(3);
      var mini = Mini();
      mini.Refresh(Controller().Frame);
      Assert.IsTrue(FogSystem.Enabled(Controller().Frame), "안개 켜짐");
      // 시작 시점: 적 본진은 아직 본 적이 없다 → 적 유닛 점·적 영역 없음, 미탐색 칸은 검정.
      Assert.AreEqual(0, mini.EnemyUnitDots, "보이지 않는 적 유닛은 숨김");
      Assert.AreEqual(0, mini.TerritoryPixels[2], "모르는 적 영역은 숨김");
      var far = Verified.FindAsset<MapSpec>(Verified.Map.UserAsset).StartPositions[1].ToUnityVector3();
      var c = mini.PixelAt(far);
      Assert.Less(c.r + c.g + c.b, 110, "미탐색 = 22% 밝기로 어둡게");
      Assert.Greater(c.r + c.g + c.b, 20, "미탐색에서도 지도 윤곽이 보인다(완전 검정 아님)");
      Assert.AreEqual(1, mini.TraderState, "암시장 금색");
    }

    /// <summary>미니맵 텍스처를 3배(최근접) 키워 PNG로 저장한다.</summary>
    static void SaveMinimapBig(string name) {
      var texture = (Texture2D)Mini().GetComponent<UnityEngine.UI.RawImage>().texture;
      var source = texture.GetPixels32();
      var size = texture.width;
      const int scale = 3;
      var big = new Texture2D(size * scale, size * scale, TextureFormat.RGB24, false);
      var pixels = new Color32[size * size * scale * scale];
      for (var y = 0; y < size * scale; y++) {
        for (var x = 0; x < size * scale; x++) {
          pixels[y * size * scale + x] = source[(y / scale) * size + x / scale];
        }
      }
      big.SetPixels32(pixels);
      big.Apply();
      var path = Path.Combine(UrbanFolder(), name + ".png");
      File.WriteAllBytes(path, big.EncodeToPNG());
    }

    /// <summary>
    /// HUD 포함 캡처: 주 카메라(안개·후처리 포함)를 먼저 그리고, UI만 따로 비추는 카메라(후처리 없음, 지우지 않음)로 캔버스를 위에 덮는다.
    /// 실제 게임은 Overlay 캔버스라 안개·비네트가 HUD를 어둡게 하지 않는다. 캡처도 같게 만든다.
    /// </summary>
    static Texture2D Grab(Camera camera) {
      var surface = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
      camera.targetTexture = surface;
      camera.Render();
      RenderTexture.active = surface;
      var image = new Texture2D(1920, 1080, TextureFormat.RGBA32, false);
      image.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
      image.Apply();
      RenderTexture.active = null;
      camera.targetTexture = null;
      surface.Release();
      return image;
    }

    IEnumerator ShootHud(string name) {
      var canvas = Controller().Hud.GetComponentInChildren<Canvas>();
      var mode = canvas.renderMode;
      // 캔버스를 잠시 UI 층(5)으로 옮겨 UI 카메라가 캔버스만 그리게 한다.
      const int UiLayer = 5;
      var layers = new System.Collections.Generic.List<(Transform Item, int Layer)>();
      foreach (var item in canvas.GetComponentsInChildren<Transform>(true)) {
        layers.Add((item, item.gameObject.layer));
        item.gameObject.layer = UiLayer;
      }
      var uiObject = new GameObject("HudCaptureCamera");
      var ui = uiObject.AddComponent<Camera>();
      ui.clearFlags = CameraClearFlags.Nothing;
      ui.cullingMask = 1 << UiLayer;
      ui.depth = 100;
      ui.enabled = false;
      canvas.renderMode = RenderMode.ScreenSpaceCamera;
      canvas.worldCamera = ui;
      canvas.planeDistance = 1f;
      yield return null;
      Canvas.ForceUpdateCanvases();
      var main = Camera.main;
      var mask = main.cullingMask;
      main.cullingMask = mask & ~(1 << UiLayer);
      // 1) 장면(안개·후처리 포함), 2) UI만 투명 바탕에, 3) CPU로 합성(UI 알파는 SrcAlpha 섞기라 rgb는 미리 곱해져 있고 a = 덮임²).
      var scene = Grab(main);
      ui.clearFlags = CameraClearFlags.SolidColor;
      ui.backgroundColor = new Color(0f, 0f, 0f, 0f);
      var overlay = Grab(ui);
      main.cullingMask = mask;
      var a = scene.GetPixels32();
      var b = overlay.GetPixels32();
      for (var i = 0; i < a.Length; i++) {
        var cover = Mathf.Sqrt(b[i].a / 255f);
        a[i] = new Color32((byte)Mathf.Min(255, b[i].r + a[i].r * (1f - cover)), (byte)Mathf.Min(255, b[i].g + a[i].g * (1f - cover)), (byte)Mathf.Min(255, b[i].b + a[i].b * (1f - cover)), 255);
      }
      var image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
      image.SetPixels32(a);
      image.Apply();
      File.WriteAllBytes(Path.Combine(UrbanFolder(), name + ".png"), image.EncodeToPNG());
      canvas.renderMode = mode;
      foreach (var (item, layer) in layers) {
        item.gameObject.layer = layer;
      }
      Object.Destroy(uiObject);
    }

    [UnityTest, Explicit, Category("CaptureUrban")]
    public IEnumerator CaptureMinimap_TerritoryAndExpansion() {
      var rules = Resources.Load<GameRulesSpec>("FactoryWars/game_rules");
      var fogBefore = rules.FogOfWar;
      Restores.Add(() => rules.FogOfWar = fogBefore);
      rules.FogOfWar = true;
      // 정찰: 내 소총 분대·전차가 앞쪽에서 적 전진 기지(병영·방어탑)와 적 유닛을 보고 있다 → 파랑·빨강 영역이 함께 보인다.
      yield return StartMatch(Map4PScene, 2, stock: Stock(5000, 5000, 2000, 200),
        units: new[] { (0, "ff_ranger_squad", -100, -84), (0, "ff_eagle_tank", -96, -86), (1, "cf_militia", -92, -76), (1, "cf_militia", -89, -78) },
        buildings: new[] { (1, "cf_barracks", -86, -70), (1, "cf_ground_tower", -95, -73) });
      yield return WaitForHud();
      yield return WaitForBattle();
      yield return Send(0, new ChooseDoctrineCommand { Doctrine = 1 });
      Time.timeScale = 1f;
      var map = Verified.FindAsset<MapSpec>(Verified.Map.UserAsset);
      var home = At(Headquarters(0)).ToUnityVector3();
      // 중반 상황: 공병 셋과 전차가 사방으로 정찰해 탐색 구역(50% 어둡게)을 넓힌다.
      var scouts = Engineers(0).Take(3).ToList();
      scouts.AddRange(UnitsNamed(0, "ff_eagle_tank"));
      var targets = new[] { V((int)home.x + 150, (int)home.z + 20), V((int)home.x + 20, (int)home.z + 150), V((int)home.x + 120, (int)home.z + 120), V(0, 0), V((int)home.x + 90, (int)home.z + 40) };
      for (var i = 0; i < scouts.Count; i++) {
        yield return Send(0, new MoveUnitsCommand { Units = new[] { scouts[i] }, Target = targets[i % targets.Length] });
      }
      for (var wait = 0; wait < 7; wait++) {
        yield return WaitFrames(Fps * 10);
      }
      var seat = 1 << FactoryWarsUtil.SeatOf(Verified, 0);
      var camera = Controller().Camera;
      camera.MoveTo(new Vector2(-96f, -82f));
      camera.Height = 32f;
      camera.Apply();
      yield return WaitFrames(Fps);
      Mini().Refresh(Controller().Frame);
      yield return ShootHud("minimap_hud_start");
      SaveMinimapBig("minimap_start");
      var start = Mini().TerritoryPixels[0];
      // 확장: 내 본진에서 가장 가까운 앞마당 자리에 사령부. 캡처 시간을 줄이려고 건설 시간만 잠시 4초로.
      var hqSpec = SpecOf(0, "_headquarters");
      var buildBefore = hqSpec.BuildTimeSeconds;
      Restores.Add(() => hqSpec.BuildTimeSeconds = buildBefore);
      hqSpec.BuildTimeSeconds = 4;
      var site = map.Expansions.Where(e => (e.SeatMask & seat) != 0 && (e.Kind == ExpansionKind.IronForecourt || e.Kind == ExpansionKind.WoodForecourt)).OrderBy(e => (e.Position.ToUnityVector3() - home).sqrMagnitude).First().Position;
      var builder = Engineers(0).Last();
      yield return Build(0, "_headquarters", site, builder);
      yield return WaitFrames(CommandFrames + 2);
      Assert.AreNotEqual(EntityRef.None, BuildingAt(site), "확장 부지: " + string.Join(",", _rejected.Select(r => r.Reason)));
      var forward = BuildingAt(site);
      yield return WaitFor(() => Finished(forward), 90f, "전진 사령부 완공");
      Assert.Greater(Battle().ForwardHeadquarters, 0, "전진 사령부 표시");
      // 공병 셋이 옆 자원지에서 채집(전진 사령부로 반입).
      var node = EntityRef.None;
      var best = float.MaxValue;
      foreach (var pair in Verified.GetComponentIterator<ResourceNode>()) {
        var d = (pair.Component.Shape.Center.ToUnityVector3() - site.ToUnityVector3()).sqrMagnitude;
        if (d < best) {
          best = d;
          node = pair.Entity;
        }
      }
      yield return Send(0, new GatherCommand { Units = Engineers(0).ToArray(), Node = node });
      for (var wait = 0; wait < 3; wait++) {
        yield return WaitFrames(Fps * 10);
      }
      camera.MoveTo(new Vector2(site.X.AsFloat + 2f, site.Z.AsFloat - 4f));
      camera.Height = 24f;
      camera.Apply();
      yield return WaitFrames(Fps / 2);
      ShootUrban("forward_base");
      // 미니맵: 확장 지점 경보 고리 + 전방 HUD.
      Mini().Ping(site.ToUnityVector3(), Minimap.PingColor);
      Mini().Refresh(Controller().Frame);
      yield return ShootHud("forward_base_hud");
      camera.MoveTo(new Vector2(-96f, -82f));
      camera.Height = 32f;
      camera.Apply();
      yield return WaitFrames(Fps / 2);
      Mini().Refresh(Controller().Frame);
      yield return ShootHud("minimap_hud_expansion");
      SaveMinimapBig("minimap_expansion");
      Assert.Greater(Mini().TerritoryPixels[0], start, "확장으로 영역 증가");
    }
  }
}
