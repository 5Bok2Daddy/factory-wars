namespace FactoryWars.View {
  using System.Collections.Generic;
  using Quantum;
  using UnityEngine;
  using UnityEngine.Rendering;

  /// <summary>
  /// MapSpec에서 바닥·지형·자원·다리를 만들고 시뮬레이션 상태를 따라 바꾼다. 상태는 읽기만 한다.
  /// </summary>
  public unsafe class WorldView {
    const int ForestSteps = 10;
    const float MineModelRadius = 3f;
    const float BarrierSeconds = 1.7f;
    const float SlowBlink = 1.6f;
    const float FastBlink = 0.32f;
    const float FastBlinkFrom = 10f;

    class Forest {
      public MapShape Shape;
      public int Seed;
      public float Amount;
      public int Step = -1;
      public MeshFilter Filter;
    }

    class Barrier {
      public GameObject Root;
      public MeshRenderer Lamps;
      public List<Vector3> Dust;
    }

    readonly List<Barrier> _barriers = new List<Barrier>();
    float _barrierDrop = -1f;
    readonly Transform _root;
    readonly MapSpec _map;
    readonly ViewCatalog _catalog;
    readonly Dictionary<EntityRef, Forest> _forests = new Dictionary<EntityRef, Forest>();
    readonly Dictionary<EntityRef, GameObject> _crystals = new Dictionary<EntityRef, GameObject>();
    public BattleFx Fx { get; set; }
    public int BarrierCount => _barriers.Count;
    public float BarrierDrop => Mathf.Max(_barrierDrop, 0f);
    public bool BarrierVisible => _barriers.Exists(b => b.Root.activeSelf);
    public int ForestCount => _forests.Count;
    GameObject _noBuild;
    public bool NoBuildShown => _noBuild == null || _noBuild.activeSelf;

    /// <summary>건설 위치를 고를 때만 금지 표시를 켠다.</summary>
    public void ShowNoBuild(bool show) {
      if (_noBuild != null && _noBuild.activeSelf != show) {
        _noBuild.SetActive(show);
      }
    }
    public int MineCount => _crystals.Count;

    public MapSpec Map => _map;

    public WorldView(Transform parent, MapSpec map, ViewCatalog catalog) {
      _root = new GameObject("World").transform;
      _root.SetParent(parent, false);
      _map = map;
      _catalog = catalog;
      BuildStatic();
    }

    static Material _ground;

    /// <summary>바닥 전용 공유 머티리얼: 공용 셰이더에 정점 색을 켜고 접촉 AO·높이 그라데이션은 끈다.</summary>
    static Material GroundMaterial() {
      if (_ground == null) {
        var shader = Shader.Find("FactoryWars/KenneyLit");
        if (shader == null) {
          return null;
        }
        _ground = new Material(shader) { name = "GroundVertexColor" };
        _ground.SetFloat("_UseVertexColor", 1f);
        _ground.SetFloat("_Desaturate", 0f);
        _ground.SetFloat("_Brightness", 1f);
        _ground.SetFloat("_AoStrength", 0f);
        _ground.SetFloat("_GradientStrength", 0f);
        _ground.SetFloat("_NoiseStrength", 0.05f);
        _ground.SetFloat("_NoiseScale", 0.05f);
      }
      return _ground;
    }

    static GameObject MeshObject(Transform parent, string name, Mesh mesh, bool casts) {
      var item = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
      item.transform.SetParent(parent, false);
      item.GetComponent<MeshFilter>().sharedMesh = mesh;
      var renderer = item.GetComponent<MeshRenderer>();
      renderer.sharedMaterial = ArtLook.NeutralMaterial;
      renderer.shadowCastingMode = casts ? ShadowCastingMode.On : ShadowCastingMode.Off;
      return item;
    }

    void BuildStatic() {
      var ground = new MeshKit { Shading = MeshKit.ShadeMode.Ground };
      TerrainKit.Ground(ground, _map, 3);
      var groundMesh = ground.ToMesh("world_ground");
      var groundObject = MeshObject(_root, "Ground", groundMesh, false);
      if (VisualProfile.Current.GroundVertexColors && GroundMaterial() != null) {
        // 정점 색 바닥(항목 25-1): 정점 위치마다 색을 골라 부드럽게 섞는다. 머티리얼은 하나를 같이 쓴다.
        var vertices = groundMesh.vertices;
        var colors = new Color[vertices.Length];
        var linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
        for (var v = 0; v < vertices.Length; v++) {
          var tint = TerrainKit.GroundTint(_map, vertices[v].x, vertices[v].z, 3);
          colors[v] = linear ? tint.linear : tint;
        }
        groundMesh.colors = colors;
        groundObject.GetComponent<MeshRenderer>().sharedMaterial = GroundMaterial();
      }
      var features = new MeshKit { Shading = MeshKit.ShadeMode.Ground };
      Dressing.Begin();
      TerrainKit.Features(features, _map);
      Dressing.End("world");
      Dressing.Begin();
      Scatter.Fill(_map);
      Dressing.End("scatter");
      if (features.TriangleCount > 0) {
        MeshObject(_root, "Features", features.ToMesh("world_features"), true);
      }
      if (VisualProfile.Current.NoBuildAlways == false) {
        var marks = new MeshKit();
        TerrainKit.NoBuildMarks(marks, _map);
        if (marks.TriangleCount > 0) {
          _noBuild = MeshObject(_root, "NoBuild", marks.ToMesh("world_nobuild"), false);
          for (var z = 0; _map.NoBuildZones != null && z < _map.NoBuildZones.Length; z++) {
            var center = _map.NoBuildZones[z].Center.ToUnityVector3();
            var ind = DressingSpawn.Place(DressKind.BuildIndicator, _noBuild.transform, center + Vector3.up * 0.04f, z * 90f, 8100 + z);
            if (ind != null) {
              ind.name = "BuildIndicator";
            }
          }
          _noBuild.SetActive(false);
        }
      }
      foreach (var shape in _map.Bridges ?? new MapShape[0]) {
        var rect = TerrainKit.Extent(shape);
        var view = ArtView.Create(_catalog, "bridge", _root, 0);
        view.transform.localPosition = new Vector3(rect.center.x, 0, rect.center.y);
        // 프리팹의 다리는 Z축으로 건넌다. 사각형의 짧은 변이 건너는 방향이다.
        view.SetYaw(rect.width < rect.height ? 90f : 0f);
      }
    }

    /// <summary>임시 도형을 끈다. NavMesh를 굽는 데 쓴 물체라 지우지 않는다.</summary>
    public static void HideBakedScene() {
      foreach (var name in new[] { "Terrain", "Ground", "Water" }) {
        var item = GameObject.Find(name);
        if (item == null) {
          continue;
        }
        foreach (var renderer in item.GetComponentsInChildren<Renderer>(true)) {
          renderer.enabled = false;
        }
      }
    }

    /// <summary>갤러리와 같은 빛을 쓴다.</summary>
    public static void ApplyLighting(Camera camera) {
      var profile = VisualProfile.Current;
      RenderSettings.ambientMode = AmbientMode.Trilight;
      RenderSettings.ambientSkyColor = profile.AmbientSky;
      RenderSettings.ambientEquatorColor = profile.AmbientEquator;
      RenderSettings.ambientGroundColor = profile.AmbientGround;
      RenderSettings.fog = false;
      var sun = RenderSettings.sun;
      if (sun == null) {
        foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.InstanceID)) {
          if (light.type == LightType.Directional) {
            sun = light;
            break;
          }
        }
      }
      if (sun == null) {
        sun = new GameObject("Sun").AddComponent<Light>();
        sun.type = LightType.Directional;
      }
      sun.color = profile.SunColor;
      sun.intensity = profile.SunIntensity;
      sun.shadows = GraphicsQuality.Current == QualityTier.Low ? LightShadows.Hard : LightShadows.Soft;
      sun.shadowStrength = profile.ShadowStrength;
      sun.transform.rotation = Quaternion.Euler(profile.SunElevation, profile.SunAzimuth, 0);
      // 맵 밖 처리: 안개는 멀리 있는 경계 밖을 어둡게 감싼다.
      RenderSettings.fog = profile.Outside == OutsideMode.Fog;
      RenderSettings.fogMode = FogMode.ExponentialSquared;
      RenderSettings.fogDensity = 0.0022f;
      RenderSettings.fogColor = new Color(0.22f, 0.24f, 0.26f);
      if (camera != null) {
        camera.clearFlags = CameraClearFlags.SolidColor;
        // 맵 밖은 어둡게 끊는다. 안개면 안개 색과 맞춘다.
        camera.backgroundColor = profile.Outside == OutsideMode.Fog ? RenderSettings.fogColor : new Color(0.11f, 0.12f, 0.13f);
        camera.allowHDR = true;
        camera.farClipPlane = 700f;
      }
    }

    int NodeIndex(ResourceNode* node) {
      var nodes = _map.ResourceNodes;
      for (var i = 0; nodes != null && i < nodes.Length; i++) {
        if (nodes[i].Shape.Center == node->Shape.Center) {
          return i;
        }
      }
      return 0;
    }

    void AddNode(EntityRef entity, ResourceNode* node) {
      var index = NodeIndex(node);
      var center = new Vector3(node->Shape.Center.X.AsFloat, 0, node->Shape.Center.Z.AsFloat);
      if (node->Kind == ResourceKind.Iron) {
        var view = ArtView.Create(_catalog, "iron_mine", _root, 0);
        view.transform.position = center;
        view.Model.transform.localScale = Vector3.one * (node->Shape.Radius.AsFloat / MineModelRadius);
        view.SetYaw(index * 57f);
        _crystals[entity] = view.Model.transform.Find("Crystals").gameObject;
        // 고철·광산 설비 느낌: 둘레에 호퍼·고철 하치장(skip-rocks)·크레인·작업 차량(Kenney, 판정 없음 — 항목 19C·19D).
        Dressing.Begin();
        var radius = node->Shape.Radius.AsFloat;
        // 작업 차량(삼각형 2,700개)은 성능 예산 때문에 컨테이너로 바꿨다(항목 25-1).
        DressKind[] kinds = { DressKind.Hopper, DressKind.ScrapSkip, DressKind.Crane, DressKind.Container };
        for (var i = 0; i < kinds.Length; i++) {
          var angle = (index * 1.7f + i * (Mathf.PI * 2f / kinds.Length)) % (Mathf.PI * 2f);
          var spot = center + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * (radius * 0.88f + MeshKit.Hash(index, 40 + i) * 0.9f);
          Dressing.Add(kinds[i], spot, index * 97 + i);
        }
        Dressing.End($"iron{index}");
        return;
      }
      var item = MeshObject(_root, "Forest", null, true);
      item.transform.SetPositionAndRotation(center, Quaternion.Euler(0, node->Shape.Yaw.AsFloat, 0));
      var amount = _map.ResourceNodes != null && index < _map.ResourceNodes.Length ? _map.ResourceNodes[index].Amount.AsFloat : node->Remaining.AsFloat;
      _forests[entity] = new Forest { Shape = node->Shape, Seed = 11 + index * 7, Amount = Mathf.Max(amount, 1f), Filter = item.GetComponent<MeshFilter>() };
    }

    static void UpdateForest(Forest forest, float remaining) {
      var step = Mathf.CeilToInt(Mathf.Clamp01(remaining / forest.Amount) * ForestSteps);
      if (step == forest.Step) {
        return;
      }
      forest.Step = step;
      var kit = new MeshKit { Shading = MeshKit.ShadeMode.Object };
      // Kenney 나무가 있으면 나무는 인스턴싱 그룹으로 가고 kit에는 바닥 판만 남는다. 채집 단계가 바뀌면 그룹을 바꿔 끼운다.
      var toWorld = forest.Filter.transform.localToWorldMatrix;
      Dressing.Begin();
      if (forest.Shape.IsCircle) {
        ForestKit.Grove(kit, forest.Shape.Radius.AsFloat, forest.Seed, step / (float)ForestSteps, toWorld);
      } else {
        ForestKit.Forest(kit, forest.Shape.HalfLength.AsFloat, forest.Shape.HalfWidth.AsFloat, forest.Seed, step / (float)ForestSteps, toWorld);
      }
      Dressing.End($"forest{forest.Seed}");
      if (forest.Filter.sharedMesh != null) {
        Object.Destroy(forest.Filter.sharedMesh);
      }
      forest.Filter.sharedMesh = kit.ToMesh("forest");
    }

    /// <summary>경고등 깜빡임 주기. 남은 시간이 10초 이하면 빠르다.</summary>
    public static float BlinkPeriod(float remainingSeconds) => remainingSeconds <= FastBlinkFrom ? FastBlink : SlowBlink;

    int NearestBase(Vector3 position) {
      var best = 0;
      var bestDistance = float.MaxValue;
      for (var i = 0; _map.BaseAreas != null && i < _map.BaseAreas.Length; i++) {
        var center = _map.BaseAreas[i].Center.ToUnityVector3();
        var distance = (center - position).sqrMagnitude;
        if (distance < bestDistance) {
          best = i;
          bestDistance = distance;
        }
      }
      return best;
    }

    /// <summary>다리마다 울타리 하나. 빈 본진 자리의 다리는 시작부터 열려 있으므로 울타리를 세우지 않는다.</summary>
    void BuildBarriers(Frame frame) {
      for (var i = 0; _map.Bridges != null && i < _map.Bridges.Length; i++) {
        var ownerSeat = NearestBase(_map.Bridges[i].Center.ToUnityVector3());
        var owner = Quantum.FactoryWars.FactoryWarsUtil.PlayerAtSeat(frame, ownerSeat);
        if (owner < 0) {
          continue;
        }
        var body = new MeshKit();
        var lamps = new MeshKit();
        var pieces = TerrainKit.BarrierPieces(_map, i);
        var dust = new List<Vector3>();
        foreach (var piece in pieces) {
          TerrainKit.Fence(body, lamps, piece);
          dust.Add((piece.From + piece.To) / 2);
        }
        var item = MeshObject(_root, "Barrier", body.ToMesh("barrier"), true);
        var state = owner < frame.PlayerCount ? frame.Global->Players.GetPointer(owner) : null;
        if (state != null && state->Active) {
          item.GetComponent<MeshRenderer>().sharedMaterial = ArtLook.Team(state->ColorIndex, true);
        }
        var light = MeshObject(item.transform, "Lamps", lamps.ToMesh("barrier_lamps"), false);
        _barriers.Add(new Barrier { Root = item, Lamps = light.GetComponent<MeshRenderer>(), Dust = dust });
      }
    }

    void UpdateBarriers(Frame frame, float deltaTime) {
      var open = (bool)frame.Global->BarrierOpen;
      if (_barrierDrop < 0f) {
        BuildBarriers(frame);
        _barrierDrop = open ? 1f : 0f;
        if (open) {
          foreach (var barrier in _barriers) {
            barrier.Root.SetActive(false);
          }
        }
      }
      if (open == false) {
        var remaining = (frame.Global->TruceEndFrame - frame.Number) / (float)frame.SessionConfig.UpdateFPS;
        var period = BlinkPeriod(remaining);
        var lit = Mathf.Repeat(Time.time, period) < period / 2;
        foreach (var barrier in _barriers) {
          barrier.Lamps.enabled = lit;
        }
        return;
      }
      if (_barrierDrop >= 1f) {
        return;
      }
      if (_barrierDrop <= 0f) {
        foreach (var barrier in _barriers) {
          foreach (var point in barrier.Dust) {
            Fx?.GroundDust(point);
          }
        }
      }
      _barrierDrop = Mathf.MoveTowards(_barrierDrop, 1f, deltaTime / BarrierSeconds);
      var eased = _barrierDrop * _barrierDrop * (3f - 2f * _barrierDrop);
      foreach (var barrier in _barriers) {
        barrier.Root.transform.localPosition = Vector3.down * (TerrainKit.FenceHeight * eased);
        if (_barrierDrop >= 1f) {
          barrier.Root.SetActive(false);
        }
      }
    }

    /// <summary>고갈되면 결정만 사라진다. 바위는 남아 건설 검사와 맞는다.</summary>
    public static void ShowCrystals(GameObject crystals, float remaining) {
      var alive = remaining > 0f;
      if (crystals.activeSelf != alive) {
        crystals.SetActive(alive);
      }
    }

    public void Update(Frame frame, float deltaTime) {
      if (frame == null) {
        return;
      }
      UpdateBarriers(frame, deltaTime);
      var nodes = frame.Filter<ResourceNode>();
      while (nodes.NextUnsafe(out var entity, out var node)) {
        if (_forests.TryGetValue(entity, out var forest)) {
          UpdateForest(forest, node->Remaining.AsFloat);
        } else if (_crystals.TryGetValue(entity, out var crystals)) {
          ShowCrystals(crystals, node->Remaining.AsFloat);
        } else {
          AddNode(entity, node);
        }
      }
    }
  }
}
