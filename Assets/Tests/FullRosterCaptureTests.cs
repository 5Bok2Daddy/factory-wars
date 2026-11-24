namespace FactoryWars.Tests {
  using System.Collections;
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using FactoryWars.View;
  using NUnit.Framework;
  using UnityEngine;
  using UnityEngine.TestTools;
  using Assert = NUnit.Framework.Assert;

  /// <summary>
  /// Captures clean studio inspection screenshots for every unit across FF, CF, and Neutral PMC Mercenaries
  /// (infantry, vehicles, tanks, helicopters/aircraft, naval units, turrets/silos) into Docs/status_images/units/.
  /// </summary>
  public unsafe partial class LocalMatchTests {
    static readonly string[] ExtraBuildingUnits = {
      "building/ff_patriot_turret", "building/cf_patriot_turret",
      "building/ff_missile_silo", "building/cf_missile_silo",
      "building/ff_ground_tower", "building/cf_ground_tower",
      "building/ff_air_defense", "building/cf_air_defense"
    };

    [UnityTest, Explicit, Timeout(600000)]
    public IEnumerator FullRoster_CaptureAllUnits() {
      var catalog = Resources.Load<VisualCatalog>(VisualCatalog.ResourcePath);
      Assert.IsNotNull(catalog, "VisualCatalog must load");

      var outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Docs", "status_images", "units"));
      Directory.CreateDirectory(outDir);

      // Build an isolated studio stage high above the world so nothing else enters the camera frustum.
      var stage = new GameObject("UnitCatalogStudio");
      var origin = new Vector3(0f, 600f, 0f);
      stage.transform.position = origin;

      var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
      floor.transform.SetParent(stage.transform, false);
      floor.transform.localScale = new Vector3(12f, 1f, 12f);
      var floorMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
      floorMat.SetColor("_BaseColor", new Color(0.72f, 0.75f, 0.79f, 1f));
      floorMat.SetFloat("_Smoothness", 0.12f);
      floor.GetComponent<Renderer>().sharedMaterial = floorMat;

      var keyGo = new GameObject("StudioKeyLight");
      keyGo.transform.SetParent(stage.transform, false);
      keyGo.transform.rotation = Quaternion.Euler(42f, 145f, 0f);
      var key = keyGo.AddComponent<Light>();
      key.type = LightType.Directional;
      key.intensity = 0.75f;
      key.color = new Color(1f, 0.98f, 0.95f);
      key.shadows = LightShadows.Soft;

      var fillGo = new GameObject("StudioFillLight");
      fillGo.transform.SetParent(stage.transform, false);
      fillGo.transform.rotation = Quaternion.Euler(25f, -35f, 0f);
      var fill = fillGo.AddComponent<Light>();
      fill.type = LightType.Directional;
      fill.intensity = 0.35f;
      fill.color = new Color(0.86f, 0.92f, 1f);
      fill.shadows = LightShadows.None;

      var camGo = new GameObject("StudioCamera");
      camGo.transform.SetParent(stage.transform, false);
      var cam = camGo.AddComponent<Camera>();
      cam.clearFlags = CameraClearFlags.SolidColor;
      cam.backgroundColor = new Color(0.76f, 0.80f, 0.85f, 1f);
      cam.fieldOfView = 32f;
      cam.nearClipPlane = 0.05f;
      cam.farClipPlane = 300f;
      cam.enabled = false;

      var targets = new List<(string FileName, VisualSlot Slot)>();
      foreach (var slot in catalog.Slots) {
        if (slot == null || string.IsNullOrEmpty(slot.Id) || slot.Prefab == null) continue;
        if (slot.Id.StartsWith("unit/")) {
          var shortName = slot.Id.Substring("unit/".Length);
          targets.Add(($"unit_{shortName}.png", slot));
        } else if (ExtraBuildingUnits.Contains(slot.Id)) {
          var shortName = slot.Id.Substring("building/".Length);
          targets.Add(($"unit_{shortName}.png", slot));
        }
      }

      var manifest = new List<string>();
      foreach (var (fileName, slot) in targets.OrderBy(t => t.FileName)) {
        var go = Object.Instantiate(slot.Prefab, stage.transform);
        var scale = slot.Scale > 0f ? slot.Scale : 1f;
        go.transform.localScale = Vector3.one * scale;
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.Euler(0f, slot.YawOffset, 0f);

        var renderers = go.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled).ToArray();
        if (renderers.Length > 0) {
          var b = renderers[0].bounds;
          for (var i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
          var offset = new Vector3(origin.x - b.center.x, origin.y - b.min.y, origin.z - b.center.z);
          go.transform.position += offset;
          b.center += offset;

          var radius = Mathf.Max(b.extents.magnitude, 0.6f);
          var dist = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.18f;
          // Front-right 3/4 view (+Z is forward for Synty units) so faces, front armour, and turrets face the camera cleanly.
          var dir = new Vector3(0.58f, 0.34f, 0.74f).normalized;
          cam.transform.position = b.center + dir * dist;
          cam.transform.LookAt(b.center);

          CaptureCameraPng(cam, Path.Combine(outDir, fileName), 512, 512);
          manifest.Add($"{fileName} | {slot.Id} | {slot.Prefab.name}");
        }
        Object.DestroyImmediate(go);
      }

      File.WriteAllLines(Path.Combine(outDir, "catalog_manifest.txt"), manifest);
      Debug.Log($"[FW-Catalog] Captured {manifest.Count} units to {outDir}");
      Object.DestroyImmediate(stage);
      Assert.Greater(manifest.Count, 30, "must capture full unit roster");
      yield return null;
    }
  }
}
