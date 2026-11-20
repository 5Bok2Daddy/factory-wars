namespace FactoryWars.View {
  using System.Collections.Generic;
  using Quantum;
  using UnityEngine;

  /// <summary>활주로 포장과 조선 중인 선체를 경기 상태에 맞춰 표시한다.</summary>
  public sealed class FacilityWorkView : MonoBehaviour {
    const int Slabs = 6;
    readonly List<Mesh> _meshes = new List<Mesh>();
    readonly List<Transform> _slabs = new List<Transform>();
    GameObject _markings;
    ArtView _hull;
    Transform _cradle;
    float _display = -1f;
    float _hullAltitude;
    public float Progress => _display;

    public static FacilityWorkView CreatePaving(Transform parent, MapShape site, WorldView world, bool helipad) {
      var work = new GameObject(helipad ? "LandingPadWork" : "RunwayWork").AddComponent<FacilityWorkView>();
      work.transform.SetParent(parent, false);
      var position = site.Center.ToUnityVector3();
      position.y = TerrainKit.SurfaceHeight(world.Map, position.x, position.z);
      var rotation = Quaternion.Euler(0f, site.Yaw.AsFloat, 0f);
      work.transform.SetPositionAndRotation(position, rotation);
      var halfLength = site.HalfLength.AsFloat;
      var halfWidth = site.HalfWidth.AsFloat;
      var footprint = new GroundFootprint(position, new Vector3(halfLength, 0f, halfWidth), rotation);
      DressingRenderer.Active?.ReserveSite(footprint);
      world.ClearBiomeProps(item => footprint.Overlaps(item));
      var length = 2f * halfLength / Slabs;
      var surface = new MeshKit().Paint(Swatch.Asphalt);
      surface.Box(new Vector3(length * 0.5f, 0.052f, 0f), new Vector3(length, 0.10f, halfWidth * 2f));
      var slab = work.Mesh(surface, "PavingSlab");
      for (var i = 0; i < Slabs; i++) {
        var part = AddMesh(work.transform, "PavingSlab", slab).transform;
        part.localPosition = Vector3.right * (-halfLength + i * length);
        part.gameObject.SetActive(false);
        work._slabs.Add(part);
      }
      var paint = new MeshKit().Paint(Swatch.LaneWhite);
      const float height = 0.112f;
      if (helipad) {
        paint.Box(new Vector3(0f, height, 0f), new Vector3(0.25f, 0.01f, halfWidth * 0.48f));
        for (var side = -1; side <= 1; side += 2) {
          paint.Box(new Vector3(0f, height, side * halfWidth * 0.24f), new Vector3(halfLength * 0.78f, 0.01f, 0.25f));
          paint.Box(new Vector3(side * (halfLength - 0.55f), height, 0f), new Vector3(0.15f, 0.01f, halfWidth * 1.76f));
          paint.Box(new Vector3(0f, height, side * (halfWidth - 0.55f)), new Vector3(halfLength * 1.76f, 0.01f, 0.15f));
        }
      } else {
        for (var i = -2; i <= 2; i++) {
          paint.Box(new Vector3(i * halfLength * 0.31f, height, 0f), new Vector3(halfLength * 0.15f, 0.01f, 0.19f));
        }
        for (var side = -1; side <= 1; side += 2) {
          paint.Box(new Vector3(0f, height, side * (halfWidth - 0.47f)), new Vector3(halfLength * 1.85f, 0.01f, 0.14f));
          for (var stripe = -2; stripe <= 2; stripe++) {
            paint.Box(new Vector3(side * (halfLength - 1.15f), height, stripe * halfWidth * 0.22f),
              new Vector3(1.0f, 0.01f, 0.24f));
          }
        }
      }
      work._markings = AddMesh(work.transform, "AirfieldMarkings", work.Mesh(paint, "AirfieldMarkings"));
      work._markings.SetActive(false);
      return work;
    }

    public static FacilityWorkView CreateVessel(Transform parent, ViewCatalog catalog, string unit, int team, Vector3 launch, Vector3 shore) {
      var work = new GameObject("ShipUnderConstruction").AddComponent<FacilityWorkView>();
      work.transform.SetParent(parent, false);
      var outward = launch - shore;
      outward.y = 0f;
      launch.y = TerrainKit.WaterLevel + 0.06f;
      var along = Vector3.Cross(Vector3.up, outward);
      work.transform.SetPositionAndRotation(launch, Quaternion.LookRotation(along.sqrMagnitude > 0.01f ? along : Vector3.forward));
      work._hull = ArtView.Create(catalog, unit, work.transform, team);
      if (work._hull == null) {
        Destroy(work.gameObject);
        return null;
      }
      work._hullAltitude = Mathf.Max(0f, work._hull.Turn.localPosition.y);
      foreach (var collider in work._hull.GetComponentsInChildren<Collider>(true)) collider.enabled = false;

      var radius = work._hull.Entry.RingRadius * ArtView.ScaleOf(work._hull.Entry);
      var width = Mathf.Clamp(radius * 0.67f, 1.15f, 3.8f);
      var length = Mathf.Clamp(radius * 1.10f, 2.8f, 6.8f);
      var cradle = new MeshKit().Paint(Swatch.ArmorDark);
      for (var x = -1; x <= 1; x += 2) {
        cradle.Box(new Vector3(x * width, 0.18f, 0f), new Vector3(0.38f, 0.36f, length * 2f));
        for (var step = -1; step <= 1; step++) {
          var z = step * length * 0.81f;
          cradle.Beam(new Vector3(x * width, 0.17f, z), new Vector3(x * width * 0.7f, 0.86f, z), 0.13f);
        }
      }
      work._cradle = AddMesh(work.transform, "FloatingCradle", work.Mesh(cradle, "FloatingCradle")).transform;
      return work;
    }

    public void UpdatePaving(float progress, float dt) {
      Follow(progress, dt);
      for (var i = 0; i < _slabs.Count; i++) {
        var amount = Mathf.Clamp01(_display * Slabs - i);
        var slab = _slabs[i];
        if (slab.gameObject.activeSelf != (amount > 0f)) slab.gameObject.SetActive(amount > 0f);
        slab.localScale = new Vector3(amount, 1f, 1f);
      }
      if (_markings.activeSelf != (_display >= 1f)) _markings.SetActive(_display >= 1f);
    }

    public void UpdateVessel(float progress, float dt) {
      if (_display - progress > 0.5f) _display = progress;
      Follow(progress, dt);
      var rise = Mathf.SmoothStep(0f, 1f, _display);
      _hull.Turn.localPosition = Vector3.up * Mathf.Lerp(-0.18f, _hullAltitude, rise);
      _hull.Turn.localScale = new Vector3(1f, Mathf.Lerp(0.27f, 1f, rise), 1f);
      _cradle.localPosition = Vector3.down * Mathf.SmoothStep(0f, 0.45f, Mathf.InverseLerp(0.8f, 1f, _display));
    }

    void Follow(float progress, float dt) => _display = _display < 0f ? progress : Mathf.MoveTowards(_display, progress, dt * 1.8f);

    Mesh Mesh(MeshKit kit, string name) {
      var mesh = kit.ToMesh(name);
      _meshes.Add(mesh);
      return mesh;
    }

    static GameObject AddMesh(Transform parent, string name, Mesh mesh) {
      var item = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
      item.transform.SetParent(parent, false);
      item.GetComponent<MeshFilter>().sharedMesh = mesh;
      item.GetComponent<MeshRenderer>().sharedMaterial = ArtLook.NeutralMaterial;
      return item;
    }

    void OnDestroy() {
      foreach (var mesh in _meshes) Destroy(mesh);
    }
  }
}
