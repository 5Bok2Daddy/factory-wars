namespace FactoryWars.EditorTools {
  using System.Collections.Generic;
  using System.Globalization;
  using System.IO;
  using System.Linq;
  using System.Text.RegularExpressions;
  using UnityEditor;
  using UnityEngine;

  /// <summary>
  /// Art/Icons/src의 SVG를 PNG 스프라이트로 바꾼다. 외부 도구나 패키지 없이 돌도록 polygon, circle, rect만 읽는다.
  /// </summary>
  public static class IconBaker {
    public const string SourceFolder = "Assets/Art/Icons/src";
    const string OutputFolder = "Assets/QuantumUser/Resources/FactoryWars/Icons";
    const int Size = 96;
    const int Samples = 3;
    const float ViewBox = 64f;

    static readonly Regex ShapePattern = new Regex("<(polygon|circle|rect)\\s+([^>]*)/>");
    static readonly Regex AttributePattern = new Regex("([a-z]+)=\"([^\"]*)\"");

    class Shape {
      public Color Fill;
      public System.Func<float, float, bool> Contains;
    }

    static float Number(string text) => float.Parse(text, CultureInfo.InvariantCulture);

    static bool InPolygon(Vector2[] points, float x, float y) {
      var inside = false;
      for (int i = 0, j = points.Length - 1; i < points.Length; j = i++) {
        var a = points[i];
        var b = points[j];
        if ((a.y > y) != (b.y > y) && x < (b.x - a.x) * (y - a.y) / (b.y - a.y) + a.x) {
          inside = inside == false;
        }
      }
      return inside;
    }

    static List<Shape> Parse(string svg) {
      var shapes = new List<Shape>();
      foreach (Match match in ShapePattern.Matches(svg)) {
        var values = AttributePattern.Matches(match.Groups[2].Value).Cast<Match>().ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
        ColorUtility.TryParseHtmlString(values["fill"], out var fill);
        var shape = new Shape { Fill = fill };
        switch (match.Groups[1].Value) {
          case "polygon":
            var points = values["points"].Split(' ', System.StringSplitOptions.RemoveEmptyEntries)
              .Select(pair => pair.Split(',')).Select(pair => new Vector2(Number(pair[0]), Number(pair[1]))).ToArray();
            shape.Contains = (x, y) => InPolygon(points, x, y);
            break;
          case "circle":
            var center = new Vector2(Number(values["cx"]), Number(values["cy"]));
            var radius = Number(values["r"]);
            shape.Contains = (x, y) => (new Vector2(x, y) - center).sqrMagnitude <= radius * radius;
            break;
          default:
            var box = new Rect(Number(values["x"]), Number(values["y"]), Number(values["width"]), Number(values["height"]));
            shape.Contains = (x, y) => box.Contains(new Vector2(x, y));
            break;
        }
        shapes.Add(shape);
      }
      return shapes;
    }

    /// <summary>뒤의 도형부터 덮어 칠하고, 픽셀 안의 표본 수로 가장자리를 부드럽게 한다.</summary>
    static Color32[] Rasterize(List<Shape> shapes) {
      var pixels = new Color32[Size * Size];
      for (var py = 0; py < Size; py++) {
        for (var px = 0; px < Size; px++) {
          var sum = Color.clear;
          for (var sy = 0; sy < Samples; sy++) {
            for (var sx = 0; sx < Samples; sx++) {
              var x = (px + (sx + 0.5f) / Samples) / Size * ViewBox;
              var y = (py + (sy + 0.5f) / Samples) / Size * ViewBox;
              for (var i = shapes.Count - 1; i >= 0; i--) {
                if (shapes[i].Contains(x, y)) {
                  sum += shapes[i].Fill;
                  break;
                }
              }
            }
          }
          var count = Samples * Samples;
          var color = sum.a > 0f ? new Color(sum.r / sum.a, sum.g / sum.a, sum.b / sum.a, sum.a / count) : Color.clear;
          // SVG는 위가 0이고 텍스처는 아래가 0이다.
          pixels[(Size - 1 - py) * Size + px] = color;
        }
      }
      return pixels;
    }

    [MenuItem("Tools/Factory Wars/Bake Icons")]
    public static void BakeAll() {
      Directory.CreateDirectory(OutputFolder);
      var count = 0;
      foreach (var file in Directory.GetFiles(SourceFolder, "*.svg")) {
        var name = Path.GetFileNameWithoutExtension(file);
        if (name == "cf_flag_emblem") {
          continue;
        }
        var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
        texture.SetPixels32(Rasterize(Parse(File.ReadAllText(file))));
        texture.Apply();
        var path = $"{OutputFolder}/{name}.png";
        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.textureCompression = TextureImporterCompression.Compressed;
        importer.SaveAndReimport();
        count++;
      }
      Debug.Log($"IconBaker: 아이콘 {count}개");
    }
  }
}
