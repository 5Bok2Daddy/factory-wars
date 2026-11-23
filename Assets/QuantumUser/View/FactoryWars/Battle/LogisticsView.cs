namespace FactoryWars.View {
  using System.Collections.Generic;
  using Photon.Deterministic;
  using Quantum;
  using Quantum.FactoryWars;
  using UnityEngine;

  /// <summary>
  /// Task 41 view (reads the simulation only): per edge site a depot dressing with its own character (west timber depot, north
  /// waterfront freight yard, south maintenance yard, east industrial warehouse yard) around a marked loading bay; per seat a
  /// garage + receiving dock with marked bays; a buffer bar over every yard; a state plate over every logistics truck
  /// (green 이동 중, amber 적재/하역 중, grey 귀환, red 경로 대기). Models come from the logi/* VisualCatalog slots (LogisticsKit).
  /// </summary>
  public class LogisticsView {
    readonly Transform _root;
    bool _built;
    MapSpec _map;
    float Ground(Vector3 p) => _map == null ? 0f : TerrainKit.SurfaceHeight(_map, p.x, p.z);
    readonly Dictionary<int, (Transform Bar, Transform Fill)> _bars = new Dictionary<int, (Transform, Transform)>();
    readonly Dictionary<EntityRef, Renderer> _plates = new Dictionary<EntityRef, Renderer>();
    Material _bayPaint, _barBack, _barFill, _plateMat;
    public int DepotProps { get; private set; }
    public int Docks { get; private set; }
    public Vector3 WestBay { get; private set; }
    public Vector3 DockOfSeat0 { get; private set; }

    public LogisticsView(Transform root) { _root = root; }

    static Material Flat(Color c) {
      var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
      m.SetColor("_BaseColor", c);
      return m;
    }

    public unsafe void Update(Frame frame) {
      var spec = LogisticsSystem.Spec(frame);
      if (spec == null) return;
      if (_built == false) Build(frame, spec);
      foreach (var pair in frame.GetComponentIterator<LogisticsYard>()) {
        if (_bars.TryGetValue(pair.Component.Site, out var bar) == false) continue;
        var b = pair.Component.Buffer;
        var fill = Mathf.Clamp01(((b.Iron + b.Wood + b.Steel + b.Power) / spec.BufferCapacity).AsFloat);
        bar.Fill.localScale = new Vector3(Mathf.Max(0.001f, fill) * 6f, 0.5f, 1f);
        bar.Fill.localPosition = new Vector3(-3f + fill * 3f, 0f, -0.02f);
        bar.Bar.gameObject.SetActive(pair.Component.Owner != PlayerRef.None);
      }
      var seen = new HashSet<EntityRef>();
      foreach (var pair in frame.GetComponentIterator<LogisticsTruck>()) {
        seen.Add(pair.Entity);
        if (frame.TryGet<Transform3D>(pair.Entity, out var t) == false) continue;
        if (_plates.TryGetValue(pair.Entity, out var plate) == false || plate == null) {
          var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
          Object.Destroy(go.GetComponent<Collider>());
          go.name = "LogisticsTruckState";
          go.transform.SetParent(_root, false);
          go.transform.localScale = new Vector3(2.2f, 0.6f, 1f);
          plate = go.GetComponent<Renderer>();
          plate.sharedMaterial = new Material(_plateMat);
          plate.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
          _plates[pair.Entity] = plate;
        }
        var p = t.Position.ToUnityVector3();
        plate.transform.position = p + Vector3.up * 5.2f;
        plate.transform.rotation = Camera.main != null ? Quaternion.LookRotation(Camera.main.transform.forward) : Quaternion.identity;
        plate.sharedMaterial.SetColor("_BaseColor", StateColour(pair.Component.State));
      }
      var gone = new List<EntityRef>();
      foreach (var kv in _plates) if (seen.Contains(kv.Key) == false) gone.Add(kv.Key);
      foreach (var e in gone) { if (_plates[e] != null) Object.Destroy(_plates[e].gameObject); _plates.Remove(e); }
    }

    public static Color StateColour(LogisticsState s) {
      switch (s) {
        case LogisticsState.Loading:
        case LogisticsState.Unloading: return new Color(0.95f, 0.71f, 0.29f);
        case LogisticsState.Aborting: return new Color(0.6f, 0.6f, 0.6f);
        case LogisticsState.WaitingRoute: return new Color(0.9f, 0.25f, 0.2f);
        default: return new Color(0.35f, 0.8f, 0.45f);
      }
    }

    /// <summary>Korean state name for HUD/tooltips.</summary>
    public static string StateName(LogisticsState s) {
      switch (s) {
        case LogisticsState.Loading: return "적재 중";
        case LogisticsState.Unloading: return "하역 중";
        case LogisticsState.Aborting: return "귀환";
        case LogisticsState.WaitingRoute: return "경로 대기";
        default: return "이동 중";
      }
    }

    unsafe void Build(Frame frame, LogisticsSpec spec) {
      _built = true;
      _bayPaint = Flat(new Color(0.95f, 0.78f, 0.25f));
      _barBack = Flat(new Color(0.12f, 0.13f, 0.15f));
      _barFill = Flat(new Color(0.95f, 0.71f, 0.29f));
      _plateMat = Flat(Color.white);
      var parent = new GameObject("Logistics").transform;
      parent.SetParent(_root, false);
      _map = FactoryWarsUtil.MapInfo(frame);
      foreach (var pair in frame.GetComponentIterator<LogisticsYard>()) {
        var yard = pair.Component;
        var site = yard.Position.ToUnityVector3();
        site.y = Ground(site);
        var outward = new Vector3(site.x, 0f, site.z).normalized;
        // Bay toward the base that sits clockwise-before this site (the yard's real bay depends on the owner; mark both sides).
        var depot = new GameObject($"Depot{yard.Site}").transform;
        depot.SetParent(parent, false);
        depot.position = site;
        depot.rotation = Quaternion.LookRotation(-outward, Vector3.up); // local +z = toward the map centre
        Dress(depot, KindOf(site));
        // Marked loading bays: two (one per neighbouring base), 4 x 9 m frames.
        foreach (var side in new[] { -1f, 1f }) Frame(depot, new Vector3(side * 10f, 0.06f, 0f), 4.2f, 9.5f);
        if (Mathf.Abs(site.x) > Mathf.Abs(site.z) && site.x < 0f) WestBay = site + depot.rotation * new Vector3(-10f, 0f, 0f);
        // Buffer bar.
        var bar = new GameObject($"Buffer{yard.Site}").transform;
        bar.SetParent(parent, false);
        bar.position = site + Vector3.up * 9f;
        bar.rotation = Quaternion.Euler(50f, 0f, 0f);
        Quad(bar, "Back", _barBack, Vector3.zero, new Vector3(6.3f, 0.75f, 1f));
        var fill = Quad(bar, "Fill", _barFill, new Vector3(-3f, 0f, -0.02f), new Vector3(0.001f, 0.5f, 1f));
        _bars[yard.Site] = (bar, fill);
      }
      // Per seat: garage + receiving dock with four marked bays (one per site) behind the start position.
      for (var seat = 0; seat < 4 && seat < frame.PlayerCount; seat++) {
        var player = (PlayerRef)seat;
        if (FactoryWarsUtil.Player(frame, player) == null) continue;
        var start = FactoryWarsUtil.StartPosition(frame, player).ToUnityVector3();
        start.y = Ground(start);
        var inward = (-new Vector3(start.x, 0f, start.z)).normalized;
        var dockRoot = new GameObject($"Dock{seat}").transform;
        dockRoot.SetParent(parent, false);
        var centre = Vector3.zero;
        for (var s = 0; s < 4; s++) {
          var d = LogisticsSystem.DockOf(frame, spec, player, s).ToUnityVector3();
          centre += d / 4f;
          Frame(dockRoot, d + Vector3.up * 0.06f, 4.2f, 9.5f, Quaternion.LookRotation(inward));
        }
        if (seat == 0) DockOfSeat0 = centre;
        // Garage building and a low dock canopy behind the bays (away from the base interior road).
        Place(dockRoot, "logi/garage", centre - inward * 11f, Quaternion.LookRotation(inward));
        Docks++;
      }
    }

    static string KindOf(Vector3 site) => Mathf.Abs(site.x) > Mathf.Abs(site.z) ? (site.x < 0f ? "west" : "east") : (site.z > 0f ? "north" : "south");

    /// <summary>Site character, local frame: +z toward the map centre, x along the edge. Props sit behind the bays (z &lt; -6).</summary>
    void Dress(Transform depot, string kind) {
      switch (kind) {
        case "west": // timber depot: low timber warehouse, grouped lumber stacks, planted strip with tree clusters
          Place(depot, "logi/warehouse_low", new Vector3(0f, 0f, -12f), 0f);
          foreach (var (x, z, yaw) in new[] { (-14f, -9f, 0f), (-14f, -12f, 0f), (14f, -9f, 0f), (14f, -12f, 0f) }) Place(depot, "logi/lumber_long", new Vector3(x, 0f, z), yaw);
          foreach (var (x, z) in new[] { (-6f, -18f), (6f, -18f) }) Place(depot, "logi/lumber", new Vector3(x, 0f, z), 90f);
          TreeCluster(depot, new Vector3(-22f, 0f, -16f), 3);
          TreeCluster(depot, new Vector3(22f, 0f, -16f), 3);
          break;
        case "north": // waterfront freight yard: container rows and a crane by the canal side
          foreach (var (x, z, yaw) in new[] { (-14f, -11f, 0f), (-14f, -14f, 0f), (14f, -11f, 0f), (17f, -14f, 0f), (0f, -16f, 90f) }) Place(depot, "logi/container", new Vector3(x, 0f, z), yaw);
          Place(depot, "logi/crane", new Vector3(-4f, 0f, -20f), 0f);
          TreeCluster(depot, new Vector3(22f, 0f, -4f), 2);
          break;
        case "south": // maintenance yard: garage sheds and parked truck
          Place(depot, "logi/garage", new Vector3(-12f, 0f, -14f), 0f);
          Place(depot, "logi/garage", new Vector3(12f, 0f, -14f), 0f);
          Place(depot, "logi/lumber", new Vector3(0f, 0f, -17f), 0f);
          TreeCluster(depot, new Vector3(-22f, 0f, -6f), 2);
          break;
        default: // east industrial warehouse yard
          Place(depot, "logi/warehouse_big", new Vector3(0f, 0f, -22f), 0f);
          foreach (var x in new[] { -16f, 16f }) Place(depot, "logi/container", new Vector3(x, 0f, -10f), 90f);
          TreeCluster(depot, new Vector3(22f, 0f, -4f), 2);
          break;
      }
    }

    void TreeCluster(Transform parent, Vector3 at, int n) {
      for (var i = 0; i < n; i++) {
        var a = i * 2.4f + at.x * 0.37f;
        Place(parent, i % 2 == 0 ? "logi/tree_a" : "logi/tree_b", at + new Vector3(Mathf.Cos(a) * 2.6f, 0f, Mathf.Sin(a) * 2.6f), i * 67f);
      }
    }

    void Place(Transform parent, string slotId, Vector3 local, float yaw) => Place(parent, slotId, parent.TransformPoint(local), parent.rotation * Quaternion.Euler(0f, yaw, 0f));

    void Place(Transform parent, string slotId, Vector3 world, Quaternion rotation) {
      if (VisualCatalog.TryFilled(slotId, out var slot) == false) return;
      var go = Object.Instantiate(slot.Prefab, parent);
      go.transform.position = new Vector3(world.x, Ground(world), world.z);
      go.transform.rotation = rotation;
      go.transform.localScale = Vector3.one * (slot.Scale > 0f ? slot.Scale : 1f);
      foreach (var c in go.GetComponentsInChildren<Collider>()) Object.Destroy(c);
      DepotProps++;
    }

    void Frame(Transform parent, Vector3 centre, float w, float l, Quaternion? rotation = null) {
      var rot = rotation ?? parent.rotation;
      var world = rotation.HasValue ? centre : parent.TransformPoint(centre);
      foreach (var (dx, dz, sx, sz) in new[] { (-w / 2f, 0f, 0.18f, l), (w / 2f, 0f, 0.18f, l), (0f, -l / 2f, w, 0.18f) }) {
        var q = Quad(parent, "BayLine", _bayPaint, Vector3.zero, new Vector3(sx, sz, 1f));
        q.position = world + rot * new Vector3(dx, 0f, dz);
        q.position = new Vector3(q.position.x, Ground(q.position) + 0.07f, q.position.z);
        q.rotation = rot * Quaternion.Euler(90f, 0f, 0f);
      }
    }

    static Transform Quad(Transform parent, string name, Material mat, Vector3 local, Vector3 scale) {
      var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
      Object.Destroy(go.GetComponent<Collider>());
      go.name = name;
      go.transform.SetParent(parent, false);
      go.transform.localPosition = local;
      go.transform.localScale = scale;
      var r = go.GetComponent<Renderer>();
      r.sharedMaterial = mat;
      r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
      return go.transform;
    }
  }
}
