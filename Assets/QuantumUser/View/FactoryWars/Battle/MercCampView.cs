namespace FactoryWars.View {
  using System.Collections.Generic;
  using Quantum;
  using Quantum.FactoryWars;
  using UnityEngine;

  /// <summary>
  /// Task 39 card 4: living PMC camp (reads the simulation only).
  /// - Garrison: one soldier per MercGuard entity at its simulated position (moves when sortieing, aims and fires at its target).
  /// - Ambience (view-only, driven by the frame clock so every client shows the same thing): patrol pairs on the road loop, a squad
  ///   jogging round the training yard, operators on the firing line, sentries at gate and towers, mechanics at the bays.
  /// - Display line ("shop window"): the parked vehicles follow the shop stock (NpcTrader.Stock); a sold vehicle drives/flies out of
  ///   the main gate, a restocked one appears again. Models are camp slots baked by CampKit (pack models, pack dark colour variants).
  /// </summary>
  public class MercCampView {
    const string GuardSlot = "unit/pmc_soldier";
    const string FallbackSlot = "unit/trader_guard";
    const float SimStep = 0.05f;

    readonly Transform _root;
    GameObject _camp;
    bool _built;

    class Soldier {
      public Transform View;
      public UnitAnimatorBridge Anim;
      public List<RiflemanRig> Rigs;
      public Vector3 Last;
      public List<Renderer> Gear = new List<Renderer>();
    }

    readonly Dictionary<EntityRef, Soldier> _garrison = new Dictionary<EntityRef, Soldier>();
    readonly List<(Soldier S, int Kind, int Index)> _ambient = new List<(Soldier, int, int)>();

    class Display {
      public Transform View;
      public CampLayout.Bay Kind;
      public Vector3 Home;
      public float Yaw;
      public bool Shown;
      public float LeaveT = -1f;
    }
    readonly List<Display> _display = new List<Display>();

    public int GarrisonShown => _garrison.Count;
    public int AmbientCount => _ambient.Count;
    public int DisplayShown { get { var n = 0; foreach (var d in _display) if (d.Shown && d.LeaveT < 0f) n++; return n; } }
    public bool Built => _built;
    /// <summary>Shown display vehicles (for the colour audit).</summary>
    public IEnumerable<(Transform View, CampLayout.Bay Kind)> Displays { get { foreach (var d in _display) if (d.Shown && d.LeaveT < 0f) yield return (d.View, d.Kind); } }
    public IEnumerable<Transform> Troops { get { foreach (var s in _garrison.Values) yield return s.View; foreach (var (s, _, _) in _ambient) yield return s.View; } }

    public MercCampView(Transform root) {
      _root = root;
    }

    static Soldier MakeSoldier(Transform parent, string name, bool female = false) {
      if ((female == false || VisualCatalog.TryFilled(GuardSlot + "_f", out var slot) == false) && VisualCatalog.TryFilled(GuardSlot, out slot) == false && VisualCatalog.TryFilled(FallbackSlot, out slot) == false) return null;
      var view = new GameObject(name).transform;
      view.SetParent(parent, false);
      var model = Object.Instantiate(slot.Prefab, view);
      var (offset, _) = VisualCatalog.Measure(slot.Prefab, slot);
      var scale = slot.Scale > 0f ? slot.Scale : 1f;
      model.transform.localScale = Vector3.one * scale;
      model.transform.localPosition = offset * scale;
      // Performance (Task 39 C1): a pack character is many part renderers. Keep only the largest skinned body mesh, no shadows.
      SkinnedMeshRenderer body = null;
      foreach (var r in model.GetComponentsInChildren<SkinnedMeshRenderer>()) {
        if (r.sharedMesh != null && (body == null || r.sharedMesh.vertexCount > body.sharedMesh.vertexCount)) body = r;
      }
      foreach (var r in model.GetComponentsInChildren<Renderer>()) {
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        if (body != null && r is SkinnedMeshRenderer && r != body) r.enabled = false;
      }
      var soldier = new Soldier { View = view, Anim = UnitAnimatorBridge.Attach(model, slot), Rigs = RiflemanRig.AttachAll(model, BattleView.Weapon(DressKind.Rifle)) };
      // Rifles are several renderers and ~3k triangles each; they are only drawn close up (see Cull).
      foreach (var r in model.GetComponentsInChildren<Renderer>()) {
        if (r is SkinnedMeshRenderer == false) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; soldier.Gear.Add(r); }
      }
      return soldier;
    }

    void Build() {
      _built = true;
      _camp = new GameObject("MercCamp");
      _camp.transform.SetParent(_root, false);
      // Ambient troops: kind 0 patrol, 1 jogger, 2 range shooter, 3 sentry, 4 mechanic.
      for (var i = 0; i < 6; i++) Add(0, i);
      for (var i = 0; i < 6; i++) Add(1, i);
      for (var i = 0; i < 9; i++) Add(5, i);
      for (var i = 0; i < CampLayout.FiringLine.Length; i++) Add(2, i);
      for (var i = 0; i < CampLayout.Sentries.Length; i++) Add(3, i);
      for (var i = 0; i < CampLayout.Mechanics.Length; i++) Add(4, i);
      for (var i = 0; i < CampLayout.Counter.Length; i++) Add(6, i);
      // Display vehicles.
      foreach (var (position, yaw, kind) in CampLayout.Bays) {
        var id = "camp/display_" + kind.ToString().ToLowerInvariant();
        if (VisualCatalog.TryFilled(id, out var slot) == false) continue;
        var view = Object.Instantiate(slot.Prefab, _camp.transform).transform;
        view.name = id;
        view.localScale = Vector3.one * (slot.Scale > 0f ? slot.Scale : 1f);
        view.localPosition = position + Vector3.up * (kind == CampLayout.Bay.Drone ? 0.05f : 0.02f);
        view.localRotation = Quaternion.Euler(0f, yaw, 0f);
        _display.Add(new Display { View = view, Kind = kind, Home = view.localPosition, Yaw = yaw, Shown = true });
      }

      void Add(int kind, int index) {
        var s = MakeSoldier(_camp.transform, $"PmcAmbient{kind}_{index}", (kind * 7 + index) % 3 == 0);
        if (s != null) _ambient.Add((s, kind, index));
      }
    }

    const float CullDistance = 220f;

    /// <summary>Hide soldiers far from the camera (they are a few pixels tall there).</summary>
    const float GearDistance = 70f;

    static void Cull(Soldier s, Vector3 camera) {
      var d2 = (s.View.position - camera).sqrMagnitude;
      var show = d2 < CullDistance * CullDistance;
      if (s.View.gameObject.activeSelf != show) s.View.gameObject.SetActive(show);
      var gear = d2 < GearDistance * GearDistance;
      foreach (var r in s.Gear) if (r != null && r.enabled != gear) r.enabled = gear;
    }

    public void Update(Frame frame, float dt) {
      var map = FactoryWarsUtil.MapInfo(frame);
      if (map == null || map.MercenaryCamp == false) return;
      NpcTrader trader = default;
      var found = false;
      foreach (var pair in frame.GetComponentIterator<NpcTrader>()) { trader = pair.Component; found = true; }
      if (found == false) return;
      if (_built == false) Build();
      if (trader.Destroyed) { _camp.SetActive(false); return; }
      var t = frame.Number * SimStep;
      Garrison(frame, dt);
      Ambience(t, dt);
      DisplayLine(frame, trader, dt);
      var cam = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
      foreach (var s in _garrison.Values) Cull(s, cam);
      foreach (var (s, _, _) in _ambient) Cull(s, cam);
    }

    void Garrison(Frame frame, float dt) {
      var seen = new HashSet<EntityRef>();
      foreach (var pair in frame.GetComponentIterator<MercGuard>()) {
        seen.Add(pair.Entity);
        if (_garrison.TryGetValue(pair.Entity, out var s) == false) {
          s = MakeSoldier(_camp.transform, "PmcGarrison" + pair.Component.Slot);
          if (s == null) return;
          s.View.position = frame.Get<Transform3D>(pair.Entity).Position.ToUnityVector3();
          s.Last = s.View.position;
          _garrison[pair.Entity] = s;
        }
        var at = frame.Get<Transform3D>(pair.Entity).Position.ToUnityVector3();
        var target = pair.Component.Target;
        var aiming = frame.Exists(target);
        var aim = aiming ? frame.Get<Transform3D>(target).Position.ToUnityVector3() : Vector3.zero;
        var moved = (at - s.Last).magnitude;
        s.View.position = Vector3.Lerp(s.View.position, at, Mathf.Clamp01(dt * 12f));
        var look = aiming ? aim - at : at - s.Last;
        if (look.sqrMagnitude > 0.0004f) s.View.rotation = Quaternion.Slerp(s.View.rotation, Quaternion.LookRotation(new Vector3(look.x, 0f, look.z)), Mathf.Clamp01(dt * 8f));
        foreach (var rig in s.Rigs) { rig.Aiming = aiming; rig.AimPoint = aim + Vector3.up; }
        s.Anim?.Set(moved > 0.001f ? UnitAnimState.Move : aiming ? UnitAnimState.Attack : UnitAnimState.Idle, moved / Mathf.Max(dt, 1e-3f));
        s.Last = at;
      }
      var gone = new List<EntityRef>();
      foreach (var kv in _garrison) if (seen.Contains(kv.Key) == false) gone.Add(kv.Key);
      foreach (var e in gone) { Object.Destroy(_garrison[e].View.gameObject); _garrison.Remove(e); }
    }

    /// <summary>Shot origin for a garrison shot (muzzle of the nearest garrison soldier), or null when none is near.</summary>
    public Vector3? ShotOrigin(Vector3 from, Vector3 to) {
      Soldier best = null;
      var bestD = 9f;
      foreach (var s in _garrison.Values) {
        var d = (s.View.position - from).sqrMagnitude;
        if (d < bestD) { bestD = d; best = s; }
      }
      if (best == null) return null;
      if (best.Rigs.Count > 0) { best.Rigs[0].Fire(); return best.Rigs[0].Muzzle; }
      return best.View.position + Vector3.up * 1.4f;
    }

    void Ambience(float t, float dt) {
      foreach (var (s, kind, index) in _ambient) {
        Vector3 p;
        float yaw;
        var state = UnitAnimState.Idle;
        var speed = 0f;
        switch (kind) {
          case 0: {
            // Two patrol pairs walking the loop in opposite directions (pairs 1.3 m apart).
            var pair = index / 2;
            var dist = (pair % 2 == 0 ? 1f : -1f) * 1.4f * t + pair * 41f + (index % 2) * 1.3f;
            (p, yaw) = CampLayout.Loop(dist, pair % 2 == 0 ? 1.4f : -1.4f);
            if (pair % 2 == 1) yaw += 180f;
            state = UnitAnimState.Move; speed = 1.4f;
            break;
          }
          case 1: {
            // Squad jogging round the training yard rectangle in file.
            var c = CampLayout.TrainingCentre;
            var h = CampLayout.TrainingHalf;
            var per = 4f * (h.x + h.y);
            var d = Mathf.Repeat(3.2f * t - index * 2.2f, per);
            if (d < 2f * h.x) { p = c + new Vector3(-h.x + d, 0f, h.y); yaw = 90f; }
            else if (d < 2f * h.x + 2f * h.y) { p = c + new Vector3(h.x, 0f, h.y - (d - 2f * h.x)); yaw = 180f; }
            else if (d < 4f * h.x + 2f * h.y) { p = c + new Vector3(h.x - (d - 2f * h.x - 2f * h.y), 0f, -h.y); yaw = 270f; }
            else { p = c + new Vector3(-h.x, 0f, -h.y + (d - 4f * h.x - 2f * h.y)); yaw = 0f; }
            state = UnitAnimState.Move; speed = 3.2f;
            break;
          }
          case 2: {
            p = CampLayout.FiringLine[index];
            yaw = 180f;
            state = UnitAnimState.Attack;
            // Staggered practice shots every ~1.7 s.
            var beat = Mathf.FloorToInt((t + index * 0.43f) / 1.7f);
            if (s.Rigs.Count > 0 && beat != Mathf.FloorToInt((t - dt + index * 0.43f) / 1.7f)) s.Rigs[0].Fire();
            foreach (var rig in s.Rigs) { rig.Aiming = true; rig.AimPoint = p + new Vector3(0f, 1f, -12f); }
            break;
          }
          case 3: {
            p = CampLayout.Sentries[index];
            yaw = Mathf.Atan2(p.x, p.z) * Mathf.Rad2Deg + Mathf.Sin(t * 0.3f + index) * 25f;
            break;
          }
          case 5: {
            // Drill squad: 3 x 3 block facing the instructor, alternating squats (attack pose) and standing.
            p = CampLayout.DrillCentre + new Vector3((index % 3 - 1) * 2.4f, 0f, (index / 3 - 1) * 2.4f);
            yaw = 270f;
            state = Mathf.Repeat(t * 0.5f + (index % 3) * 0.05f, 2f) < 1f ? UnitAnimState.Attack : UnitAnimState.Idle;
            break;
          }
          case 6: {
            // Trading-post counter: the vendor faces the customers (south), customers face the counter.
            p = CampLayout.Counter[index];
            yaw = index == 0 ? 180f : 0f;
            state = index == 0 && Mathf.Repeat(t * 0.4f, 3f) < 1f ? UnitAnimState.Gather : UnitAnimState.Idle;
            break;
          }
          default: {
            p = CampLayout.Mechanics[index];
            yaw = 90f * index + Mathf.Sin(t * 0.5f + index) * 40f;
            state = UnitAnimState.Gather;
            break;
          }
        }
        s.View.localPosition = p;
        s.View.localRotation = Quaternion.Euler(0f, yaw, 0f);
        s.Anim?.Set(state, speed);
      }
    }

    unsafe void DisplayLine(Frame frame, NpcTrader trader, float dt) {
      var spec = MercenaryCamp.Spec(frame);
      if (spec == null || spec.Offers == null) return;
      // Stock per display kind (by the offered unit's name).
      var stock = new Dictionary<CampLayout.Bay, int>();
      for (var i = 0; i < spec.Offers.Length && i < MercenaryCamp.MaxOffers; i++) {
        if (spec.Offers[i].Unit.IsValid == false) continue;
        var name = frame.FindAsset(spec.Offers[i].Unit).name;
        CampLayout.Bay? kind = name.Contains("scout_heli") ? CampLayout.Bay.ScoutHeli : name.Contains("tank") ? CampLayout.Bay.Tank : name.Contains("suv") || name.Contains("mrap") ? CampLayout.Bay.Mrap
          : name.Contains("drone") ? CampLayout.Bay.Drone : name.Contains("heli") || name.Contains("gunship") ? CampLayout.Bay.Heli : (CampLayout.Bay?)null;
        if (kind.HasValue) stock[kind.Value] = (stock.TryGetValue(kind.Value, out var n) ? n : 0) + trader.Stock[i];
      }
      var used = new Dictionary<CampLayout.Bay, int>();
      foreach (var d in _display) {
        used[d.Kind] = used.TryGetValue(d.Kind, out var u) ? u + 1 : 1;
        var want = (stock.TryGetValue(d.Kind, out var have) ? have : 0) >= used[d.Kind];
        if (want && (d.Shown == false || d.LeaveT >= 0f)) {
          d.Shown = true; d.LeaveT = -1f;
          d.View.gameObject.SetActive(true);
          d.View.localPosition = d.Home; d.View.localRotation = Quaternion.Euler(0f, d.Yaw, 0f);
        } else if (want == false && d.Shown && d.LeaveT < 0f) {
          d.LeaveT = 0f;
        }
        if (d.LeaveT >= 0f) {
          // Sold: leave through the main gate (air goods climb and fly off).
          d.LeaveT += dt;
          var air = d.Kind == CampLayout.Bay.Drone || d.Kind == CampLayout.Bay.Heli || d.Kind == CampLayout.Bay.ScoutHeli;
          var exit = CampLayout.MainGate + new Vector3(0f, air ? 18f : 0f, -12f);
          var a = Mathf.Clamp01(d.LeaveT / 6f);
          var from = d.Home + (air ? Vector3.up * Mathf.Clamp01(d.LeaveT) * 4f : Vector3.zero);
          var p = air ? Vector3.Lerp(from, exit, a * a) : (a < 0.4f ? Vector3.Lerp(d.Home, new Vector3(d.Home.x, 0f, -21f), a / 0.4f) : Vector3.Lerp(new Vector3(0f, 0f, -21f), exit, (a - 0.4f) / 0.6f));
          var dir = p - d.View.localPosition;
          if (dir.sqrMagnitude > 0.0001f) d.View.localRotation = Quaternion.Slerp(d.View.localRotation, Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.z)), Mathf.Clamp01(dt * 4f));
          d.View.localPosition = p;
          if (a >= 1f) { d.Shown = false; d.LeaveT = -1f; d.View.gameObject.SetActive(false); }
        }
      }
    }
  }
}
