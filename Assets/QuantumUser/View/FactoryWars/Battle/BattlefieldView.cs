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
    static Mesh _parachute;
    const float DropHeight = 16f;
    const float DropSeconds = 2.4f;
    readonly Dictionary<EntityRef, float> _falling = new Dictionary<EntityRef, float>();
    static Mesh _pole;
    static Mesh _flag;
    static Mesh _bar;
    static Mesh _fill;

    public int SiteCount => _sites.Count;
    public int CrateCount => _crates.Count;

    /// <summary>낙하 지점 근처에 상자가 이미 내려왔는지. 신호 연기를 멈출 때 쓴다.</summary>
    public bool CrateNear(Vector3 position) {
      foreach (var crate in _crates.Values) {
        var offset = crate.transform.position - position;
        offset.y = 0f;
        if (offset.sqrMagnitude < 16f) {
          return true;
        }
      }
      return false;
    }
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

    /// <summary>주황 낙하산 덮개와 줄 네 가닥. 상자 위 4m에 펼친다.</summary>
    static Mesh ParachuteMesh() {
      if (_parachute == null) {
        var kit = new MeshKit();
        kit.Paint(Swatch.LeafAutumn).Frustum(new Vector3(0, CrateSize + 3.6f, 0), 2.2f, 0.5f, 1.1f, 8);
        kit.Paint(Swatch.White);
        foreach (var (x, z) in new[] { (1f, 1f), (-1f, 1f), (1f, -1f), (-1f, -1f) }) {
          var top = new Vector3(x * 1.9f, CrateSize + 3.6f, z * 1.9f);
          var bottom = new Vector3(x * 0.5f, CrateSize, z * 0.5f);
          kit.Box((top + bottom) / 2, new Vector3(0.05f, (top - bottom).magnitude, 0.05f));
        }
        _parachute = kit.ToMesh("supply_parachute");
      }
      return _parachute;
    }

    /// <summary>점령지 옆 구조물: 통신소는 둥근 기단과 안테나·접시, 발전 중계소는 송전탑과 변압기. 원 밖에 두어 유닛과 겹치지 않는다.</summary>
    static Mesh StationMesh(CaptureKind kind) {
      var kit = new MeshKit();
      if (kind == CaptureKind.Comms) {
        kit.Paint(Swatch.ConcreteDark).Cylinder(Vector3.zero, 1.8f, 0.6f, 10);
        kit.Paint(Swatch.Concrete).Cylinder(new Vector3(0, 0.6f, 0), 1.2f, 1.6f, 10);
        kit.Paint(Swatch.MetalDark).Frustum(new Vector3(0, 2.2f, 0), 0.25f, 0.08f, 7f, 6);
        kit.Paint(Swatch.Metal).Frustum(new Vector3(0.9f, 2.6f, 0), 0.9f, 0.2f, 0.5f, 10);
        kit.Paint(Swatch.GlowRed).Box(new Vector3(0, 9.3f, 0), Vector3.one * 0.25f);
      } else {
        kit.Paint(Swatch.Concrete).Box(new Vector3(0, 0.15f, 0), new Vector3(3.2f, 0.3f, 2.4f));
        kit.Paint(Swatch.MetalDark).Box(new Vector3(-0.6f, 1.1f, 0), new Vector3(1.4f, 1.6f, 1.2f));
        kit.Paint(Swatch.Metal);
        for (var i = 0; i < 3; i++) {
          kit.Cylinder(new Vector3(0.6f + i * 0.35f, 0.3f, 0.5f), 0.1f, 1.4f, 6);
        }
        kit.Paint(Swatch.MetalDark).Frustum(new Vector3(1f, 0.3f, -0.4f), 0.5f, 0.12f, 8f, 4);
        kit.Box(new Vector3(1f, 7.6f, -0.4f), new Vector3(2.6f, 0.15f, 0.15f));
        kit.Paint(Swatch.GlowCyan).Box(new Vector3(-0.6f, 1.95f, 0), new Vector3(0.9f, 0.08f, 0.7f));
      }
      kit.Shading = MeshKit.ShadeMode.Object;
      return kit.ToMesh($"station_{kind}");
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
      var station = Item(root, "Station", StationMesh(site->Kind), ArtLook.NeutralMaterial);
      station.transform.localPosition = new Vector3(0, 0, site->Radius.AsFloat + 2.4f);
      station.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.On;
      var seed = KenneyAssetMap.Seed(position, 601);
      if (site->Kind == CaptureKind.Comms) {
        DressingSpawn.Place(DressKind.SpaceFloor, station.transform, new Vector3(0f, 0.02f, 0f), 0f, seed + 10);
      }
      DressingSpawn.Place(DressKind.SpaceProp, station.transform, new Vector3(-1.3f, 0.15f, 0.2f), 90f, seed);
      DressingSpawn.Place(site->Kind == CaptureKind.Comms ? DressKind.SpaceProp : DressKind.Hopper, station.transform, new Vector3(1.3f, 0.15f, -0.2f), -90f, seed + 1);
      DressingSpawn.Place(DressKind.Prop, station.transform, new Vector3(0f, 0.15f, 1.1f), 0f, seed + 2);
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
      // 폐차 더미(Kenney car-kit, 불탄 고철 톤)를 잔해 앞뒤에 섞는다. 판정은 바리케이드 영역 그대로이고, 사라지면 같이 사라진다(항목 19C).
      var length = local.IsCircle ? local.Radius.AsFloat : local.HalfLength.AsFloat;
      var width = local.IsCircle ? local.Radius.AsFloat : local.HalfWidth.AsFloat;
      for (var i = 0; i < 4; i++) {
        var along = (i - 1.5f) / 1.5f * length * 0.8f;
        var side = (i % 2 == 0 ? 1f : -1f) * width * 0.9f;
        var offset = Quaternion.Euler(0, local.Yaw.AsFloat, 0) * new Vector3(along, 0, side);
        var tilt = MeshKit.Hash(barricade->Index, 20 + i);
        var wreck = DressingSpawn.Place(i % 2 == 0 ? DressKind.Wreck : DressKind.Debris, root, offset * 0.8f, local.Yaw.AsFloat + tilt * 70f - 35f, barricade->Index * 13 + i);
        if (wreck != null && i % 2 == 0) {
          // 옆으로 기울어진 폐차.
          wreck.transform.localRotation *= Quaternion.Euler(0f, 0f, tilt < 0.5f ? 12f : 88f);
        }
      }
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
      while (crates.NextUnsafe(out var entity, out var transform, out var supplyCrate)) {
        _seen.Add(entity);
        if (_crates.ContainsKey(entity) == false) {
          var crate = new GameObject("Crate");
          crate.transform.SetParent(_root, false);
          crate.transform.localPosition = transform->Position.ToUnityVector3();
          var body = Item(crate.transform, "Body", CrateMesh(), ArtLook.NeutralMaterial);
          body.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.On;
          var parachute = Item(body.transform, "Parachute", ParachuteMesh(), ArtLook.NeutralMaterial);
          var dressedCrate = DressingSpawn.Place(DressKind.SupplyCrate, body.transform, Vector3.zero, entity.Index * 90f, 8400 + entity.Index);
          if (dressedCrate != null) {
            dressedCrate.name = "KenneyCrate";
          }
          _crates[entity] = crate;
          if (supplyCrate->Seat >= 0) {
            parachute.SetActive(false);
          } else {
            _falling[entity] = 0f;
          }
        }
        if (_falling.TryGetValue(entity, out var age)) {
          // 하강 연출: 낙하산을 펴고 내려와 땅에 닿으면 즉시 접는다(SetActive false).
          age += deltaTime;
          var body = _crates[entity].transform.GetChild(0);
          var t = Mathf.Clamp01(age / DropSeconds);
          body.localPosition = Vector3.up * DropHeight * (1f - t) * (1f - t);
          if (t >= 1f) {
            var chute = body.Find("Parachute");
            if (chute != null) {
              chute.gameObject.SetActive(false);
            }
            _falling.Remove(entity);
          } else {
            _falling[entity] = age;
          }
        }
        // 착지 확인 안전장치: 하강 중이 아니거나 바닥 높이에 도달했으면 낙하산 무조건 비활성화
        if (_crates.TryGetValue(entity, out var crateObj) && crateObj != null) {
          var b = crateObj.transform.childCount > 0 ? crateObj.transform.GetChild(0) : null;
          if (b != null && (_falling.ContainsKey(entity) == false || b.localPosition.y <= 0.05f)) {
            var chute = b.Find("Parachute");
            if (chute != null && chute.gameObject.activeSelf) {
              chute.gameObject.SetActive(false);
            }
          }
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
        _falling.Remove(entity);
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
