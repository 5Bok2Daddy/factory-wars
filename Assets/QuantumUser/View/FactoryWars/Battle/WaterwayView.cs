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
        var alongX = gates[i].HalfLength >= gates[i].HalfWidth;
        var half = (alongX ? gates[i].HalfLength : gates[i].HalfWidth).AsFloat;
        for (var k = 0; k < 2; k++) {
          var end = c + (alongX ? Vector3.right : Vector3.forward) * (k == 0 ? -half - 0.6f : half + 0.6f);
          var lamp = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
          Object.Destroy(lamp.GetComponent<Collider>());
          lamp.name = $"GateLamp{i}_{k}";
          lamp.transform.SetParent(parent, false);
          lamp.transform.position = end + Vector3.up * 3.4f;
          lamp.transform.localScale = new Vector3(0.45f, 0.25f, 0.45f);
          var r = lamp.GetComponent<Renderer>();
          r.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
          r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
          _lamps[i * 2 + k] = r;
        }
      }
      // Harbours: a quay wall along the land edge, finger docks into the basin with moored boats, boat ties, a crane on the quay.
      foreach (var h in _map.Harbours ?? new MapShape[0]) {
        var c = h.Center.ToUnityVector3();
        var west = c.x < 0f && Mathf.Abs(c.x) > Mathf.Abs(c.z);
        if (west == false) continue; // phase A: the west harbour only
        var edgeX = c.x + h.HalfLength.AsFloat; // land edge (quay face)
        var halfZ = h.HalfWidth.AsFloat;
        var face = Quaternion.Euler(0f, 90f, 0f);
        for (var z = -halfZ + 4f; z <= halfZ - 4f; z += 8f) Place(parent, "naval/port_wall", new Vector3(edgeX + 0.6f, -1.2f, c.z + z), face);
        foreach (var (dz, boatSide) in new[] { (-20f, 1f), (-4f, -1f), (14f, 1f) }) {
          Place(parent, "naval/dock", new Vector3(edgeX - 5f, -0.4f, c.z + dz), face);
          Place(parent, "naval/boat", new Vector3(edgeX - 6f, -0.3f, c.z + dz + boatSide * 4.2f), Quaternion.Euler(0f, 90f + boatSide * 4f, 0f));
        }
        foreach (var dz in new[] { -26f, -12f, 6f, 22f }) Place(parent, "naval/boat_tie", new Vector3(edgeX + 1.4f, 0f, c.z + dz), face);
        Place(parent, "naval/crane", new Vector3(edgeX + 5f, 0f, c.z + 26f), face);
      }
      // Dock basins (sector corners): a finger dock and a moored boat in each west corner basin.
      foreach (var g in gates) {
        var corner = new Vector3(-308f, 0f, Mathf.Sign(g.Center.Z.AsFloat) * 300f);
        Place(parent, "naval/dock", corner + new Vector3(8f, -0.4f, -Mathf.Sign(corner.z) * 10f), Quaternion.Euler(0f, 90f, 0f));
        Place(parent, "naval/boat", corner + new Vector3(4f, -0.3f, -Mathf.Sign(corner.z) * 14f), Quaternion.Euler(0f, 180f, 0f));
        Place(parent, "naval/dock_poles", corner + new Vector3(10f, -0.5f, -Mathf.Sign(corner.z) * 20f), Quaternion.identity);
      }
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
