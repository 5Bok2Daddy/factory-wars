namespace FactoryWars.Tests {
  using System;
  using System.Collections;
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using FactoryWars.View;
  using NUnit.Framework;
  using Quantum;
  using UnityEngine;
  using UnityEngine.TestTools;
  using Object = UnityEngine.Object;

  /// <summary>
  /// 그래픽 룩 개발의 축별 비교(항목 19A). 같은 고정 장면을 축마다 3~5안으로 다시 그려 시트로 붙인다.
  /// 안마다 프로필을 복제해 값 하나만 바꾸고, 화면을 그 프로필로 다시 만든다. 시뮬레이션은 멈춰 있다.
  /// </summary>
  public unsafe partial class LocalMatchTests {
    public const int LookScene = 480;
    const float TankTargetPixels = 80f;

    static VisualProfile Variant(VisualProfile basis, Action<VisualProfile> change) {
      var copy = Object.Instantiate(basis);
      change(copy);
      return copy;
    }

    /// <summary>전차 한 대가 화면에서 target 픽셀이 되는 기본 높이를 찾는다. 높이에 반비례하므로 한 번 재고 비율로 맞춘다.</summary>
    float HeightForTank(Camera camera, float target) {
      var tank = Battle().Actors.Values.FirstOrDefault(actor => actor.View != null && actor.View.Entry.Id.Contains("tank") && actor.View.gameObject.activeInHierarchy);
      if (tank == null) {
        return VisualProfile.Current.DefaultHeight;
      }
      const float Probe = 40f;
      Aim(camera, tank.View.transform.position, Probe);
      var pixels = OneTankPixels(camera, tank, 1920, 1080);
      return pixels > 1f ? Probe * pixels / target : VisualProfile.Current.DefaultHeight;
    }

    static float OneTankPixels(Camera camera, BattleView.Actor actor, int width, int height) {
      var surface = RenderTexture.GetTemporary(width, height, 24);
      var previous = camera.targetTexture;
      camera.targetTexture = surface;
      var bounds = new Bounds(actor.View.transform.position, Vector3.zero);
      foreach (var renderer in actor.View.Model.GetComponentsInChildren<MeshRenderer>()) {
        bounds.Encapsulate(renderer.bounds);
      }
      var min = float.MaxValue;
      var max = float.MinValue;
      for (var i = 0; i < 8; i++) {
        var corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
        var screen = camera.WorldToScreenPoint(corner);
        min = Mathf.Min(min, screen.x);
        max = Mathf.Max(max, screen.x);
      }
      camera.targetTexture = previous;
      RenderTexture.ReleaseTemporary(surface);
      return max - min;
    }

    /// <summary>프로필을 바꿔 끼우고 화면을 다시 만든 뒤 시점마다 찍는다.</summary>
    IEnumerator ShootVariant(VisualProfile profile, string[] views, List<(Texture2D, string)> cells, string label) {
      VisualProfile.Current = profile;
      Battle().Rebuild();
      yield return null;
      yield return null;
      GraphicsQuality.Apply(GraphicsQuality.Current);
      yield return null;
      var map = Verified.FindAsset<MapSpec>(Verified.Map.UserAsset);
      var camera = Camera.main;
      var points = ViewPoints(map);
      foreach (var name in views) {
        var view = points.First(p => p.Name == name);
        Aim(camera, view.Focus, name == "D" ? view.Height : profile.DefaultHeight * (name == "B" ? 0.8f : 1f));
        var seen = Visible(camera, 1920, 1080);
        cells.Add((Shoot(camera, CellWidth * 2, CellHeight * 2), $"{label} [{name}] draw~{EstimateDrawCalls(seen)} tris {seen.Triangles / 1000}k"));
      }
    }

    IEnumerator Axis(string axis, VisualProfile basis, (string Name, Action<VisualProfile> Change)[] variants, string[] views) {
      var cells = new List<(Texture2D, string)>();
      foreach (var (name, change) in variants) {
        yield return ShootVariant(Variant(basis, change), views, cells, name);
      }
      SaveSheet($"{axis}", $"축 {axis}: {string.Join(" / ", variants.Select(v => v.Name))}", cells, views.Length);
      foreach (var (texture, _) in cells) {
        Object.Destroy(texture);
      }
      VisualProfile.Current = basis;
    }

    [UnityTest, Explicit, Category("Lookdev"), Timeout(6000000)]
    public IEnumerator Lookdev_Axes() {
      yield return FixedScene(LookScene, human: true);
      GraphicsQuality.Apply(QualityTier.Medium);
      var basis = Object.Instantiate(VisualProfile.Current);
      var camera = Camera.main;
      var views = new[] { "A", "C" };

      // 1. 카메라: 기본 높이는 전차 80px(1080p)이 되도록 안마다 다시 잰다.
      var cameras = new (string, Action<VisualProfile>)[] { ("FOV60 피치50", p => { }), ("FOV45 피치50", p => p.FieldOfView = 45), ("FOV35 피치50", p => p.FieldOfView = 35), ("FOV30 피치50", p => p.FieldOfView = 30), ("FOV30 피치55", p => { p.FieldOfView = 30; p.Pitch = 55; }) };
      var tuned = new List<(string, Action<VisualProfile>)>();
      foreach (var (name, change) in cameras) {
        var probe = Variant(basis, change);
        VisualProfile.Current = probe;
        var height = HeightForTank(camera, TankTargetPixels);
        LookdevNote("axes.csv", $"camera,{name},default_height,{L(height)}");
        tuned.Add(($"{name} 높이{L(height)}", p => { change(p); p.DefaultHeight = height; p.MinHeight = height * 0.6f; p.MaxHeight = height * 2.6f; }));
      }
      VisualProfile.Current = basis;
      yield return Axis("01_camera", basis, tuned.ToArray(), views);

      yield return Axis("02_scale", basis, new (string, Action<VisualProfile>)[] {
        ("유닛1.0 건물85%", p => { p.UnitScale = 1f; p.BuildingBody = 0.85f; }), ("유닛1.2 건물80%", p => { p.UnitScale = 1.2f; p.BuildingBody = 0.8f; }), ("유닛1.4 건물75%", p => { p.UnitScale = 1.4f; p.BuildingBody = 0.75f; }),
      }, views);

      yield return Axis("03_light", basis, new (string, Action<VisualProfile>)[] {
        ("따뜻한 오후 40°", p => { p.SunColor = new Color32(0xFF, 0xF4, 0xE0, 0xFF); p.SunIntensity = 1.2f; p.SunElevation = 40; p.SunAzimuth = -35; p.AmbientSky = new Color(0.52f, 0.6f, 0.72f); p.AmbientGround = new Color(0.25f, 0.2f, 0.16f); }),
        ("맑은 정오 60°", p => { p.SunColor = Color.white; p.SunIntensity = 1.1f; p.SunElevation = 60; p.SunAzimuth = -20; }),
        ("늦은 오후 25°", p => { p.SunColor = new Color32(0xFF, 0xD2, 0xA0, 0xFF); p.SunIntensity = 1.25f; p.SunElevation = 25; p.SunAzimuth = -45; p.AmbientSky = new Color(0.45f, 0.52f, 0.68f); }),
      }, views);

      yield return Axis("04_post", basis, new (string, Action<VisualProfile>)[] {
        ("Neutral 0/0", p => { }), ("Neutral 채도+12 대비+10", p => { p.Saturation = 12; p.Contrast = 10; }), ("ACES 채도+20 대비+10", p => { p.Tone = ToneMode.Aces; p.Saturation = 20; p.Contrast = 10; }),
        ("Neutral +12/+10 SSAO 비네트", p => { p.Saturation = 12; p.Contrast = 10; p.Ssao = true; p.Vignette = 0.18f; }),
      }, views);

      yield return Axis("05_outline", basis, new (string, Action<VisualProfile>)[] {
        ("없음", p => p.Outline = OutlineMode.None), ("외곽선 0.04", p => { p.Outline = OutlineMode.Hull; p.OutlineWidth = 0.04f; }), ("외곽선 0.07", p => { p.Outline = OutlineMode.Hull; p.OutlineWidth = 0.07f; }),
      }, views);

      yield return Axis("06_terrain", basis, new (string, Action<VisualProfile>)[] {
        ("대지3 층2 강0.8", p => { p.PlateauHeight = 3; p.CliffLayers = 2; p.WaterDepth = 0.8f; p.HighGroundHeight = 1.5f; }),
        ("대지5 층3 강2.5", p => { p.PlateauHeight = 5; p.CliffLayers = 3; p.WaterDepth = 2.5f; p.HighGroundHeight = 2.5f; }),
        ("대지8 층4 강4", p => { p.PlateauHeight = 8; p.CliffLayers = 4; p.WaterDepth = 4f; p.HighGroundHeight = 3.5f; p.Jagged = 0.9f; }),
      }, new[] { "A", "B" });

      yield return Axis("07_ground", basis, new (string, Action<VisualProfile>)[] {
        ("기본", p => { p.GroundPalette = 0; }), ("밝은 연두 + 구역", p => { p.GroundPalette = 1; p.ZoneColors = true; }), ("올리브·흙 + 구역 큰 얼룩", p => { p.GroundPalette = 2; p.ZoneColors = true; p.PatchSize = 28; }),
      }, views);

      yield return Axis("08_forest", basis, new (string, Action<VisualProfile>)[] {
        ("기존", p => { p.LeafSet = 0; p.ConiferRatio = 0.45f; }), ("침엽 75% + 단풍", p => { p.LeafSet = 1; p.ConiferRatio = 0.75f; }), ("침엽 90% + 단풍", p => { p.LeafSet = 1; p.ConiferRatio = 0.9f; }),
      }, new[] { "A", "D" });

      yield return Axis("09_outside", basis, new (string, Action<VisualProfile>)[] {
        ("어두운 단색", p => p.Outside = OutsideMode.Dark), ("안개", p => p.Outside = OutsideMode.Fog),
      }, new[] { "D" });

      yield return Axis("10_nobuild", basis, new (string, Action<VisualProfile>)[] {
        ("늘 빗금", p => p.NoBuildAlways = true), ("건설 때만", p => p.NoBuildAlways = false),
      }, new[] { "B" });
    }
  }
}
