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
      new Entry { Id = "MapDesert", Name = "사막 보급전", Blurb = "광산 40% · 보급 2배 빠르고 2배 보상 · 모래폭풍" },
      new Entry { Id = "MapUrban", Name = "시가전", Blurb = "고밀도 시가지 · 주둔 저격·엄폐·붕괴 통로" },
      new Entry { Id = "MapArchipelago", Name = "다도해", Blurb = "섬 거점 · 여울·교량·상륙·해무" },
      new Entry { Id = "MapStrait", Name = "해협", Blurb = "대형 운하·7분 강철 수문 · 함대·교량 결전" },
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
