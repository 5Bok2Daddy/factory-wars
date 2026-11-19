namespace FactoryWars.View {
  using Quantum;
  using UnityEngine;

  /// <summary>
  /// 전장 손상 지도(항목 27 K-2). 화면 전용이다. 맵(땅 + 외곽 수로)을 128×128 칸(4인 맵 약 2.8m)으로 나누고 칸마다 손상도 0~1을 쌓는다.
  /// - 원인: 범위 피해(AreaImpact), 유닛·건물 파괴(Wrecked), 도시 건물 손상·붕괴. 모두 synced 이벤트라 확정 프레임에서 한 번만 온다.
  ///   예측 프레임 이벤트는 쓰지 않으므로 롤백 때 지워져야 할 흔적이 남지 않는다.
  /// - 손상도는 줄지 않는다(누적, 상한 1). 시뮬레이션에는 아무것도 쓰지 않는다.
  /// - 텍스처 1장(R8)을 전역 셰이더 값 _ScarMap·_ScarRect로 내보낸다. 바닥·도로 머티리얼이 읽어 누렇게 → 흙·그을음 → 검게 탄 땅으로 바꾼다.
  /// </summary>
  public class BattleScars : MonoBehaviour {
    public static BattleScars Active { get; private set; }

    public const int Size = 128;
    static readonly int ScarMapId = Shader.PropertyToID("_ScarMap");
    static readonly int ScarRectId = Shader.PropertyToID("_ScarRect");

    readonly float[] _damage = new float[Size * Size];
    readonly byte[] _bytes = new byte[Size * Size];
    Texture2D _mask;
    Vector2 _min;
    float _span = 1f;
    bool _dirty;
    float _smokeAt;
    MapSpec _map;

    public BattleFx Fx { get; set; }
    /// <summary>바뀔 때마다 늘어난다. 숲처럼 손상에 따라 다시 만드는 쪽이 비교한다.</summary>
    public int Version { get; private set; }
    public float Average { get; private set; }
    public int Damaged { get; private set; }
    /// <summary>받아 쌓은 이벤트 수.</summary>
    public int Events { get; private set; }
    /// <summary>이 프레임 뒤의 이벤트는 무시한다(테스트: 두 경기를 같은 프레임에서 비교).</summary>
    public int LastTick { get; set; } = int.MaxValue;

    bool Take(EventBase e) {
      if (e.Tick > LastTick) {
        return false;
      }
      Events++;
      return true;
    }

    public void Init(MapSpec map) {
      Active = this;
      _map = map;
      var channel = TerrainKit.Channel(map) + 8f;
      _min = new Vector2(map.BoundsMin.X.AsFloat - channel, map.BoundsMin.Y.AsFloat - channel);
      _span = Mathf.Max(map.BoundsMax.X.AsFloat - map.BoundsMin.X.AsFloat, map.BoundsMax.Y.AsFloat - map.BoundsMin.Y.AsFloat) + channel * 2f;
      _mask = new Texture2D(Size, Size, TextureFormat.R8, false, true) { name = "BattleScars", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
      _mask.SetPixelData(_bytes, 0);
      _mask.Apply(false);
      Shader.SetGlobalTexture(ScarMapId, _mask);
      Shader.SetGlobalVector(ScarRectId, new Vector4(_min.x, _min.y, 1f / _span, 1f / _span));
      QuantumEvent.Subscribe<EventAreaImpact>(this, e => {
        if (Take(e) == false) {
          return;
        }
        var radius = e.Radius.AsFloat;
        Add(e.Position.ToUnityVector3(), Mathf.Max(3.5f, radius * 2f), 0.3f);
        // 중포격·전략 미사일(반경 3.5m 이상)은 경기 내내 영구 크레이터로 잔존.
        Fx?.Crater(e.Position.ToUnityVector3(), Mathf.Clamp(radius, 0.8f, 5.2f), permanent: radius >= 3.5f);
      });
      QuantumEvent.Subscribe<EventWrecked>(this, e => {
        if (Take(e) == false) {
          return;
        }
        // 유닛 사망: 소형 3m·0.22, 중형 4.5m·0.5, 대형 6m·0.7. 건물 파괴: 부지 반경의 2배·1.
        var radius = e.Size < 0 ? e.Footprint.AsFloat * 2f : e.Size >= (int)UnitSize.Large ? 6f : e.Size == (int)UnitSize.Medium ? 4.5f : 3f;
        Add(e.Position.ToUnityVector3(), radius, e.Size < 0 ? 1f : e.Size >= (int)UnitSize.Large ? 0.7f : e.Size == (int)UnitSize.Medium ? 0.5f : 0.22f);
      });
      QuantumEvent.Subscribe<EventCityBuildingDamaged>(this, e => {
        if (Take(e) && _map.CityBuildings != null && e.Index >= 0 && e.Index < _map.CityBuildings.Length) {
          var shape = _map.CityBuildings[e.Index].Shape;
          Add(shape.Center.ToUnityVector3(), Mathf.Max(shape.HalfLength.AsFloat, shape.HalfWidth.AsFloat) + 3f, 0.15f * Mathf.Max(1, e.Stage));
        }
      });
      QuantumEvent.Subscribe<EventCityBuildingCollapsed>(this, e => {
        if (Take(e)) {
          City(e.Index, 0.85f, 8f);
        }
      });
      QuantumEvent.Subscribe<EventCityBuildingToppled>(this, e => {
        if (Take(e)) {
          City(e.Index, 0.85f, 10f);
        }
      });
    }

    void City(int index, float amount, float spread) {
      if (_map.CityBuildings == null || index < 0 || index >= _map.CityBuildings.Length) {
        return;
      }
      var placement = _map.CityBuildings[index];
      var shape = placement.Shape;
      Add(shape.Center.ToUnityVector3(), Mathf.Max(shape.HalfLength.AsFloat, shape.HalfWidth.AsFloat) + spread, amount);
      if (placement.Topples) {
        Add(placement.FallZone.Center.ToUnityVector3(), Mathf.Max(placement.FallZone.HalfLength.AsFloat, placement.FallZone.HalfWidth.AsFloat) + 2f, amount);
      }
    }

    void OnDestroy() {
      QuantumEvent.UnsubscribeListener(this);
      if (Active == this) {
        Active = null;
        Shader.SetGlobalVector(ScarRectId, Vector4.zero);
      }
      if (_mask != null) {
        Destroy(_mask);
      }
    }

    /// <summary>원 안의 칸에 손상도를 더한다. 가운데가 가장 크고 가장자리로 줄어든다.</summary>
    public void Add(Vector3 position, float radius, float amount) {
      var cell = _span / Size;
      var cx = (position.x - _min.x) / cell;
      var cz = (position.z - _min.y) / cell;
      var r = Mathf.Max(0.6f, radius / cell);
      var x0 = Mathf.Max(0, Mathf.FloorToInt(cx - r));
      var x1 = Mathf.Min(Size - 1, Mathf.CeilToInt(cx + r));
      var z0 = Mathf.Max(0, Mathf.FloorToInt(cz - r));
      var z1 = Mathf.Min(Size - 1, Mathf.CeilToInt(cz + r));
      for (var z = z0; z <= z1; z++) {
        for (var x = x0; x <= x1; x++) {
          var d = new Vector2(x + 0.5f - cx, z + 0.5f - cz).magnitude / r;
          if (d >= 1f) {
            continue;
          }
          var i = z * Size + x;
          _damage[i] = Mathf.Min(1f, _damage[i] + amount * (1f - d * d));
        }
      }
      _dirty = true;
      Version++;
    }

    /// <summary>한 지점의 손상도(칸 사이 보간 없음, 0~1).</summary>
    public float Level(Vector3 position) {
      var cell = _span / Size;
      var x = Mathf.Clamp(Mathf.FloorToInt((position.x - _min.x) / cell), 0, Size - 1);
      var z = Mathf.Clamp(Mathf.FloorToInt((position.z - _min.y) / cell), 0, Size - 1);
      return _damage[z * Size + x];
    }

    /// <summary>원 안에서 가장 큰 손상도.</summary>
    public float Max(Vector3 position, float radius) {
      var best = 0f;
      for (var a = 0; a < 9; a++) {
        var offset = a == 0 ? Vector3.zero : Quaternion.Euler(0f, a * 45f, 0f) * Vector3.forward * radius * 0.7f;
        best = Mathf.Max(best, Level(position + offset));
      }
      return best;
    }

    void LateUpdate() {
      if (_dirty) {
        _dirty = false;
        var sum = 0f;
        var damaged = 0;
        // 텍스처는 3×3 흐림을 거쳐 칸 경계가 각지게 보이지 않게 한다. 판정용 값(_damage)은 그대로다.
        for (var z = 0; z < Size; z++) {
          for (var x = 0; x < Size; x++) {
            var i = z * Size + x;
            sum += _damage[i];
            damaged += _damage[i] > 0.05f ? 1 : 0;
            var blurred = 0f;
            var weight = 0f;
            for (var dz = -1; dz <= 1; dz++) {
              for (var dx = -1; dx <= 1; dx++) {
                var nx = x + dx;
                var nz = z + dz;
                if (nx < 0 || nz < 0 || nx >= Size || nz >= Size) {
                  continue;
                }
                var w = dx == 0 && dz == 0 ? 2f : 1f;
                blurred += _damage[nz * Size + nx] * w;
                weight += w;
              }
            }
            _bytes[i] = (byte)(Mathf.Clamp01(blurred / weight) * 255f);
          }
        }
        Average = sum / _damage.Length;
        Damaged = damaged;
        _mask.SetPixelData(_bytes, 0);
        _mask.Apply(false);
      }
      // 화면 분위기(약하게): 많이 부서진 칸에서 연기 기둥이 가끔 오른다. 평균 손상도가 클수록 자주. 밝은 하늘·조명은 그대로다.
      if (Fx != null && Damaged > 0 && Time.time >= _smokeAt) {
        _smokeAt = Time.time + Mathf.Lerp(2.5f, 0.6f, Mathf.Clamp01(Average * 12f)) / Mathf.Max(0.35f, GraphicsQuality.FxScale);
        for (var tries = 0; tries < 24; tries++) {
          var i = Random.Range(0, _damage.Length);
          if (_damage[i] > 0.7f) {
            var cell = _span / Size;
            Fx.Burning(new Vector3(_min.x + (i % Size + 0.5f) * cell, 0.5f, _min.y + (i / Size + 0.5f) * cell), false);
            break;
          }
        }
      }
    }

    /// <summary>가장 많이 부서진 곳들(5×5칸 합이 큰 순, 서로 separation 이상 떨어짐). 시안 촬영 위치를 고를 때 쓴다.</summary>
    public System.Collections.Generic.List<Vector3> Hotspots(int count, float separation) {
      var cell = _span / Size;
      var scored = new System.Collections.Generic.List<(float Score, Vector3 Position)>();
      for (var z = 2; z < Size - 2; z++) {
        for (var x = 2; x < Size - 2; x++) {
          var sum = 0f;
          for (var dz = -2; dz <= 2; dz++) {
            for (var dx = -2; dx <= 2; dx++) {
              sum += _damage[(z + dz) * Size + x + dx];
            }
          }
          if (sum > 0.5f) {
            scored.Add((sum, new Vector3(_min.x + (x + 0.5f) * cell, 0f, _min.y + (z + 0.5f) * cell)));
          }
        }
      }
      scored.Sort((a, b) => b.Score.CompareTo(a.Score));
      var picked = new System.Collections.Generic.List<Vector3>();
      foreach (var (_, position) in scored) {
        if (picked.TrueForAll(p => (p - position).magnitude >= separation)) {
          picked.Add(position);
          if (picked.Count >= count) {
            break;
          }
        }
      }
      return picked;
    }

    /// <summary>손상 지도 해시(테스트: 같은 리플레이면 같다).</summary>
    public int Hash() {
      var hash = 17;
      for (var i = 0; i < _damage.Length; i++) {
        hash = hash * 31 + (int)(_damage[i] * 1000f);
      }
      return hash;
    }
  }
}
