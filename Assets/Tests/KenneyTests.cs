namespace FactoryWars.Tests {
  using FactoryWars.View;
  using NUnit.Framework;
  using UnityEngine;
  using Assert = NUnit.Framework.Assert;

  /// <summary>항목 19B: 공용 셰이더와 Kenney 배치 규칙.</summary>
  public class KenneyTests {
    [Test]
    public void SharedShaderCompiles() {
      var shader = Shader.Find("FactoryWars/KenneyLit");
      Assert.IsNotNull(shader, "공용 셰이더가 빌드에 포함됨");
      Assert.IsTrue(shader.isSupported, "공용 셰이더가 이 그래픽 API에서 동작");
      var material = new Material(shader) { enableInstancing = true };
      Assert.IsTrue(material.HasProperty("_Tint"));
      Object.DestroyImmediate(material);
    }

    [Test]
    public void PlacementIsStableAndWeighted() {
      var entry = new KenneyAssetMap.Entry { Scale = 2f, ScaleJitter = new Vector2(1f, 1f), Yaw = new Vector2(0f, 360f), SnapYaw90 = true };
      entry.Variants.Add(new KenneyAssetMap.Variant { Weight = 3f });
      entry.Variants.Add(new KenneyAssetMap.Variant { Weight = 1f });
      var seed = KenneyAssetMap.Seed(new Vector3(12.3f, 0f, -4.5f), 7);
      var a = KenneyAssetMap.Place(entry, seed);
      var b = KenneyAssetMap.Place(entry, seed);
      Assert.AreSame(a.Variant, b.Variant, "같은 자리 같은 변종");
      Assert.AreEqual(a.Rotation, b.Rotation);
      Assert.AreEqual(2f, a.Scale, 1e-4f);
      Assert.AreEqual(0f, Mathf.Repeat(a.Rotation.eulerAngles.y + 0.01f, 90f), 0.1f, "90도 단위");
      var first = 0;
      for (var i = 0; i < 400; i++) {
        if (KenneyAssetMap.Place(entry, KenneyAssetMap.Seed(new Vector3(i * 3.1f, 0f, i * 1.7f), 1)).Variant == entry.Variants[0]) {
          first++;
        }
      }
      Assert.That(first, Is.InRange(250, 350), "가중치 3:1");
    }
  }
}
