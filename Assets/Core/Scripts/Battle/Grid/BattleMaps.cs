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
        /// <summary>거점 A, B, C 순 (좌→우) — 거점당 칸 목록 (3×3 패치).</summary>
        public List<List<Coord>> Zones = new List<List<Coord>>();
        /// <summary>스폰 마커 1..6 → 좌표 (1~3 = 팀0, 4~6 = 팀1).</summary>
        public Dictionary<int, Coord> Spawns = new Dictionary<int, Coord>();
    }

    /// <summary>
    /// 고정 맵 5장 (탱고파이브식 수제 구성 — 시드 무작위 대신 고정 로테이션).
    /// 맵이 고정이라 플레이어도, 예측 AI도 지형 위 습관을 제대로 학습한다.
    ///
    /// 문자: '.' 바닥, '#' 벽(크레이트), A/B/C 거점 패치, 1-3 팀0 스폰(아래), 4-6 팀1 스폰(위).
    /// 모든 맵 21×13, 거점 3×3 — 벽은 180° 점대칭(양팀 공정). 첫 행이 맵의 위(y = Height-1).
    /// </summary>
    public static class BattleMaps
    {
        static readonly (string name, string[] rows)[] Layouts =
        {
            ("중앙 창고", new[]
            {
                "....4.....5.....6....",
                ".....................",
                "..##.....###.....##..",
                "......#.......#......",
                "....#....###....#....",
                ".AAA.....BBB.....CCC.",
                ".AAA..#..BBB..#..CCC.",
                ".AAA.....BBB.....CCC.",
                "....#....###....#....",
                "......#.......#......",
                "..##.....###.....##..",
                ".....................",
                "....1.....2.....3....",
            }),
            ("골목 시장", new[]
            {
                "....4.....5.....6....",
                ".....................",
                ".#####.........#####.",
                ".....................",
                "......###...###......",
                ".AAA.....BBB.....CCC.",
                ".AAA.#...BBB...#.CCC.",
                ".AAA.....BBB.....CCC.",
                "......###...###......",
                ".....................",
                ".#####.........#####.",
                ".....................",
                "....1.....2.....3....",
            }),
            ("십자로", new[]
            {
                "....4.....5.....6....",
                "...#.....#.....#.....",
                ".....................",
                ".#...#....#....#...#.",
                ".....................",
                ".AAA.....BBB.....CCC.",
                ".AAA.#...BBB...#.CCC.",
                ".AAA.....BBB.....CCC.",
                ".....................",
                ".#...#....#....#...#.",
                ".....................",
                ".....#.....#.....#...",
                "....1.....2.....3....",
            }),
            ("엄폐 광장", new[]
            {
                "....4.....5.....6....",
                "..#.......#.......#..",
                ".....##.......##.....",
                "...#.....#.#.....#...",
                ".....................",
                ".AAA.....BBB.....CCC.",
                ".AAA..#..BBB..#..CCC.",
                ".AAA.....BBB.....CCC.",
                ".....................",
                "...#.....#.#.....#...",
                ".....##.......##.....",
                "..#.......#.......#..",
                "....1.....2.....3....",
            }),
            ("대각 창고", new[]
            {
                "....4.....5.....6....",
                ".###.................",
                "......##......#......",
                "...#........##.......",
                ".....................",
                ".AAA.....BBB.....CCC.",
                ".AAA...#.BBB.#...CCC.",
                ".AAA.....BBB.....CCC.",
                ".....................",
                ".......##........#...",
                "......#......##......",
                ".................###.",
                "....1.....2.....3....",
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
