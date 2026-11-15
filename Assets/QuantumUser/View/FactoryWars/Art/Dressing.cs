namespace FactoryWars.View {
  using System.Collections.Generic;
  using System.Linq;
  using Quantum;
  using UnityEngine;
  using UnityEngine.Rendering;

  /// <summary>
  /// Kenney 모델 배치(항목 19B). 화면 코드가 Begin(그룹) → Add(...) → End()로 모은 개체를 DressingRenderer가 GPU 인스턴싱으로 그린다.
  /// 맵(VisualProfile.Dressing)이 없거나 그 종류가 비어 있으면 Has가 false이고, 호출한 쪽은 절차 메쉬를 그대로 쓴다.
  /// </summary>
  public static class Dressing {
    public struct Item {
      public DressKind Kind;
      public GameObject Prefab;
      public Matrix4x4 World;
      public bool Shadows;
      /// <summary>0~1 해시. 품질 낮음에서는 이 값이 LowKeep보다 작은 것만 남긴다.</summary>
      public float Keep;
      public float LowKeep;
      public float MediumKeep;
      /// <summary>합친 메쉬의 정점 색에 곱하는 색(절벽 단 밝기, 그을린 나무 등). 기본 흰색.</summary>
      public Color Tint;
    }

    static List<Item> _collector;

    public static KenneyAssetMap Map => VisualProfile.Current.Dressing;

    public static bool Has(DressKind kind) => Map != null && Map.Has(kind);

    public static bool Collecting => _collector != null;

    public static void Begin() {
      _collector = new List<Item>();
    }

    /// <summary>모은 개체를 그룹 이름으로 넘긴다. 같은 이름이면 바꿔 끼운다.</summary>
    public static void End(string group) {
      var items = _collector;
      _collector = null;
      if (items != null && DressingRenderer.Active != null) {
        DressingRenderer.Active.Set(group, items);
      }
    }

    /// <summary>
    /// 종류 하나를 놓는다. scale은 엔트리 배율에 곱하는 축별 배율이다. yaw가 null이면 엔트리 범위에서 해시로 고른다.
    /// 놓았으면 true. 모으는 중이 아니거나 종류가 비어 있으면 false다.
    /// </summary>
    public static bool Add(DressKind kind, Vector3 position, int seed, Vector3? scale = null, float? yaw = null) => Add(kind, position, seed, Color.white, scale, yaw);

    /// <summary>틴트를 곱해 놓는다(합친 메쉬에서만 보인다).</summary>
    public static bool Add(DressKind kind, Vector3 position, int seed, Color tint, Vector3? scale = null, float? yaw = null) {
      if (_collector == null || Map == null) {
        return false;
      }
      var entry = Map.Get(kind);
      if (entry == null) {
        return false;
      }
      var (variant, rotation, size) = KenneyAssetMap.Place(entry, seed);
      if (yaw.HasValue) {
        rotation = Quaternion.Euler(0f, yaw.Value, 0f);
      }
      _collector.Add(new Item {
        Kind = kind,
        Prefab = variant.Prefab,
        World = KenneyAssetMap.Matrix(variant, position, rotation, (scale ?? Vector3.one) * size),
        Shadows = entry.Shadows,
        Keep = MeshKit.Hash(seed, 77),
        LowKeep = entry.LowKeep,
        MediumKeep = entry.MediumKeep,
        Tint = tint,
      });
      return true;
    }

    /// <summary>시가지 장식용: 품질별 남김 비율을 직접 정해 놓는다(낮음·보통에서 해시가 이 값보다 크면 그리지 않는다).</summary>
    public static bool AddKept(DressKind kind, Vector3 position, int seed, float yaw, float lowKeep, float mediumKeep) {
      if (_collector == null || Map == null) {
        return false;
      }
      var entry = Map.Get(kind);
      if (entry == null) {
        return false;
      }
      var (variant, rotation, size) = KenneyAssetMap.Place(entry, seed);
      rotation = Quaternion.Euler(0f, yaw, 0f);
      _collector.Add(new Item {
        Kind = kind,
        Prefab = variant.Prefab,
        World = KenneyAssetMap.Matrix(variant, position, rotation, Vector3.one * size),
        Shadows = entry.Shadows,
        Keep = MeshKit.Hash(seed, 77),
        LowKeep = lowKeep,
        MediumKeep = mediumKeep,
        Tint = Color.white,
      });
      return true;
    }

    /// <summary>정해진 칸(월드 길이 length × width, 회전 yaw)에 맞춰 놓는다. 건물 부지처럼 밖으로 튀어나오면 안 되는 곳에 쓴다.</summary>
    public static bool AddFitted(DressKind kind, Vector3 position, float yaw, float length, float width, int seed, float margin = 0.94f) {
      if (_collector == null || Map == null) {
        return false;
      }
      var entry = Map.Get(kind);
      if (entry == null) {
        return false;
      }
      var variant = KenneyAssetMap.Pick(entry, MeshKit.Hash(seed, 1));
      var turned = MeshKit.Hash(seed, 2) < 0.5f;
      var fit = KenneyAssetMap.FitScale(variant, length, width, turned) * margin * entry.Scale;
      var rotation = Quaternion.Euler(0f, yaw + (turned ? 90f : 0f), 0f);
      _collector.Add(new Item { Kind = kind, Prefab = variant.Prefab, World = KenneyAssetMap.Matrix(variant, position, rotation, Vector3.one * fit), Shadows = entry.Shadows, Keep = 0f, LowKeep = 1f, MediumKeep = 1f });
      return true;
    }
  }

  /// <summary>
  /// 빈 땅 흩뿌리기(항목 19C): 풀포기·작은 바위·덤불을 5m 격자에 해시로 놓는다.
  /// 도로·다리·강·본진·건설 금지 구역·지형·자원·도시 건물·점령지 위는 피한다. 모두 통과 가능한 작은 장식이다.
  /// </summary>
  public static class Scatter {
    const float Step = 5f;

    static bool Near(MapShape[] shapes, Vector3 point, float margin) {
      if (shapes == null) {
        return false;
      }
      var p = new Photon.Deterministic.FPVector3(Photon.Deterministic.FP.FromFloat_UNSAFE(point.x), default, Photon.Deterministic.FP.FromFloat_UNSAFE(point.z));
      foreach (var shape in shapes) {
        if (shape.Distance(p).AsFloat <= margin) {
          return true;
        }
      }
      return false;
    }

    public static int Fill(MapSpec map) {
      if (Dressing.Collecting == false || Dressing.Has(DressKind.Grass) == false || VisualProfile.Current.UrbanDecor) {
        // 항목 36 시각 교정: 시가지는 바닥에 점처럼 흩뿌린 자갈·쓰레기 소품을 두지 않는다(길가 엄폐물만 정렬 배치).
        return 0;
      }
      var paths = TerrainKit.RiverPaths(map);
      var terrain = map.Terrain?.Select(t => t.Shape).ToArray();
      var nodes = map.ResourceNodes?.Select(n => n.Shape).ToArray();
      var city = map.CityBuildings?.Select(c => c.Shape).ToArray();
      var sites = map.CaptureSites?.Select(c => new MapShape { Center = c.Center, Radius = c.Radius }).ToArray();
      var count = 0;
      var index = 0;
      for (var x = map.BoundsMin.X.AsFloat + Step / 2; x < map.BoundsMax.X.AsFloat; x += Step) {
        for (var z = map.BoundsMin.Y.AsFloat + Step / 2; z < map.BoundsMax.Y.AsFloat; z += Step) {
          index++;
          var spot = new Vector3(x + (MeshKit.Hash(index, 1) - 0.5f) * Step * 0.8f, 0f, z + (MeshKit.Hash(index, 2) - 0.5f) * Step * 0.8f);
          if (MeshKit.Hash(index, 3) > 0.7f) {
            continue;
          }
          if (TerrainKit.RiverDistance(paths, new Vector2(spot.x, spot.z)) < 3f || Near(map.Roads, spot, 1.5f) || Near(map.Bridges, spot, 2f)
              || Near(map.BaseAreas, spot, 0f) || Near(map.NoBuildZones, spot, 0f) || Near(terrain, spot, 0.5f) || Near(nodes, spot, 1.5f)
              || Near(city, spot, 1.5f) || Near(sites, spot, 1f) || Near(map.HighGrounds, spot, 1f) || Near(map.Fords, spot, 1f)) {
            continue;
          }
          var pick = MeshKit.Hash(index, 4);
          var kind = pick < 0.48f ? DressKind.Grass : pick < 0.6f && Dressing.Has(DressKind.GroundPatch) ? DressKind.GroundPatch : pick < 0.85f ? DressKind.RockSmall : DressKind.Bush;
          // 도시 건물 둘레(폐허 주변)는 차량 부품·타이어 같은 잔해와 길가 버려진 가구(high 전용)를 섞는다. 작아서 통과해도 어색하지 않다.
          if (Near(city, spot, 5f) && pick < 0.6f) {
            kind = pick < 0.45f || Dressing.Has(DressKind.Furniture) == false ? DressKind.Debris : DressKind.Furniture;
          }
          spot.y = TerrainKit.SurfaceHeight(map, spot.x, spot.z);
          count += Dressing.Add(kind, spot, 9000 + index) ? 1 : 0;
        }
      }
      return count;
    }
  }

  public static class DressingSpawn {
    /// <summary>
    /// 움직이거나 단계가 바뀌는 것(도시 건물)은 인스턴싱 대신 실제 오브젝트로 만든다. 칸(length × width)에 맞추고,
    /// 높이는 targetHeight 쪽으로 0.8~1.3배까지 늘이거나 줄인다. 만들었으면 감싼 오브젝트, 아니면 null.
    /// </summary>
    public static GameObject Fitted(DressKind kind, Transform parent, float length, float width, float targetHeight, int seed) {
      var map = Dressing.Map;
      var entry = map != null ? map.Get(kind) : null;
      if (entry == null) {
        return null;
      }
      var variant = KenneyAssetMap.Pick(entry, MeshKit.Hash(seed, 1));
      var turned = MeshKit.Hash(seed, 2) < 0.5f;
      var fit = KenneyAssetMap.FitScale(variant, length, width, turned) * 0.86f * entry.Scale;
      if (kind == DressKind.CityTower) {
        fit = Mathf.Min(fit, 3.2f);
      }
      var stretch = targetHeight > 0f ? Mathf.Clamp(targetHeight / Mathf.Max(0.01f, variant.Size.y * fit), 0.8f, 1.3f) : 1f;
      var wrapper = new GameObject("Kenney").transform;
      wrapper.SetParent(parent, false);
      wrapper.localRotation = Quaternion.Euler(0f, turned ? 90f : 0f, 0f);
      wrapper.localScale = new Vector3(fit, fit * stretch, fit);
      var model = Object.Instantiate(variant.Prefab, wrapper);
      model.transform.localPosition += variant.Offset;
      foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>()) {
        renderer.sharedMaterials = KenneyLook.Resolve(renderer.sharedMaterials);
        // 품질 낮음에서는 도시 건물 그림자를 끈다(그림자 그리기 호출 절약, 항목 25-1).
        renderer.shadowCastingMode = entry.Shadows && GraphicsQuality.Current != QualityTier.Low ? ShadowCastingMode.On : ShadowCastingMode.Off;
      }
      return wrapper.gameObject;
    }

    /// <summary>사라지거나 움직이는 것에 붙이는 Kenney 소품 하나(실제 오브젝트). localPosition은 parent 기준이다.</summary>
    public static GameObject Place(DressKind kind, Transform parent, Vector3 localPosition, float yaw, int seed) {
      var map = Dressing.Map;
      var entry = map != null ? map.Get(kind) : null;
      if (entry == null) {
        return null;
      }
      // 품질별 남김 비율(항목 25-1): 인스턴싱 개체와 같은 규칙을 실제 오브젝트 소품에도 적용한다(낮음·보통에서 렌더러 수를 줄인다).
      var keep = MeshKit.Hash(seed, 77);
      if ((GraphicsQuality.Current == QualityTier.Low && keep > entry.LowKeep) || (GraphicsQuality.Current == QualityTier.Medium && keep > entry.MediumKeep)) {
        return null;
      }
      var (variant, _, scale) = KenneyAssetMap.Place(entry, seed);
      var wrapper = new GameObject("Kenney").transform;
      wrapper.SetParent(parent, false);
      wrapper.localPosition = localPosition;
      wrapper.localRotation = Quaternion.Euler(0f, yaw, 0f);
      wrapper.localScale = Vector3.one * scale;
      var model = Object.Instantiate(variant.Prefab, wrapper);
      model.transform.localPosition += variant.Offset;
      foreach (var renderer in model.GetComponentsInChildren<Renderer>()) {
        renderer.sharedMaterials = KenneyLook.Resolve(renderer.sharedMaterials);
        renderer.shadowCastingMode = entry.Shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
      }
      return wrapper.gameObject;
    }

    static MaterialPropertyBlock _block;

    /// <summary>손상 단계의 그을음(공용 셰이더 _Soot). 0이면 블록을 지워 SRP Batcher로 돌아간다.</summary>
    public static void Soot(GameObject root, float amount) {
      _block ??= new MaterialPropertyBlock();
      foreach (var renderer in root.GetComponentsInChildren<Renderer>()) {
        if (amount <= 0f) {
          renderer.SetPropertyBlock(null);
          continue;
        }
        _block.Clear();
        _block.SetFloat("_Soot", amount);
        renderer.SetPropertyBlock(_block);
      }
    }
  }

  /// <summary>
  /// 모은 Kenney 개체를 그린다. 두 방식이다.
  /// - 읽기 가능한 메쉬(nature-kit): 그룹 × 120m 칸마다 한 메쉬로 합치고, 원래 머티리얼 색은 정점 색으로 굽는다. 칸 하나가 그리기 호출 하나다.
  ///   그룹(숲 하나, 지형, 경계)마다 따로 합치므로 숲이 벌목돼도 그 숲만 다시 합친다.
  /// - 나머지(색 아틀라스 키트): (메쉬, 머티리얼, 서브메쉬)별 Graphics.RenderMeshInstanced.
  /// 테스트가 코루틴에서 카메라를 직접 그리므로 LateUpdate가 아니라 Update에서 낸다.
  /// </summary>
  public class DressingRenderer : MonoBehaviour {
    public static DressingRenderer Active { get; private set; }

    const float CellSize = 120f;
    const int MaxPerBatch = 1000;

    struct Part {
      public Mesh Mesh;
      public Material[] Materials;
      public Matrix4x4 Local;
    }

    class Batch {
      public Mesh Mesh;
      public Material Material;
      public int Submesh;
      public bool Shadows;
      public Bounds Bounds;
      public readonly List<Matrix4x4> Matrices = new List<Matrix4x4>();
    }

    class Merged {
      public Mesh Mesh;
      public bool Shadows;
    }

    static readonly Dictionary<GameObject, Part[]> Parts = new Dictionary<GameObject, Part[]>();
    readonly Dictionary<string, List<Dressing.Item>> _groups = new Dictionary<string, List<Dressing.Item>>();
    readonly Dictionary<string, List<Merged>> _merged = new Dictionary<string, List<Merged>>();
    readonly HashSet<string> _stale = new HashSet<string>();
    readonly List<Batch> _batches = new List<Batch>();
    Material _vertexColor;
    bool _instancedDirty;

    public int InstanceCount { get; private set; }

    void Awake() {
      Active = this;
      // 프로필이 바뀌었을 수 있다(원본 ↔ 군용). 부품 캐시를 비운다.
      Parts.Clear();
    }

    void OnDestroy() {
      if (Active == this) {
        Active = null;
      }
      foreach (var list in _merged.Values) {
        foreach (var merged in list) {
          Destroy(merged.Mesh);
        }
      }
      if (_vertexColor != null) {
        Destroy(_vertexColor);
      }
    }

    public void Set(string group, List<Dressing.Item> items) {
      _groups[group] = items;
      _stale.Add(group);
      _instancedDirty = true;
    }

    public IEnumerable<string> GroupNames => _groups.Keys;

    public IReadOnlyList<Dressing.Item> Group(string group) => _groups.TryGetValue(group, out var list) ? list : System.Array.Empty<Dressing.Item>();

    /// <summary>품질이 바뀌면(낮음은 나무 수를 줄인다) 모두 다시 만든다.</summary>
    public void MarkDirty() {
      foreach (var group in _groups.Keys) {
        _stale.Add(group);
      }
      _instancedDirty = true;
    }

    static readonly Dictionary<Material, Material> InstancedCopies = new Dictionary<Material, Material>();

    /// <summary>인스턴싱이 켜진 머티리얼. 이미 켜져 있으면 그대로, 아니면 원본마다 복사본 하나를 만들어 같이 쓴다.</summary>
    static Material[] Instanced(Material[] sources) {
      var result = new Material[sources.Length];
      for (var i = 0; i < sources.Length; i++) {
        var source = sources[i];
        if (source == null || source.enableInstancing) {
          result[i] = source;
          continue;
        }
        if (InstancedCopies.TryGetValue(source, out var copy) == false || copy == null) {
          copy = new Material(source) { name = source.name + "_instanced", enableInstancing = true };
          InstancedCopies[source] = copy;
        }
        result[i] = copy;
      }
      return result;
    }

    static Part[] PartsOf(GameObject prefab) {
      if (Parts.TryGetValue(prefab, out var parts)) {
        return parts;
      }
      var list = new List<Part>();
      // 프리팹 에셋의 변환은 부모가 없으므로 localToWorld가 곧 루트 기준이다(루트 자신의 회전·배율 포함). 바닥 보정도 같은 기준으로 쟀다.
      foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true)) {
        var renderer = filter.GetComponent<MeshRenderer>();
        if (filter.sharedMesh == null || renderer == null) {
          continue;
        }
        var materials = KenneyLook.Resolve(renderer.sharedMaterials);
        // 읽을 수 없는 메쉬(구입한 팩)는 GPU 인스턴싱으로 그린다. 팩 머티리얼은 인스턴싱이 꺼져 있으므로 메모리 안에서 복사본을 만들어 쓴다(팩 파일은 그대로).
        if (filter.sharedMesh.isReadable == false) {
          materials = Instanced(materials);
        }
        list.Add(new Part { Mesh = filter.sharedMesh, Materials = materials, Local = filter.transform.localToWorldMatrix });
      }
      parts = list.ToArray();
      Parts[prefab] = parts;
      return parts;
    }

    static bool Skip(Dressing.Item item) => item.Prefab == null
      || (GraphicsQuality.Current == QualityTier.Low && (item.LowKeep <= 0f || item.Keep > item.LowKeep))
      || (GraphicsQuality.Current == QualityTier.Medium && (item.MediumKeep <= 0f || item.Keep > item.MediumKeep));

    /// <summary>합친 메쉬용 머티리얼: 공용 셰이더, 흰 바탕, 정점 색 사용. 다른 값은 원본 머티리얼에서 가져온다.</summary>
    Material VertexColorMaterial(Material source) {
      if (_vertexColor == null) {
        _vertexColor = new Material(source) { name = "KenneyMerged", enableInstancing = false };
        _vertexColor.SetColor("_BaseColor", Color.white);
        _vertexColor.SetTexture("_BaseMap", Texture2D.whiteTexture);
        _vertexColor.SetFloat("_UseVertexColor", 1f);
        _vertexColor.SetFloat("_Desaturate", 0f);
        _vertexColor.SetFloat("_Brightness", 1f);
        _vertexColor.SetColor("_Tint", Color.white);
      }
      return _vertexColor;
    }

    void MergeGroup(string group) {
      if (_merged.TryGetValue(group, out var old)) {
        foreach (var merged in old) {
          Destroy(merged.Mesh);
        }
      }
      var cells = new Dictionary<(int, int, bool), (List<Vector3> V, List<Vector3> N, List<Color> C, List<int> T)>();
      var linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
      foreach (var item in _groups[group]) {
        if (Skip(item)) {
          continue;
        }
        var position = item.World.GetColumn(3);
        var key = (Mathf.FloorToInt(position.x / CellSize), Mathf.FloorToInt(position.z / CellSize), item.Shadows);
        foreach (var part in PartsOf(item.Prefab)) {
          if (part.Mesh.isReadable == false) {
            continue;
          }
          if (cells.TryGetValue(key, out var cell) == false) {
            cell = (new List<Vector3>(), new List<Vector3>(), new List<Color>(), new List<int>());
            cells[key] = cell;
          }
          var world = item.World * part.Local;
          var normalMatrix = world.inverse.transpose;
          var vertices = part.Mesh.vertices;
          var normals = part.Mesh.normals;
          for (var s = 0; s < part.Mesh.subMeshCount && s < part.Materials.Length; s++) {
            var material = part.Materials[s];
            if (material == null) {
              continue;
            }
            VertexColorMaterial(material);
            var source = KenneyLook.Resolve(material);
            var color = (source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") : Color.white) * (item.Tint.a > 0f ? item.Tint : Color.white);
            if (linear) {
              color = color.linear;
            }
            var indices = part.Mesh.GetTriangles(s);
            var remap = new Dictionary<int, int>();
            foreach (var index in indices) {
              if (remap.TryGetValue(index, out var mapped) == false) {
                mapped = cell.V.Count;
                remap[index] = mapped;
                cell.V.Add(world.MultiplyPoint3x4(vertices[index]));
                cell.N.Add(normals.Length > index ? normalMatrix.MultiplyVector(normals[index]).normalized : Vector3.up);
                cell.C.Add(color);
              }
              cell.T.Add(mapped);
            }
          }
        }
      }
      var list = new List<Merged>();
      foreach (var pair in cells) {
        if (pair.Value.T.Count == 0) {
          continue;
        }
        var mesh = new Mesh { name = $"kenney_{group}", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.SetVertices(pair.Value.V);
        mesh.SetNormals(pair.Value.N);
        mesh.SetColors(pair.Value.C);
        mesh.SetTriangles(pair.Value.T, 0);
        mesh.RecalculateBounds();
        mesh.UploadMeshData(true);
        list.Add(new Merged { Mesh = mesh, Shadows = pair.Key.Item3 });
      }
      _merged[group] = list;
    }

    void RebuildInstanced() {
      _instancedDirty = false;
      _batches.Clear();
      InstanceCount = 0;
      var index = new Dictionary<(Mesh, Material, int, bool, int, int, int), Batch>();
      foreach (var group in _groups.Values) {
        foreach (var item in group) {
          if (Skip(item)) {
            continue;
          }
          InstanceCount++;
          foreach (var part in PartsOf(item.Prefab)) {
            if (part.Mesh.isReadable) {
              continue;
            }
            var world = item.World * part.Local;
            for (var s = 0; s < part.Mesh.subMeshCount && s < part.Materials.Length; s++) {
              var material = part.Materials[s];
              if (material == null) {
                continue;
              }
              var page = 0;
              Batch batch;
              // 칸(60m)마다 묶음을 나눈다: RenderMeshInstanced는 묶음 전체를 한 번에 그리므로, 맵 전체를 덮는 묶음은 카메라 밖 개체도 전부 그린다.
              var cellX = Mathf.FloorToInt(world.m03 / 60f);
              var cellZ = Mathf.FloorToInt(world.m23 / 60f);
              while (true) {
                var key = (part.Mesh, material, s, item.Shadows, page, cellX, cellZ);
                if (index.TryGetValue(key, out batch) == false) {
                  batch = new Batch { Mesh = part.Mesh, Material = material, Submesh = s, Shadows = item.Shadows, Bounds = new Bounds(world.MultiplyPoint3x4(part.Mesh.bounds.center), Vector3.zero) };
                  index[key] = batch;
                  _batches.Add(batch);
                  break;
                }
                if (batch.Matrices.Count < MaxPerBatch) {
                  break;
                }
                page++;
              }
              batch.Matrices.Add(world);
              var b = part.Mesh.bounds;
              for (var c = 0; c < 8; c++) {
                batch.Bounds.Encapsulate(world.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1))));
              }
            }
          }
        }
      }
    }

    void Refresh() {
      foreach (var group in _stale) {
        MergeGroup(group);
      }
      _stale.Clear();
      if (_instancedDirty) {
        RebuildInstanced();
      }
    }

    bool Casts(bool shadows) => shadows && GraphicsQuality.UnitShadows;

    void Update() {
      Refresh();
      foreach (var list in _merged.Values) {
        foreach (var merged in list) {
          var parameters = new RenderParams(_vertexColor) { worldBounds = merged.Mesh.bounds, shadowCastingMode = Casts(merged.Shadows) ? ShadowCastingMode.On : ShadowCastingMode.Off, receiveShadows = true, layer = gameObject.layer };
          Graphics.RenderMesh(parameters, merged.Mesh, 0, Matrix4x4.identity);
        }
      }
      foreach (var batch in _batches) {
        var parameters = new RenderParams(batch.Material) { worldBounds = batch.Bounds, shadowCastingMode = Casts(batch.Shadows) ? ShadowCastingMode.On : ShadowCastingMode.Off, receiveShadows = true, layer = gameObject.layer };
        Graphics.RenderMeshInstanced(parameters, batch.Mesh, batch.Submesh, batch.Matrices);
      }
    }

    /// <summary>카메라에 걸리는 묶음 수·삼각형 수·그림자 묶음 수. 비교 도구의 그리기 호출 추정에 더한다.</summary>
    public (int Batches, int Triangles, int Casters) Count(Plane[] planes) {
      Refresh();
      int batches = 0, triangles = 0, casters = 0;
      foreach (var list in _merged.Values) {
        foreach (var merged in list) {
          if (GeometryUtility.TestPlanesAABB(planes, merged.Mesh.bounds)) {
            batches++;
            triangles += (int)(merged.Mesh.GetIndexCount(0) / 3);
            casters += Casts(merged.Shadows) ? 1 : 0;
          }
        }
      }
      foreach (var batch in _batches) {
        if (GeometryUtility.TestPlanesAABB(planes, batch.Bounds) == false) {
          continue;
        }
        batches++;
        triangles += (int)(batch.Mesh.GetIndexCount(batch.Submesh) / 3) * batch.Matrices.Count;
        casters += Casts(batch.Shadows) ? 1 : 0;
      }
      return (batches, triangles, casters);
    }
  }
}
