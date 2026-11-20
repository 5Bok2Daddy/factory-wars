namespace FactoryWars.Tests {
  using System.Collections;
  using System.Globalization;
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
  using Object = UnityEngine.Object;

  /// <summary>
  /// 항목 23 터치 점검. 실제 HUD를 기기 해상도로 그려 버튼의 물리 크기(mm)와 가장 작은 글자 높이를 잰다.
  /// 화면 크기(인치)로 dpi를 계산한다. 결과: screenshots/device/<기기>.png, Game-logs/device/touch.csv.
  /// </summary>
  public unsafe partial class LocalMatchTests {
    static readonly (string Name, int Width, int Height, float Inches)[] Devices = {
      ("phone_6.1in_2400x1080", 2400, 1080, 6.1f),
      ("phone_6.7in_3200x1440", 3200, 1440, 6.7f),
      ("tablet_11in_2560x1600", 2560, 1600, 11f),
    };

    const float MinButtonMillimeters = 9f;

    [UnityTest, Explicit, Category("Device"), Timeout(300000)]
    public IEnumerator Device_TouchTargets() {
      yield return StartMatch(Map4PScene, 2, bots: new[] { BotDifficulty.Normal }, truce: 0);
      yield return WaitForHud();
      yield return WaitForBattle();
      Controller().Input.Selection.SelectUnit(Engineers(0)[0]);
      Controller().Hud.Refresh(Controller().Frame);
      yield return null;
      var canvas = Controller().Hud.Root.GetComponentInParent<Canvas>();
      var camera = Camera.main;
      var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "screenshots", "device"));
      var logs = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Game-logs", "device"));
      Directory.CreateDirectory(folder);
      Directory.CreateDirectory(logs);
      var csv = "device,dpi,min_button_mm,min_button_name,min_font_mm,buttons_under_9mm\n";
      var worst = float.MaxValue;
      var hasGpu = SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;
      foreach (var device in Devices) {
        var dpi = Mathf.Sqrt(device.Width * device.Width + device.Height * device.Height) / device.Inches;
        RenderTexture surface = null;
        if (hasGpu) {
          surface = new RenderTexture(device.Width, device.Height, 24);
          camera.targetTexture = surface;
          canvas.renderMode = RenderMode.ScreenSpaceCamera;
          canvas.worldCamera = camera;
          canvas.planeDistance = 2f;
        }
        yield return null;
        Canvas.ForceUpdateCanvases();
        var corners = new Vector3[4];
        var minMm = float.MaxValue;
        var minName = "";
        var under = 0;
        foreach (var button in Controller().Hud.Root.GetComponentsInChildren<Button>(false)) {
          var rect = (RectTransform)button.transform;
          rect.GetWorldCorners(corners);
          var px = hasGpu
            ? Mathf.Min(Mathf.Abs(camera.WorldToScreenPoint(corners[2]).x - camera.WorldToScreenPoint(corners[0]).x), Mathf.Abs(camera.WorldToScreenPoint(corners[2]).y - camera.WorldToScreenPoint(corners[0]).y))
            : Mathf.Min(Mathf.Abs(corners[2].x - corners[0].x), Mathf.Abs(corners[2].y - corners[0].y));
          var mm = px / dpi * 25.4f;
          under += mm < MinButtonMillimeters ? 1 : 0;
          if (mm < minMm) {
            minMm = mm;
            minName = button.name;
          }
        }
        var minFont = Controller().Hud.Root.GetComponentsInChildren<Text>(false).Where(t => t.text.Length > 0).Min(t => t.fontSize) * canvas.scaleFactor;
        var fontMm = minFont / dpi * 25.4f;
        worst = Mathf.Min(worst, minMm);
        csv += string.Join(",", device.Name, dpi.ToString("0", CultureInfo.InvariantCulture), minMm.ToString("0.0", CultureInfo.InvariantCulture), minName, fontMm.ToString("0.0", CultureInfo.InvariantCulture), under) + "\n";
        if (hasGpu && surface != null) {
          camera.Render();
          RenderTexture.active = surface;
          var image = new Texture2D(device.Width, device.Height, TextureFormat.RGB24, false);
          image.ReadPixels(new Rect(0, 0, device.Width, device.Height), 0, 0);
          image.Apply();
          RenderTexture.active = null;
          File.WriteAllBytes(Path.Combine(folder, device.Name + ".png"), image.EncodeToPNG());
          Object.Destroy(image);
          camera.targetTexture = null;
          surface.Release();
        }
      }
      canvas.renderMode = RenderMode.ScreenSpaceOverlay;
      File.WriteAllText(Path.Combine(logs, "touch.csv"), csv);
      Debug.Log("[FW-DEVICE]\n" + csv);
      Assert.GreaterOrEqual(worst, MinButtonMillimeters, "가장 작은 HUD 버튼이 9mm 이상\n" + csv);
    }

    [UnityTest, Timeout(120000)]
    public IEnumerator Ui_PanelsDoNotOverlap() {
      yield return StartMatch(Map4PScene, 2, bots: new[] { BotDifficulty.Normal }, truce: 0);
      yield return WaitForHud();
      yield return WaitForBattle();

      var hud = Controller().Hud;
      if (hud.DoctrineOpen) {
        yield return Send(0, new Quantum.FactoryWars.ChooseDoctrineCommand { Doctrine = 1 });
        yield return WaitFor(() => hud.DoctrineOpen == false, 10f, "전술 도크린 선택 완료");
      }
      var root = hud.Root;

      // 1. 초기 상태 (유닛 미선택): ContextPanel 비활성화 확인
      Controller().Input.Selection.Clear();
      hud.Refresh(Controller().Frame);
      yield return null;

      var contextPanel = root.Find("ContextPanel") as RectTransform;
      Assert.IsNotNull(contextPanel, "ContextPanel 존재");
      Assert.IsFalse(contextPanel.gameObject.activeSelf, "유닛 미선택 시 건설 패널 비활성화");

      // 2. 공병(Worker) 선택: ContextPanel 활성화 확인
      var engineers = Engineers(0);
      Assert.Greater(engineers.Count, 0, "공병 존재");
      Controller().Input.Selection.SelectUnit(engineers[0]);
      hud.Refresh(Controller().Frame);
      yield return null;

      Assert.IsTrue(contextPanel.gameObject.activeSelf, "공병 선택 시 건설 패널 활성화");

      // 3. 패널 간 겹침 점검 (Minimap, Actions, ContextPanel)
      var minimap = root.Find("MinimapFrame") as RectTransform ?? root.Find("Minimap") as RectTransform;
      var actions = root.Find("Actions") as RectTransform;

      Assert.IsNotNull(minimap, "Minimap 존재");
      Assert.IsNotNull(actions, "Actions 존재");

      Rect GetScreenRect(RectTransform rt) {
        var corners = new Vector3[4];
        rt.GetWorldCorners(corners);
        return new Rect(corners[0].x, corners[0].y, corners[2].x - corners[0].x, corners[2].y - corners[0].y);
      }

      bool Overlaps(Rect a, Rect b) {
        return a.xMin < b.xMax && a.xMax > b.xMin && a.yMin < b.yMax && a.yMax > b.yMin;
      }

      var mapRect = GetScreenRect(minimap);
      var actionsRect = GetScreenRect(actions);
      var contextRect = GetScreenRect(contextPanel);

      Assert.IsFalse(Overlaps(mapRect, actionsRect), "Minimap과 Actions가 겹치지 않음");
      Assert.IsFalse(Overlaps(actionsRect, contextRect), "Actions와 ContextPanel이 겹치지 않음");
      Assert.IsFalse(Overlaps(mapRect, contextRect), "Minimap과 ContextPanel이 겹치지 않음");

      // 4. 건설 패널 화면 1/3 면적 이하 점검
      var rootRect = GetScreenRect(root);
      var screenArea = rootRect.width * rootRect.height;
      var panelArea = contextRect.width * contextRect.height;
      Assert.LessOrEqual(panelArea, screenArea / 3f, "건설 패널은 화면 1/3 면적을 넘지 않음");

      // 5. 전투 유닛을 고르면 같은 자리에서 전투 명령을 표시한다.
      var combatUnits = UnitsNamed(0, "ff_eagle_tank").Concat(UnitsNamed(0, "ff_trooper_squad")).ToArray();
      if (combatUnits.Length > 0) {
        Controller().Input.Selection.SelectUnit(combatUnits[0]);
        hud.Refresh(Controller().Frame);
        yield return null;
        Assert.IsTrue(contextPanel.gameObject.activeSelf, "전투 명령 패널 표시");
        Assert.IsFalse(hud.Entries.Any(entry => entry.Label == "병기 공장"), "공병 건설 메뉴는 숨김");
      }
    }
  }
}
