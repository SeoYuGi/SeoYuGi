using System;
using System.Collections.Generic;

namespace SeoYuGi.Battle
{
    /// <summary>BattleMaps 레이아웃 1장의 파싱 결과.</summary>
    public class ParsedMap
    {
        public string Name;
        public int Width;
        public int Height;
        public List<Coord> Walls = new List<Coord>();
        public List<Coord> Highlands = new List<Coord>();
        public List<Coord> Voids = new List<Coord>();
        public List<Coord> HealPacks = new List<Coord>();
        /// <summary>거점 A, B, C 순 (좌→우) — 거점당 칸 목록 (3×3~5×5 직사각 패치).</summary>
        public List<List<Coord>> Zones = new List<List<Coord>>();
        /// <summary>스폰 마커 1..6 → 좌표 (1~3 = 팀0, 4~6 = 팀1).</summary>
        public Dictionary<int, Coord> Spawns = new Dictionary<int, Coord>();
    }

    /// <summary>
    /// 고정 맵 6장 (탱고파이브식 수제 구성 — 시드 무작위 대신 고정 로테이션).
    /// 맵이 고정이라 플레이어도, 예측 AI도 지형 위 습관을 제대로 학습한다.
    ///
    /// 문자: '.' 바닥, '#' 벽(크레이트), '^' 고지대(진입 2·시야 +1·저격 벽 관통·항상 노출),
    /// '_' 구덩이(이동 불가·시야/저격 통과·타일 없음 — ㄱ자/도넛 실루엣용),
    /// 'H' 힐팩 스폰(바닥 취급 — 밟으면 회복, 리스폰은 PickupSystem),
    /// A~C 거점 패치, 1-3 팀0 스폰(아래), 4-6 팀1 스폰(위).
    /// 맵 리마스터: 맵마다 "전술 문법" 자체가 다르다 — 크기(9×9 난투장 ~ 41×21 한강 둔치)와
    /// 거점 수(1 또는 3, 항상 홀수 — 동점 없음)가 가변. 어떤 맵을 픽하느냐 = 어떤 게임을 하느냐.
    /// 공통 규칙: 홀수 W×H · 거점 3×3~5×5 가변(중앙 거점은 홀수 크기) · 가운데 거점은 맵 정중앙 ·
    /// 지형·거점 180° 점대칭(양팀 공정) · 첫 행이 맵의 위.
    /// </summary>
    public static class BattleMaps
    {
        static readonly (string name, string[] rows)[] Layouts =
        {
            ("막다른 골목", new[] // 9×9 팔각 투기장 · 거점 1 — 숨을 데 없는 난투, 첫 점거가 곧 라운드
            {
                "__4.5.6__",
                "_......._",
                ".#..#..#.",
                "...AAA...",
                ".H.AAA.H.",
                "...AAA...",
                ".#..#..#.",
                "_......._",
                "__1.2.3__",
            }),
            ("옥상 종주", new[] // 17×15 옥상 중정 — A/C는 고지대 링에 둘러싸인 중정, B 골짜기는 담벼락 캣워크가 내려다본다
            {
                "....4..5..6..____",
                "...............__",
                ".^^^^^_.H._.##...",
                ".^AAA^_..._......",
                ".^AAA^_..._......",
                ".^AAA^#^^^#......",
                ".^^^^^.BBB.......",
                ".....#.BBB.#.....",
                ".......BBB.^^^^^.",
                "......#^^^#^CCC^.",
                "......_..._^CCC^.",
                "......_..._^CCC^.",
                "...##._.H._^^^^^.",
                "__...............",
                "____..1..2..3....",
            }),
            ("뒷골목 미로", new[] // 21×13 근접 난전 — 시선이 다 끊겨 어쌔신·탱커의 맵
            {
                "....4.....5.....6....",
                "..#...#.......#...#..",
                "....#...##.##...#....",
                ".##..#....#....#..##.",
                "..H.....BBBBB........",
                ".AAA.#..BBBBB..#.CCC.",
                ".AAA..#.BBBBB.#..CCC.",
                ".AAA.#..BBBBB..#.CCC.",
                "........BBBBB.....H..",
                ".##..#....#....#..##.",
                "....#...##.##...#....",
                "..#...#.......#...#..",
                "....1.....2.....3....",
            }),
            ("공사장 섬", new[] // 15×13 도넛 — B 섬 주위 구덩이 해자 + 모서리 컷·벽 보강. 구덩이 너머 저격 가능
            {
                "_..4...5...6.._",
                "...............",
                ".##....#....##.",
                "...^.......#...",
                ".....__H__CCCC.",
                ".AAAA_BBB_CCCC.",
                ".AAAA.BBB.CCCC.",
                ".AAAA_BBB_CCCC.",
                ".AAAA__H__.....",
                "...#.......^...",
                ".##....#....##.",
                "...............",
                "_..1...2...3.._",
            }),
            ("청계 물류단지", new[] // 29×15 초대형 평행사변형 — 컨테이너 레인 + 하역장 고지대 구역, 로테이션 싸움
            {
                "_____.4.......5.......6......",
                "___..........................",
                "........####..^..####........",
                "...AAA........^..............",
                "...AAA.....#.#.#.#......#.^^.",
                "...AAA.#.H..BBBBB..H.#..#.^^.",
                ".......#....BBBBB....#....^^.",
                ".......#..##BBBBB##..#.......",
                ".^^....#....BBBBB....#.......",
                ".^^.#..#.H..BBBBB..H.#.CCC...",
                ".^^.#......#.#.#.#.....CCC...",
                "..............^........CCC...",
                "........####..^..####........",
                "..........................___",
                "......1.......2.......3._____",
            }),
            ("한강 둔치", new[] // 41×21 초대형 — 강이 맵을 관통, 도하는 다리 2개 + 중앙 섬(B) 징검다리뿐
            {
                "..............4.....5.....6..............",
                ".........................................",
                "....##........#...........#........##....",
                "..........#...................#..........",
                ".....AAAA.......#.......#................",
                ".....AAAA...^^.............^^............",
                ".....AAAA...^^.............^^............",
                "....#AAAA.....H...........H.........#....",
                "________..__________.__________..________",
                "________..________.BBB.________..________",
                "________..________.BBB.________..________",
                "________..________.BBB.________..________",
                "________..__________.__________..________",
                "....#.........H...........H.....CCCC#....",
                "............^^.............^^...CCCC.....",
                "............^^.............^^...CCCC.....",
                "................#.......#.......CCCC.....",
                "..........#...................#..........",
                "....##........#...........#........##....",
                ".........................................",
                "..............1.....2.....3..............",
            }),
        };

