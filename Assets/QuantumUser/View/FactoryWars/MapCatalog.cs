namespace FactoryWars.View {
  /// <summary>
  /// 항목 35 맵 풀. 씬 이름(= 맵 ID)·메뉴 표시·한 줄 특징. 새 맵은 여기와 FactoryWarsMaps(데이터)에만 더하면 된다.
  /// 지금은 표준과 사막 보급전 2종이다(시가전·다도해·해협은 아직 없음).
  /// </summary>
  public static class MapCatalog {
    public struct Entry {
      public string Id;
      public string Name;
      public string Blurb;
    }

    public static readonly Entry[] Maps = {
      new Entry { Id = "Map4P", Name = "표준", Blurb = "육해공 균형 · 4인 맵" },
      new Entry { Id = "MapDesert", Name = "사막 보급전", Blurb = "광산 40% · 보급 2배 빠르고 2배 보상" },
    };

    public static int IndexOf(string id) {
      for (var i = 0; i < Maps.Length; i++) {
        if (Maps[i].Id == id) {
          return i;
        }
      }
      return 0;
    }

    public static Entry Next(string id) => Maps[(IndexOf(id) + 1) % Maps.Length];
  }
}
