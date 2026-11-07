namespace FactoryWars.View {
  using UnityEngine;
  using UnityEngine.Rendering;

  /// <summary>
  /// 화면에 놓인 유닛·건물 하나. 위치는 시뮬레이션 좌표이고 크기는 바꾸지 않는다.
  /// 표시 배율은 모델에만 적용하고, 선택 링과 체력바는 모델의 형제라서 배율을 물려받지 않는다.
  /// 회전축을 모델 가운데에 맞춰서 어느 방향을 봐도 링 안에 있다.
  /// </summary>
  public class ArtView : MonoBehaviour {
    const float RingWidth = 0.14f;
    const float RingLift = 0.06f;
    const float BarThickness = 0.26f;
    const float BarMinWidth = 1.8f;
    const float BarMaxWidth = 5f;
    const float BarPerRadius = 1.3f;
    const float BarPitch = 50f;

    static Mesh _ring;
    static Mesh _barBack;
    static Mesh _barFill;

    public ViewCatalog.Entry Entry;
    public Transform Turn;
    public GameObject Model;
    public GameObject Ring;
    public GameObject Bar;
    public Transform Fill;

    public static ArtView Create(ViewCatalog catalog, string id, Transform parent, int color, bool powered = true) {
      if (catalog.TryGet(id, out var entry) == false) {
        return null;
      }
      var view = new GameObject(id).AddComponent<ArtView>();
      view.transform.SetParent(parent, false);
      view.Entry = entry;

      view.Turn = new GameObject("Turn").transform;
      view.Turn.SetParent(view.transform, false);
      view.Turn.localPosition = Vector3.up * entry.Altitude;

      view.Model = Instantiate(entry.Prefab, view.Turn);
      view.Model.name = "Model";
      view.Model.transform.localScale = Vector3.one * entry.DisplayScale;
      view.Model.transform.localPosition = new Vector3(-entry.Center.x, 0, -entry.Center.y) * entry.DisplayScale;
      ArtLook.Apply(view.Model, color, powered);

      view.Ring = Marker("Ring", view.transform, RingMesh());
      view.Ring.transform.localPosition = Vector3.up * RingLift;
      view.Ring.transform.localScale = new Vector3(entry.RingRadius, 1, entry.RingRadius);
      view.Ring.SetActive(false);

      var width = Mathf.Clamp(entry.RingRadius * BarPerRadius, BarMinWidth, BarMaxWidth);
      view.Bar = Marker("Bar", view.transform, BarBackMesh());
      view.Bar.transform.localPosition = Vector3.up * entry.BarHeight;
      view.Bar.transform.localRotation = Quaternion.Euler(BarPitch, 0, 0);
      view.Bar.transform.localScale = new Vector3(width, BarThickness, 1);
      view.Fill = Marker("Fill", view.Bar.transform, BarFillMesh()).transform;
      view.Bar.SetActive(false);

      view.ApplyQuality();
      return view;
    }

    static GameObject Marker(string name, Transform parent, Mesh mesh) {
      var item = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
      item.transform.SetParent(parent, false);
      item.GetComponent<MeshFilter>().sharedMesh = mesh;
      var renderer = item.GetComponent<MeshRenderer>();
      renderer.sharedMaterial = ArtLook.NeutralMaterial;
      renderer.shadowCastingMode = ShadowCastingMode.Off;
      renderer.receiveShadows = false;
      return item;
    }

    static Mesh RingMesh() {
      if (_ring == null) {
        var kit = new MeshKit().Paint(Swatch.White);
        kit.Annulus(Vector3.zero, 1f - RingWidth, 1f, 32);
        _ring = kit.ToMesh("marker_ring");
      }
      return _ring;
    }

    static Mesh BarBackMesh() {
      if (_barBack == null) {
        var kit = new MeshKit().Paint(Swatch.Black);
        kit.Quad(new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0), Vector3.back);
        _barBack = kit.ToMesh("marker_bar_back");
      }
      return _barBack;
    }

    static Mesh BarFillMesh() {
      if (_barFill == null) {
        var kit = new MeshKit().Paint(Swatch.LeafLight);
        const float Inset = 0.04f;
        const float Top = 0.34f;
        var left = -0.5f + Inset;
        var right = 0.5f - Inset;
        kit.Quad(new Vector3(left, -Top, -0.01f), new Vector3(right, -Top, -0.01f), new Vector3(right, Top, -0.01f), new Vector3(left, Top, -0.01f), Vector3.back);
        _barFill = kit.ToMesh("marker_bar_fill");
      }
      return _barFill;
    }

    public void SetYaw(float degrees) {
      Turn.localRotation = Quaternion.Euler(0, degrees, 0);
    }

    GameObject _hidden;
    static Mesh _hiddenRing;

    /// <summary>숨은 상태 표시. 선택 고리 바깥의 점선 고리다.</summary>
    public void SetHidden(bool hidden) {
      if (hidden && _hidden == null) {
        _hidden = Marker("Hidden", transform, HiddenMesh());
        _hidden.transform.localPosition = Vector3.up * RingLift;
        _hidden.transform.localScale = new Vector3(Entry.RingRadius * 1.25f, 1, Entry.RingRadius * 1.25f);
      }
      if (_hidden != null && _hidden.activeSelf != hidden) {
        _hidden.SetActive(hidden);
      }
    }

    public bool IsHiddenShown => _hidden != null && _hidden.activeSelf;

    static Mesh HiddenMesh() {
      if (_hiddenRing == null) {
        var kit = new MeshKit().Paint(Swatch.White);
        const int Dashes = 12;
        for (var i = 0; i < Dashes; i++) {
          var a = Mathf.PI * 2f * i / Dashes;
          var b = a + Mathf.PI / Dashes;
          Vector3 P(float angle, float r) => new Vector3(Mathf.Sin(angle) * r, 0, Mathf.Cos(angle) * r);
          kit.Quad(P(a, 1f - RingWidth), P(a, 1f), P(b, 1f), P(b, 1f - RingWidth), Vector3.up);
        }
        _hiddenRing = kit.ToMesh("marker_hidden");
      }
      return _hiddenRing;
    }

    public void SetSelected(bool selected) {
      Ring.SetActive(selected);
      Bar.SetActive(selected);
    }

    /// <summary>0~1. 왼쪽을 기준으로 줄어든다.</summary>
    public void SetHealth(float ratio) {
      var value = Mathf.Clamp01(ratio);
      Fill.localScale = new Vector3(value, 1, 1);
      Fill.localPosition = new Vector3((value - 1f) * 0.5f, 0, 0);
    }

    public void ApplyQuality() {
      if (Entry.Unit == false) {
        return;
      }
      var mode = GraphicsQuality.UnitShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
      foreach (var renderer in Model.GetComponentsInChildren<Renderer>(true)) {
        renderer.shadowCastingMode = mode;
      }
    }

    public static void RefreshAll() {
      foreach (var view in FindObjectsByType<ArtView>(FindObjectsInactive.Include, FindObjectsSortMode.None)) {
        view.ApplyQuality();
      }
    }
  }
}
