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
      Assert.Less(c.r + c.g + c.b, 60, "미탐색 = 검정");
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
      camera.MoveTo(new Vector2(home.x + 18f, home.z + 18f));
      camera.Height = 40f;
      camera.Apply();
      var canvas = Controller().Hud.GetComponentInChildren<Canvas>();
      canvas.renderMode = RenderMode.ScreenSpaceCamera;
      canvas.worldCamera = Camera.main;
      canvas.planeDistance = 1f;
      yield return WaitFrames(Fps);
      Mini().Refresh(Controller().Frame);
      yield return null;
      ShootUrban("minimap_hud_start");
      SaveMinimapBig("minimap_start");
      var start = Mini().TerritoryPixels[0];
      // 확장: 내 본진에서 가장 가까운 확장 자리(자리 표시가 나를 포함)에 사령부 착공.
      var site = map.Expansions.Where(e => (e.SeatMask & seat) != 0 && (e.Kind == ExpansionKind.IronForecourt || e.Kind == ExpansionKind.WoodForecourt)).OrderBy(e => (e.Position.ToUnityVector3() - home).sqrMagnitude).First().Position;
      yield return Build(0, "_headquarters", site);
      yield return WaitFrames(CommandFrames + 2);
      Assert.AreNotEqual(EntityRef.None, BuildingAt(site), "확장 부지: " + string.Join(",", _rejected.Select(r => r.Reason)));
      yield return WaitFrames(Fps);
      Mini().Ping(site.ToUnityVector3(), Minimap.PingColor);
      Mini().Refresh(Controller().Frame);
      yield return null;
      // HUD 캡처는 시야가 있는 전방(내 정찰 분대와 적 전진 기지)을 비춘다.
      camera.MoveTo(new Vector2(-96f, -82f));
      camera.Height = 32f;
      camera.Apply();
      yield return WaitFrames(Fps / 2);
      Mini().Refresh(Controller().Frame);
      yield return null;
      ShootUrban("minimap_hud_expansion");
      SaveMinimapBig("minimap_expansion");
      Assert.Greater(Mini().TerritoryPixels[0], start, "확장으로 영역 증가");
    }
  }
}