        public static int Count => Layouts.Length;

        /// <summary>index는 맵 수로 래핑 — 아무 정수나 안전.</summary>
        public static ParsedMap Get(int index)
        {
            var (name, rows) = Layouts[((index % Count) + Count) % Count];
            return Parse(name, rows);
        }

        static ParsedMap Parse(string name, string[] rows)
        {
            var map = new ParsedMap
            {
                Name = name,
                Height = rows.Length,
                Width = rows[0].Length
            };
            var zoneByLetter = new SortedDictionary<char, List<Coord>>();

            for (int r = 0; r < rows.Length; r++)
            {
                if (rows[r].Length != map.Width)
                    throw new FormatException($"map '{name}' row {r}: width mismatch ({rows[r].Length} != {map.Width})");
                int y = map.Height - 1 - r; // 첫 행 = 맵의 위
                for (int x = 0; x < map.Width; x++)
                {
                    char ch = rows[r][x];
                    var c = new Coord(x, y);
                    if (ch == '#') map.Walls.Add(c);
                    else if (ch == '^') map.Highlands.Add(c);
                    else if (ch == '_') map.Voids.Add(c);
                    else if (ch == 'H') map.HealPacks.Add(c); // 지형은 바닥
                    else if (ch >= 'A' && ch <= 'C')
                    {
                        if (!zoneByLetter.TryGetValue(ch, out var cells))
                            zoneByLetter[ch] = cells = new List<Coord>();
                        cells.Add(c);
                    }
                    else if (ch >= '1' && ch <= '6') map.Spawns[ch - '0'] = c;
                    else if (ch != '.')
                        throw new FormatException($"map '{name}' ({x},{y}): unknown char '{ch}'");
                }
            }

            foreach (var kv in zoneByLetter) // A → B → C
                map.Zones.Add(kv.Value);
            return map;
        }
    }
}
