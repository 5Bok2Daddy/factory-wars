namespace FactoryWars.View {
  using System.Collections.Generic;
  using Quantum;
  using UnityEngine;
  using UnityEngine.Rendering;

  /// <summary>
  /// 점령지·보급 상자·바리케이드의 최소 화면. 시뮬레이션 상태를 읽기만 한다. 꾸밈은 항목 19에서 한다.
  /// </summary>
  public unsafe class BattlefieldView {
    const int RingSteps = 24;
    const float RingWidth = 0.6f;
    const float PoleHeight = 4.2f;
    const float FlagLow = 0.6f;
    const float BarWidth = 4f;
    const float BarHeight = 3.2f;
    const float CrateSize = 1.2f;
    const float FallSeconds = 1.2f;

    class Site {
      public GameObject Root;
      public MeshFilter Progress;
      public MeshRenderer ProgressRenderer;
      public Transform Flag;
      public MeshRenderer FlagRenderer;
      public int Step = -1;
      public int Color = -2;
    }

    class Wall {
      public GameObject Root;
      public Transform Fill;
      public float Max;
      public float Fall = -1f;
    }

    readonly Transform _root;
    readonly Dictionary<EntityRef, Site> _sites = new Dictionary<EntityRef, Site>();
    readonly Dictionary<EntityRef, GameObject> _crates = new Dictionary<EntityRef, GameObject>();
    readonly Dictionary<EntityRef, Wall> _walls = new Dictionary<EntityRef, Wall>();
    readonly HashSet<EntityRef> _seen = new HashSet<EntityRef>();
    readonly List<EntityRef> _gone = new List<EntityRef>();
    static Mesh _crate;
    static Mesh _pole;
    static Mesh _flag;
    static Mesh _bar;
    static Mesh _fill;

    public int SiteCount => _sites.Count;
    public int CrateCount => _crates.Count;
    public int WallCount => _walls.Count;

    public BattlefieldView(Transform parent) {
      _root = new GameObject("Battlefield").transform;
      _root.SetParent(parent, false);
    }

    static GameObject Item(Transform parent, string name, Mesh mesh, Material material) {
      var item = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
      item.transform.SetParent(parent, false);
      item.GetComponent<MeshFilter>().sharedMesh = mesh;
      var renderer = item.GetComponent<MeshRenderer>();
      renderer.sharedMaterial = material;
      renderer.shadowCastingMode = ShadowCastingMode.Off;
      return item;
    }

    /// <summary>바닥에 눕힌 원호. fraction만큼만 그린다.</summary>
    public static Mesh Arc(float radius, float fraction) {
      var kit = new MeshKit().Team();
      var segments = Mathf.Max(1, Mathf.RoundToInt(48 * fraction));
      for (var i = 0; i < segments; i++) {
        var a = Mathf.PI * 2f * fraction * i / segments;
        var b = Mathf.PI * 2f * fraction * (i + 1) / segments;
        Vector3 P(float angle, float r) => new Vector3(Mathf.Sin(angle) * r, 0.08f, Mathf.Cos(angle) * r);
        kit.Quad(P(a, radius - RingWidth), P(a, radius), P(b, radius), P(b, radius - RingWidth), Vector3.up);
      }
      return kit.ToMesh("capture_progress");
    }

    static Mesh CrateMesh() {
      if (_crate == null) {
        var kit = new MeshKit();
        kit.Paint(Swatch.Earth).Box(new Vector3(0, CrateSize / 2, 0), Vector3.one * CrateSize);
        kit.Paint(Swatch.MetalDark).Box(new Vector3(0, CrateSize / 2, 0), new Vector3(CrateSize + 0.06f, 0.16f, CrateSize + 0.06f));
        kit.Paint(Swatch.White).Box(new Vector3(0, CrateSize + 0.02f, 0), new Vector3(0.5f, 0.04f, 0.16f));
        kit.Box(new Vector3(0, CrateSize + 0.02f, 0), new Vector3(0.16f, 0.04f, 0.5f));
        _crate = kit.ToMesh("supply_crate");
      }
      return _crate;
    }

    static Mesh PoleMesh() {
      if (_pole == null) {
        var kit = new MeshKit();
        kit.Paint(Swatch.MetalDark).Cylinder(Vector3.zero, 0.7f, 0.3f, 8);
        kit.Paint(Swatch.Metal).Cylinder(Vector3.zero, 0.09f, PoleHeight, 6);
        _pole = kit.ToMesh("capture_pole");
      }
      return _pole;
    }

    static Mesh FlagMesh() {
      if (_flag == null) {
        var kit = new MeshKit().Team();
        kit.Box(new Vector3(0.75f, 0, 0), new Vector3(1.4f, 0.9f, 0.05f));
        _flag = kit.ToMesh("capture_flag");
      }
      return _flag;
    }

    static Mesh BarMesh(Swatch swatch, float inset, string name) {
      var kit = new MeshKit().Paint(swatch);
      kit.Quad(new Vector3(-0.5f + inset, -0.5f + inset, 0), new Vector3(0.5f - inset, -0.5f + inset, 0), new Vector3(0.5f - inset, 0.5f - inset, 0), new Vector3(-0.5f + inset, 0.5f - inset, 0), Vector3.back);
      return kit.ToMesh(name);
    }

    Site CreateSite(CaptureSite* site, Vector3 position) {
      var root = new GameObject("CaptureSite").transform;
      root.SetParent(_root, false);
      root.localPosition = position;
      var ring = new MeshKit().Paint(Swatch.MetalDark);
      ring.Annulus(new Vector3(0, 0.05f, 0), site->Radius.AsFloat - RingWidth, site->Radius.AsFloat, 48);
      Item(root, "Ring", ring.ToMesh("capture_ring"), ArtLook.NeutralMaterial);
      var progress = Item(root, "Progress", null, ArtLook.NeutralMaterial);
      Item(root, "Pole", PoleMesh(), ArtLook.NeutralMaterial);
      var flag = Item(root, "Flag", FlagMesh(), ArtLook.NeutralMaterial);
      return new Site {
        Root = root.gameObject, Progress = progress.GetComponent<MeshFilter>(), ProgressRenderer = progress.GetComponent<MeshRenderer>(),
        Flag = flag.transform, FlagRenderer = flag.GetComponent<MeshRenderer>(),
      };
    }

    static Material TeamOf(Frame frame, PlayerRef player, bool glow) {
      if (player.IsValid == false || (int)player >= frame.PlayerCount) {
        return ArtLook.NeutralMaterial;
      }
      return ArtLook.Team(frame.Global->Players.GetPointer(player)->ColorIndex, glow);
    }

    void UpdateSite(Frame frame, Site view, CaptureSite* site, float captureSeconds) {
      var owned = site->Owner.IsValid;
      var fraction = owned ? 1f : Mathf.Clamp01(site->Progress.AsFloat / captureSeconds);
      var step = Mathf.RoundToInt(fraction * RingSteps);
      // 다른 편이 중립화하는 중이면 소유자 색 링이 줄어든다.
      if (owned && site->Claimant.IsValid) {
        step = RingSteps - Mathf.RoundToInt(Mathf.Clamp01(site->Progress.AsFloat / captureSeconds) * RingSteps);
      }
      if (step != view.Step) {
        view.Step = step;
        if (view.Progress.sharedMesh != null) {
          Object.Destroy(view.Progress.sharedMesh);
        }
        view.Progress.sharedMesh = step > 0 ? Arc(site->Radius.AsFloat, step / (float)RingSteps) : null;
      }
      var color = owned ? (int)site->Owner : site->Claimant.IsValid ? (int)site->Claimant : -1;
      if (color != view.Color) {
        view.Color = color;
        view.ProgressRenderer.sharedMaterial = TeamOf(frame, owned ? site->Owner : site->Claimant, false);
        view.FlagRenderer.sharedMaterial = TeamOf(frame, owned ? site->Owner : site->Claimant, owned);
      }
      view.Flag.localPosition = Vector3.up * Mathf.Lerp(FlagLow, PoleHeight - 0.5f, owned ? 1f : fraction);
    }

    Wall CreateWall(Barricade* barricade, Vector3 position) {
      var root = new GameObject("Barricade").transform;
      root.SetParent(_root, false);
      root.localPosition = position;
      var kit = new MeshKit();
      var local = barricade->Shape;
      local.Center = default;
      TerrainKit.Boulders(kit, local, 300 + barricade->Index);
      Item(root, "Rubble", kit.ToMesh("barricade"), ArtLook.NeutralMaterial).GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.On;
      _bar ??= BarMesh(Swatch.Black, 0f, "barricade_bar");
      _fill ??= BarMesh(Swatch.LeafLight, 0.06f, "barricade_fill");
      var bar = Item(root, "Bar", _bar, ArtLook.NeutralMaterial).transform;
      bar.localPosition = Vector3.up * BarHeight;
      bar.localRotation = Quaternion.Euler(50f, 0, 0);
      bar.localScale = new Vector3(BarWidth, 0.42f, 1f);
      var fill = Item(bar, "Fill", _fill, ArtLook.NeutralMaterial).transform;
      fill.localPosition = new Vector3(0, 0, -0.01f);
      return new Wall { Root = root.gameObject, Fill = fill, Max = barricade->Health.AsFloat };
    }

    public void Update(Frame frame, float deltaTime) {
      var rules = frame.FindAsset(frame.RuntimeConfig.Rules);
      _seen.Clear();
      var sites = frame.Filter<Transform3D, CaptureSite>();
      while (sites.NextUnsafe(out var entity, out var transform, out var site)) {
        _seen.Add(entity);
        if (_sites.TryGetValue(entity, out var view) == false) {
          view = CreateSite(site, transform->Position.ToUnityVector3());
          _sites[entity] = view;
        }
        UpdateSite(frame, view, site, rules.CaptureSeconds.AsFloat);
      }
      var crates = frame.Filter<Transform3D, SupplyCrate>();
      while (crates.NextUnsafe(out var entity, out var transform, out _)) {
        _seen.Add(entity);
        if (_crates.ContainsKey(entity) == false) {
          var crate = Item(_root, "Crate", CrateMesh(), ArtLook.NeutralMaterial);
          crate.transform.localPosition = transform->Position.ToUnityVector3();
          crate.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.On;
          _crates[entity] = crate;
        }
      }
      var walls = frame.Filter<Transform3D, Barricade>();
      while (walls.NextUnsafe(out var entity, out var transform, out var barricade)) {
        _seen.Add(entity);
        if (_walls.TryGetValue(entity, out var wall) == false) {
          wall = CreateWall(barricade, transform->Position.ToUnityVector3());
          _walls[entity] = wall;
        }
        var ratio = Mathf.Clamp01(barricade->Health.AsFloat / wall.Max);
        wall.Fill.localScale = new Vector3(ratio, 1, 1);
        wall.Fill.localPosition = new Vector3((ratio - 1f) * 0.5f, 0, -0.01f);
      }
      Collect(deltaTime);
    }

    /// <summary>사라진 상자는 지우고, 무너진 바리케이드는 가라앉힌 뒤 지운다. 무너지는 꾸밈은 항목 19에서 한다.</summary>
    void Collect(float deltaTime) {
      _gone.Clear();
      foreach (var pair in _crates) {
        if (_seen.Contains(pair.Key) == false) {
          _gone.Add(pair.Key);
        }
      }
      foreach (var entity in _gone) {
        Object.Destroy(_crates[entity]);
        _crates.Remove(entity);
      }
      _gone.Clear();
      foreach (var pair in _walls) {
        var wall = pair.Value;
        if (_seen.Contains(pair.Key)) {
          continue;
        }
        if (wall.Fall < 0f) {
          wall.Fall = 0f;
          wall.Root.transform.Find("Bar").gameObject.SetActive(false);
        }
        wall.Fall += deltaTime / FallSeconds;
        wall.Root.transform.localScale = new Vector3(1f + wall.Fall * 0.4f, Mathf.Max(0.05f, 1f - wall.Fall), 1f + wall.Fall * 0.4f);
        if (wall.Fall >= 1f) {
          _gone.Add(pair.Key);
        }
      }
      foreach (var entity in _gone) {
        Object.Destroy(_walls[entity].Root);
        _walls.Remove(entity);
      }
    }
  }
}
