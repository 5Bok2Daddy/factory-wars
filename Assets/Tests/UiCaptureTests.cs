namespace FactoryWars.Tests {
  using System;
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
  /// 항목 31 UI 전/후 캡처. GPU가 있는 배치 모드에서 -testCategory UiCapture로 돌린다.
  /// 파일 이름 앞부분은 환경 변수 FW_UI_PREFIX(기본 31_after). screenshots/ui/{앞부분}_{해상도}_{상황}.png.
  /// HUD 메서드는 이름으로 찾아 부른다(바꾸기 전 HUD에서도 같은 시험이 돈다).
  /// </summary>
  public unsafe partial class LocalMatchTests {
    public static readonly (string Name, int Width, int Height)[] UiResolutions = {
      ("16x9", 1920, 1080), ("19.5x9", 2340, 1080), ("21x9", 2520, 1080),
    };

    static string UiFolder() => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "screenshots", "ui"));

    static System.Collections.Generic.List<EntityRef> OwnUnits(int owner, int limit) {
      var list = new System.Collections.Generic.List<EntityRef>();
      foreach (var pair in Verified.GetComponentIterator<Unit>()) {
        if (pair.Component.Owner == owner && list.Count < limit) {
          list.Add(pair.Entity);
        }
      }
      return list;
    }

    static void CallHud(string method, params object[] args) {
      var hud = Controller().Hud;
      var info = hud.GetType().GetMethod(method);
      info?.Invoke(hud, args);
    }

    IEnumerator ShootUi(string prefix, string situation) {
      var canvas = Controller().Hud.Root.GetComponentInParent<Canvas>();
      var camera = Camera.main;
      foreach (var (name, width, height) in UiResolutions) {
        var surface = new RenderTexture(width, height, 24);
        camera.targetTexture = surface;
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        canvas.planeDistance = 2f;
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
        File.WriteAllBytes(Path.Combine(UiFolder(), $"{prefix}_{name}_{situation}.png"), image.EncodeToPNG());
        UnityEngine.Object.Destroy(image);
        camera.targetTexture = null;
        surface.Release();
      }
      canvas.renderMode = RenderMode.ScreenSpaceOverlay;
    }

    [UnityTest, Explicit, Category("UiCapture"), Timeout(900000)]
    public IEnumerator Ui_CaptureScreens() {
      var prefix = Environment.GetEnvironmentVariable("FW_UI_PREFIX");
      prefix = string.IsNullOrEmpty(prefix) ? "31_after" : prefix;
      Directory.CreateDirectory(UiFolder());
      // 시험 맵: 시작 유닛(레인저 2·이글 전차)이 있어 여러 종류 선택을 찍을 수 있다. 1단계로 시작해 잠금이 보인다.
      yield return StartMatch(startingTier: 1, buildings: new[] { (0, "ff_barracks", -42, 12) }, truce: -1);
      yield return WaitForHud();
      yield return WaitForBattle();
      GraphicsQuality.Apply(QualityTier.Medium);
      yield return WaitFrames(5 * Fps);
      var selection = Controller().Input.Selection;
      var hq = Headquarters(0);
      var home = Verified.Get<Transform3D>(hq).Position.ToUnityVector3();
      Controller().Camera.MoveTo(new Vector2(home.x, home.z));

      // 1. 공병 선택
      selection.SelectUnit(Engineers(0)[0]);
      CallHud("PressContext", 0);
      yield return ShootUi(prefix, "engineer");

      // 2. 건물(병영) 선택
      selection.SelectBuilding(OwnBuilding(0, "ff_barracks"));
      yield return ShootUi(prefix, "building");

      // 3. 여러 종류 선택: 공병 + 시작 전투 유닛
      selection.Clear();
      var mine = OwnUnits(0, 8);
      selection.Units.AddRange(mine);
      yield return ShootUi(prefix, "multi");

      // 4. 잠금 이유: 공병 → 2단계 건물 → 잠긴 칸 길게 누르기(바뀐 HUD만).
      selection.SelectUnit(Engineers(0)[0]);
      Controller().Hud.Refresh(Controller().Frame);
      CallHud("PressContext", 1);
      Controller().Hud.Refresh(Controller().Frame);
      CallHud("LongPressContext", 0);
      yield return ShootUi(prefix, "locked");
      Assert.IsTrue(File.Exists(Path.Combine(UiFolder(), $"{prefix}_16x9_locked.png")));
    }
  }
}
