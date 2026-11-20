namespace FactoryWars.View {
  using UnityEngine;

  /// <summary>이송 거리만큼 표면 UV를 움직인다. 컨베이어 한 줄당 메쉬 하나를 재사용한다.</summary>
  public sealed class ConveyorBeltSurface : MonoBehaviour {
    const float SlatPitch = 0.48f;
    static Material _material;
    Mesh _mesh;
    Vector2[] _uv;
    float _cyclesPerSegment;

    public float Phase { get; private set; }

    public static ConveyorBeltSurface Create(Transform parent, Vector3[] points, float width, float height, float length) {
      if (_material == null) {
        var shader = Resources.Load<Shader>("FactoryWars/Shaders/FactoryWarsConveyorBelt");
        if (shader == null) return null;
        _material = new Material(shader) { name = "Extractor Conveyor Belt", hideFlags = HideFlags.HideAndDontSave };
      }
      var surface = new GameObject("MovingBelt", typeof(MeshFilter), typeof(MeshRenderer), typeof(ConveyorBeltSurface))
        .GetComponent<ConveyorBeltSurface>();
      surface.transform.SetParent(parent, false);
      surface.Build(points, width, height, length);
      return surface;
    }

    void Build(Vector3[] points, float width, float height, float length) {
      var side = Vector3.Cross(Vector3.up, points[points.Length - 1] - points[0]).normalized * (width * 0.5f);
      var vertices = new Vector3[points.Length * 2];
      var indices = new int[(points.Length - 1) * 6];
      _uv = new Vector2[vertices.Length];
      _cyclesPerSegment = length / (SlatPitch * (points.Length - 1));
      for (var i = 0; i < points.Length; i++) {
        var center = points[i] + Vector3.up * height;
        var vertex = i * 2;
        vertices[vertex] = transform.InverseTransformPoint(center - side);
        vertices[vertex + 1] = transform.InverseTransformPoint(center + side);
        if (i == points.Length - 1) continue;
        var index = i * 6;
        indices[index] = vertex;
        indices[index + 1] = vertex + 2;
        indices[index + 2] = vertex + 1;
        indices[index + 3] = vertex + 1;
        indices[index + 4] = vertex + 2;
        indices[index + 5] = vertex + 3;
      }
      _mesh = new Mesh { name = "Extractor Conveyor Surface" };
      _mesh.MarkDynamic();
      _mesh.vertices = vertices;
      _mesh.triangles = indices;
      _mesh.RecalculateBounds();
      WriteUVs();
      GetComponent<MeshFilter>().sharedMesh = _mesh;
      var renderer = GetComponent<MeshRenderer>();
      renderer.sharedMaterial = _material;
      renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    public void Advance(float distance) {
      if (distance <= 0f) return;
      Phase = Mathf.Repeat(Phase + distance / SlatPitch, 1f);
      WriteUVs();
    }

    void WriteUVs() {
      for (var i = 0; i < _uv.Length; i += 2) {
        var along = i / 2 * _cyclesPerSegment - Phase;
        _uv[i] = new Vector2(0f, along);
        _uv[i + 1] = new Vector2(1f, along);
      }
      _mesh.SetUVs(0, _uv);
    }

    void OnDestroy() {
      if (_mesh != null) Destroy(_mesh);
    }
  }
}
