namespace FactoryWars.View {
  using Photon.Deterministic;
  using Quantum;
  using Quantum.FactoryWars;
  using UnityEngine;

  /// <summary>
  /// Task 46 view (reads the simulation only): harbour and dock-basin dressing from the naval/* pack slots (quay walls, docks,
  /// boat ties, moored boats, a crane), and a gate lamp at both ends of every sea barrier (red closed, amber opening/closing,
  /// green open, dark when the barrier is gone).
  /// </summary>
  public class WaterwayView {
    readonly Transform _root;
    bool _built;
    MapSpec _map;
    Renderer[] _lamps = new Renderer[0];
    public int Props { get; private set; }

    public WaterwayView(Transform root) { _root = root; }

    public unsafe void Update(Frame frame) {
      if (SeaGateSystem.Spec(frame) == null) return;
      if (_built == false) Build(frame);
      foreach (var pair in frame.GetComponentIterator<SeaGate>()) {
        var g = pair.Component;
        var colour = g.Barrier == EntityRef.None ? new Color(0.15f, 0.15f, 0.16f)
          : g.State == SeaGateSystem.Open ? new Color(0.25f, 0.85f, 0.35f)
          : g.State == SeaGateSystem.Closed ? new Color(0.9f, 0.22f, 0.18f) : new Color(0.98f, 0.7f, 0.2f);
        for (var k = 0; k < 2; k++) {
          var i = g.Index * 2 + k;
          if (i < _lamps.Length && _lamps[i] != null) _lamps[i].sharedMaterial.SetColor("_BaseColor", colour);
        }
      }
    }

    void Build(Frame frame) {
      _built = true;
      _map = FactoryWarsUtil.MapInfo(frame);
      var parent = new GameObject("Waterway").transform;
      parent.SetParent(_root, false);
      var gates = _map.SeaGates ?? new MapShape[0];
      _lamps = new Renderer[gates.Length * 2];
      for (var i = 0; i < gates.Length; i++) {
        var c = gates[i].Center.ToUnityVector3();
        // Phase B: the barrier spans the waterway across its side (west-frame x, turned with the side).
        var across = SideTurn(c) * Vector3.right;
        var half = gates[i].HalfLength.AsFloat;
        for (var k = 0; k < 2; k++) {
          var end = c + across * (k == 0 ? -half - 0.6f : half + 0.6f);
          var lamp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
          Object.Destroy(lamp.GetComponent<Collider>());
          lamp.name = $"GateLamp{i}_{k}";
          lamp.transform.SetParent(parent, false);
          lamp.transform.position = end + Vector3.up * 3.35f;
          lamp.transform.localScale = new Vector3(0.32f, 0.24f, 0.32f);
          var r = lamp.GetComponent<Renderer>();
          r.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
          r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
          _lamps[i * 2 + k] = r;
        }
      }
      // Harbours (one per side): a quay wall along the land edge, finger docks into the basin with moored boats, boat ties, a crane
      // on the quay. Laid out in the west frame and turned with the side.
      foreach (var h in _map.Harbours ?? new MapShape[0]) {
        var turn = SideTurn(h.Center.ToUnityVector3());
        var c = Quaternion.Inverse(turn) * h.Center.ToUnityVector3();
        var edgeX = c.x + h.HalfLength.AsFloat; // land edge (quay face), west frame
        var halfZ = h.HalfWidth.AsFloat;
        var face = turn * Quaternion.Euler(0f, 90f, 0f);
        Vector3 W(float x, float y, float z) => turn * new Vector3(x, y, z);
        for (var z = -halfZ + 4f; z <= halfZ - 4f; z += 8f) Place(parent, "naval/port_wall", W(edgeX + 0.6f, -1.2f, c.z + z), face);
        foreach (var (dz, boatSide) in new[] { (-20f, 1f), (-4f, -1f), (14f, 1f) }) {
          Place(parent, "naval/dock", W(edgeX - 5f, -0.4f, c.z + dz), face);
          Place(parent, "naval/boat", W(edgeX - 6f, -0.3f, c.z + dz + boatSide * 4.2f), turn * Quaternion.Euler(0f, 90f + boatSide * 4f, 0f));
        }
        foreach (var dz in new[] { -26f, -12f, 6f, 22f }) Place(parent, "naval/boat_tie", W(edgeX + 1.4f, 0f, c.z + dz), face);
        Place(parent, "naval/crane", W(edgeX + 5f, 0f, c.z + 26f), face);
      }
      // Dock basins (sector corners): a finger dock and a moored boat in each sector's corner basin (west frame, turned).
      foreach (var g in gates) {
        var turn = SideTurn(g.Center.ToUnityVector3());
        var local = Quaternion.Inverse(turn) * g.Center.ToUnityVector3();
        var corner = new Vector3(-308f, 0f, Mathf.Sign(local.z) * 300f);
        Place(parent, "naval/dock", turn * (corner + new Vector3(8f, -0.4f, -Mathf.Sign(corner.z) * 10f)), turn * Quaternion.Euler(0f, 90f, 0f));
        Place(parent, "naval/boat", turn * (corner + new Vector3(4f, -0.3f, -Mathf.Sign(corner.z) * 14f)), turn * Quaternion.Euler(0f, 180f, 0f));
        Place(parent, "naval/dock_poles", turn * (corner + new Vector3(10f, -0.5f, -Mathf.Sign(corner.z) * 20f)), turn);
      }
    }

    /// <summary>
    /// Side of the map a point belongs to, as the rotation from the west frame (the map generator's TurnShape (x, z) -> (-z, x) per
    /// quarter turn = Euler(0, -90, 0)): west 0, south 1, east 2, north 3.
    /// </summary>
    public static Quaternion SideTurn(Vector3 p) {
      var t = Mathf.Abs(p.x) >= Mathf.Abs(p.z) ? (p.x < 0f ? 0 : 2) : (p.z < 0f ? 1 : 3);
      return Quaternion.Euler(0f, -90f * t, 0f);
    }

    void Place(Transform parent, string slotId, Vector3 world, Quaternion rotation) {
      if (VisualCatalog.TryFilled(slotId, out var slot) == false) return;
      var go = Object.Instantiate(slot.Prefab, parent);
      go.transform.position = world;
      go.transform.rotation = rotation;
      foreach (var col in go.GetComponentsInChildren<Collider>()) Object.Destroy(col);
      Props++;
    }
  }
}
