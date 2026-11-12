namespace FactoryWars.Tests {
  using System;
  using System.Collections;
  using System.Collections.Generic;
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
  /// 항목 31 HUD 시험: 해상도 3종(16:9·19.5:9·21:9) x 상황(아무것도·공병·건물·여러 종류)에서
  /// 구역 겹침 0, 버튼 글자 넘침 0, 버튼 9mm 이상(기기 dpi), 빈 패널 표시 0. 잠긴 칸은 이유를 보인다.
  /// </summary>
  public unsafe partial class LocalMatchTests {
    static Rect ScreenRect(RectTransform rt) {
      var corners = new Vector3[4];
      rt.GetWorldCorners(corners);
      return new Rect(corners[0].x, corners[0].y, corners[2].x - corners[0].x, corners[2].y - corners[0].y);
    }

    static bool Overlaps(Rect a, Rect b) => a.xMin < b.xMax - 0.5f && a.xMax > b.xMin + 0.5f && a.yMin < b.yMax - 0.5f && a.yMax > b.yMin + 0.5f;

    /// <summary>HUD를 해상도별 렌더 텍스처(화면 공간 카메라)에 맞춰 놓고 상황마다 check를 부른다.</summary>
    IEnumerator EachResolutionAndSituation(Action<string> check) {
      var hud = Controller().Hud;
      var canvas = hud.Root.GetComponentInParent<Canvas>();
      var camera = Camera.main;
      var selection = Controller().Input.Selection;
      var situations = new (string Name, Action Select)[] {
        ("none", () => selection.Clear()),
        ("engineer", () => selection.SelectUnit(Engineers(0)[0])),
        ("engineer_build", () => { selection.SelectUnit(Engineers(0)[0]); hud.Refresh(Controller().Frame); hud.PressContext(0); }),
        ("building", () => selection.SelectBuilding(Headquarters(0))),
        ("multi", () => { selection.Clear(); selection.Units.AddRange(OwnUnits(0, 8)); }),
      };
      foreach (var (name, width, height) in UiResolutions) {
        var surface = new RenderTexture(width, height, 24);
        camera.targetTexture = surface;
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        canvas.planeDistance = 2f;
        foreach (var (situation, select) in situations) {
          select();
          hud.Refresh(Controller().Frame);
          yield return null;
          hud.Refresh(Controller().Frame);
          Canvas.ForceUpdateCanvases();
          check($"{name}/{situation}");
        }
        camera.targetTexture = null;
        surface.Release();
      }
      canvas.renderMode = RenderMode.ScreenSpaceOverlay;
      selection.Clear();
    }

    [UnityTest, Timeout(240000)]
    public IEnumerator Ui_ZonesDoNotOverlapAtAllResolutions() {
      yield return StartMatch(truce: -1);
      yield return WaitForHud();
      var root = Controller().Hud.Root;
      string[] zones = { "TopBar", "MenuButton", "DiplomacyButton", "TruceBadge", "MinimapFrame", "BoxButton", "Actions", "SelectionCard", "ContextPanel", "Tooltip" };
      var failures = new List<string>();
      yield return EachResolutionAndSituation(label => {
        var active = zones.Select(z => root.Find(z) as RectTransform).Where(r => r != null && r.gameObject.activeInHierarchy).ToList();
        for (var i = 0; i < active.Count; i++) {
          for (var j = i + 1; j < active.Count; j++) {
            if (Overlaps(ScreenRect(active[i]), ScreenRect(active[j]))) {
              failures.Add($"{label}: {active[i].name} x {active[j].name}");
            }
          }
          if (ScreenRect(active[i]).xMin < ScreenRect(root).xMin - 1 || ScreenRect(active[i]).xMax > ScreenRect(root).xMax + 1) {
            failures.Add($"{label}: {active[i].name} 화면 밖");
          }
        }
        var context = root.Find("ContextPanel") as RectTransform;
        if (context.gameObject.activeSelf) {
          var area = ScreenRect(context).width * ScreenRect(context).height;
          if (area > ScreenRect(root).width * ScreenRect(root).height / 3f) {
            failures.Add($"{label}: 명령 카드가 화면 1/3 초과");
          }
        }
      });
      Assert.IsEmpty(failures, string.Join("\n", failures));
    }

    [UnityTest, Timeout(240000)]
    public IEnumerator Ui_TextFitsAllButtons() {
      yield return StartMatch(startingTier: 1);
      yield return WaitForHud();
      var failures = new List<string>();
      var generator = new TextGenerator();
      yield return EachResolutionAndSituation(label => {
        foreach (var button in Controller().Hud.Root.GetComponentsInChildren<Button>(false)) {
          var text = button.GetComponentInChildren<Text>();
          if (text == null || text.text.Length == 0) {
            continue;
          }
          var size = text.rectTransform.rect.size;
          var settings = text.GetGenerationSettings(size);
          settings.resizeTextForBestFit = false;
          settings.fontSize = text.resizeTextForBestFit && text.cachedTextGenerator.fontSizeUsedForBestFit > 0 ? text.cachedTextGenerator.fontSizeUsedForBestFit : text.fontSize;
          settings.verticalOverflow = VerticalWrapMode.Overflow;
          var height = generator.GetPreferredHeight(text.text, settings) / text.pixelsPerUnit;
          if (height > size.y + 1f || settings.fontSize < 14) {
            failures.Add($"{label}: {button.name} \"{text.text.Replace("\n", " ")}\" 높이 {height:0}/{size.y:0} 글자 {settings.fontSize}");
          }
        }
      });
      Assert.IsEmpty(failures, string.Join("\n", failures.Distinct()));
    }

    [UnityTest, Timeout(240000)]
    public IEnumerator Ui_TouchTargetsAtLeast9mm() {
      yield return StartMatch();
      yield return WaitForHud();
      var failures = new List<string>();
      var minimum = UiTheme.Current.MinTouchMillimeters;
      yield return EachResolutionAndSituation(label => {
        foreach (var button in Controller().Hud.Root.GetComponentsInChildren<Button>(false)) {
          var size = ((RectTransform)button.transform).rect.size;
          var canvasUnits = Mathf.Min(size.x, size.y);
          foreach (var device in Devices) {
            // 캔버스는 높이 1080 기준으로 맞추므로 기기 픽셀 = 캔버스 단위 x (기기 높이 / 1080).
            var dpi = Mathf.Sqrt(device.Width * device.Width + device.Height * device.Height) / device.Inches;
            var mm = canvasUnits * (Mathf.Min(device.Width, device.Height) / 1080f) / dpi * 25.4f;
            if (mm < minimum) {
              failures.Add($"{label}: {button.name} {mm:0.0}mm ({device.Name})");
            }
          }
        }
      });
      Assert.IsEmpty(failures, string.Join("\n", failures.Distinct()));
    }

    [UnityTest, Timeout(240000)]
    public IEnumerator Ui_NoEmptyVisiblePanels() {
      yield return StartMatch(truce: 0);
      yield return WaitForHud();
      var failures = new List<string>();
      yield return EachResolutionAndSituation(label => {
        foreach (var image in Controller().Hud.Root.GetComponentsInChildren<Image>(false)) {
          if (image.GetComponent<Button>() != null || image.GetComponentInParent<Button>() != null) {
            continue;
          }
          var texts = image.GetComponentsInChildren<Text>(false);
          if (texts.Length == 0) {
            continue;
          }
          var hasContent = texts.Any(t => t.text.Length > 0) || image.GetComponentsInChildren<Button>(false).Length > 0 || image.GetComponentsInChildren<RawImage>(false).Length > 0;
          if (hasContent == false) {
            failures.Add($"{label}: {image.name}");
          }
        }
      });
      Assert.IsEmpty(failures, "빈 패널: " + string.Join(", ", failures.Distinct()));
      // 휴전이 없으면 상단 가운데 막대가 없다(B5).
      Assert.IsFalse(Controller().Hud.Root.Find("TruceBadge").gameObject.activeSelf, "빈 휴전 막대 숨김");
      var perf = Object.FindAnyObjectByType<PerfMonitor>();
      Assert.IsTrue(perf == null || perf.PanelVisible == false, "좌상단 빈 성능 상자 숨김");
    }

    [UnityTest]
    public IEnumerator Ui_LockedEntryShowsReasonAndHidesFarTiers() {
      yield return StartMatch(startingTier: 1, buildings: new[] { (0, "ff_barracks", -42, 12) });
      yield return WaitForHud();
      var hud = Controller().Hud;
      Controller().Input.Selection.SelectUnit(Engineers(0)[0]);
      hud.Refresh(Controller().Frame);
      var labels = hud.Entries.Select(e => e.Label).ToList();
      Assert.Contains("기본 건물", labels);
      Assert.Contains("2단계 건물", labels);
      Assert.IsFalse(labels.Contains("3단계 건물"), "1단계에서 3단계는 숨김(지금 데이터에는 3단계 건물도 없다)");
      hud.PressContext(labels.IndexOf("2단계 건물"));
      Assert.AreEqual("Build2", hud.MenuName);
      var factory = hud.Entries.First(e => e.Label.StartsWith("군수공장"));
      Assert.IsTrue(factory.Locked);
      Assert.IsFalse(factory.Enabled);
      StringAssert.Contains("기지 2단계 필요", factory.Reason);
      hud.LongPressContext(hud.Entries.IndexOf(factory));
      StringAssert.Contains("기지 2단계 필요", hud.TooltipText);
      hud.PressContext(hud.Entries.FindIndex(e => e.Label == "뒤로"));
      Assert.AreEqual("Root", hud.MenuName);

      // 병영: 소형은 열려 있고, 사령부: 업그레이드는 연구소가 없어 잠김.
      Controller().Input.Selection.SelectBuilding(Headquarters(0));
      hud.Refresh(Controller().Frame);
      var upgrade = hud.Entries.First(e => e.Label.StartsWith("연대 본부"));
      Assert.IsTrue(upgrade.Locked);
      StringAssert.Contains("연구소 필요", upgrade.Reason);
      Assert.AreEqual("1단계", hud.SelectionBadge);
      Assert.Greater(hud.IdleEngineers, 0, "유휴 공병 배지");
    }

    [UnityTest]
    public IEnumerator Ui_SelectionCardShowsSizeHealthAndTypes() {
      yield return StartMatch();
      yield return WaitForHud();
      var hud = Controller().Hud;
      var tank = UnitsNamed(0, "ff_eagle_tank")[0];
      Controller().Input.Selection.SelectUnit(tank);
      hud.Refresh(Controller().Frame);
      StringAssert.Contains("이글 전차", hud.SelectionTitle);
      Assert.AreEqual("중", hud.SelectionBadge, "크기 배지");
      StringAssert.Contains("체력", hud.SelectionDetail);
      StringAssert.Contains("공격 +0", hud.SelectionDetail);

      var selection = Controller().Input.Selection;
      selection.Clear();
      selection.Units.AddRange(UnitsNamed(0, "ff_ranger_squad"));
      selection.Units.Add(tank);
      hud.Refresh(Controller().Frame);
      Assert.AreEqual(2, hud.SelectionTypeButtons, "종류별 버튼");
      hud.SelectType(1);
      hud.Refresh(Controller().Frame);
      Assert.IsTrue(selection.Units.All(e => Verified.Get<Unit>(e).Spec == Verified.Get<Unit>(selection.Units[0]).Spec), "그 종류만 남음");
    }

    [Test]
    public void Ui_CatalogHasAllSlotsAndTheme() {
      var catalog = Resources.Load<UiCatalog>(UiCatalog.ResourcePath);
      Assert.IsNotNull(catalog, "Resources/FactoryWars/UI/UiCatalog");
      foreach (var (id, _, _) in UiCatalog.Defaults) {
        Assert.IsNotNull(catalog.Get(id), $"칸 {id}");
        Assert.IsNotNull(UiCatalog.SpriteFor(id), $"칸 {id}의 임시 그림");
      }
      Assert.IsNotNull(Resources.Load<UiTheme>(UiTheme.ResourcePath), "Resources/FactoryWars/UI/UiTheme");
      Assert.GreaterOrEqual(UiTheme.Current.MinButton, 154f);
    }
  }
}
