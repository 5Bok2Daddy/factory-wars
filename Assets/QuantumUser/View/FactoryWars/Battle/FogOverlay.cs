namespace FactoryWars.View {
  using Quantum;
  using Quantum.FactoryWars;
  using UnityEngine;
  using UnityEngine.Rendering;

  /// <summary>
  /// 항목 34-A 전장의 안개 화면. 내 시야(동맹 공유 포함) 격자를 96×96 텍스처 한 장으로 올려 땅 위에 반투명 검정 판으로 덮는다.
  /// 미탐색은 짙게, 탐색됨(지금은 안 보임)은 옅게, 보임은 투명. 텍스처를 쌍선형으로 늘려 가장자리가 부드럽다(GPU 블러 대신).
  /// 시뮬레이션 상태는 읽기만 한다. 안개가 꺼져 있으면 아무것도 만들지 않는다.
  /// </summary>
  public unsafe class FogOverlay {
    public const float UnexploredAlpha = 0.92f;
    public const float ExploredAlpha = 0.5f;
    const float Lift = 0.16f;

    readonly GameObject _root;
    readonly Texture2D _texture;
    readonly Color32[] _pixels = new Color32[FogSystem.Size * FogSystem.Size];
    int _revision = -1;
    int _viewer = -2;

    public GameObject Root => _root;
    public Texture2D Texture => _texture;
    public int Revision => _revision;

    public FogOverlay(Transform parent, Frame frame) {
      var fog = frame.Unsafe.GetPointerSingleton<Fog>();
      var cell = fog->Cell.AsFloat;
      var size = FogSystem.Size * cell;
      _root = GameObject.CreatePrimitive(PrimitiveType.Quad);
      _root.name = "Fog";
      Object.Destroy(_root.GetComponent<Collider>());
      _root.transform.SetParent(parent, false);
      // 쿼드는 XY 평면이라 눕힌다(위를 향하게). 칸 (0,0)이 (MinX, MinZ)다.
      _root.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
      _root.transform.localScale = new Vector3(size, size, 1f);
      _root.transform.localPosition = new Vector3(fog->MinX.AsFloat + size * 0.5f, Lift, fog->MinZ.AsFloat + size * 0.5f);
      _texture = new Texture2D(FogSystem.Size, FogSystem.Size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "fog" };
      var renderer = _root.GetComponent<MeshRenderer>();
      renderer.shadowCastingMode = ShadowCastingMode.Off;
      renderer.receiveShadows = false;
      var shader = Shader.Find("FactoryWars/Fog");
      var material = new Material(shader != null ? shader : Shader.Find("Universal Render Pipeline/Unlit")) { name = "FogOverlay" };
      material.SetTexture("_BaseMap", _texture);
      renderer.sharedMaterial = material;
    }

    /// <summary>안개 칸의 알파(시험·미니맵과 같은 규칙).</summary>
    public static byte AlphaOf(bool visible, bool explored) => visible ? (byte)0 : (byte)Mathf.RoundToInt((explored ? ExploredAlpha : UnexploredAlpha) * 255f);

    /// <summary>시뮬레이션이 안개를 다시 만들었거나 보는 사람이 바뀌었을 때만 텍스처를 다시 올린다.</summary>
    public void Refresh(Frame frame, PlayerRef viewer) {
      var fog = frame.Unsafe.GetPointerSingleton<Fog>();
      var index = viewer.IsValid ? (int)viewer : -1;
      if (fog->Revision == _revision && index == _viewer) {
        return;
      }
      _revision = fog->Revision;
      _viewer = index;
      var show = index >= 0 && index < 4;
      for (var z = 0; z < FogSystem.Size; z++) {
        for (var x = 0; x < FogSystem.Size; x++) {
          var cell = z * FogSystem.Size + x;
          var visible = show == false;
          var explored = show == false;
          if (show) {
            var grid = fog->Grids.GetPointer(index);
            visible = (grid->Visible[cell >> 3] & (1 << (cell & 7))) != 0;
            explored = (grid->Explored[cell >> 3] & (1 << (cell & 7))) != 0;
          }
          _pixels[cell] = new Color32(0, 0, 0, AlphaOf(visible, explored));
        }
      }
      _texture.SetPixels32(_pixels);
      _texture.Apply(false);
    }

    /// <summary>한 칸의 안개 알파(0~255). 보는 사람이 없으면 0.</summary>
    public static byte CellAlpha(Fog* fog, int viewer, int cell) {
      if (viewer < 0 || viewer >= 4) {
        return 0;
      }
      var grid = fog->Grids.GetPointer(viewer);
      return AlphaOf((grid->Visible[cell >> 3] & (1 << (cell & 7))) != 0, (grid->Explored[cell >> 3] & (1 << (cell & 7))) != 0);
    }

    public void Destroy() {
      if (_root != null) {
        Object.Destroy(_root);
      }
      if (_texture != null) {
        Object.Destroy(_texture);
      }
    }
  }
}
