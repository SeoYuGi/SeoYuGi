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
        /// <summary>거점 A, B, C 순 (좌→우) — 거점당 칸 목록 (3×3 패치).</summary>
        public List<List<Coord>> Zones = new List<List<Coord>>();
        /// <summary>스폰 마커 1..6 → 좌표 (1~3 = 팀0, 4~6 = 팀1).</summary>
        public Dictionary<int, Coord> Spawns = new Dictionary<int, Coord>();
    }

    /// <summary>
    /// 고정 맵 5장 (탱고파이브식 수제 구성 — 시드 무작위 대신 고정 로테이션).
    /// 맵이 고정이라 플레이어도, 예측 AI도 지형 위 습관을 제대로 학습한다.
    ///
    /// 문자: '.' 바닥, '#' 벽(크레이트), '^' 고지대(진입 2·시야 +1·저격 벽 관통·항상 노출),
    /// A/B/C 거점 패치, 1-3 팀0 스폰(아래), 4-6 팀1 스폰(위).
    /// 맵마다 크기·거점 배치가 다르다(2026 서울 뒷골목 로케이션 5곳) — 단, 규칙은 공통:
    /// 홀수 W×H · 거점 3×3 · B는 맵 정중앙 · 지형·거점 180° 점대칭(양팀 공정) · 첫 행이 맵의 위.
    /// </summary>
    public static class BattleMaps
    {
        static readonly (string name, string[] rows)[] Layouts =
        {
            ("시장 골목", new[] // 15×11 밀집 난전 — 거점이 계단식(A 아래 · C 위), 짧은 로테이션
            {
                "...4...5...6...",
                "...............",
                ".....##....CCC.",
                ".#.........CCC.",
                "......BBB..CCC.",
                "..#...BBB...#..",
                ".AAA..BBB......",
                ".AAA.........#.",
                ".AAA....##.....",
                "...............",
                "...1...2...3...",
            }),
            ("옥상 정원", new[] // 17×13 고지대 4개 산개 — 옥상을 잡는 팀이 정보를 잡는다
            {
                "....4...5...6....",
                ".................",
                "..AAA......^.....",
                "..AAA............",
                "..AAA..##........",
                ".......BBB...##..",
                "..^....BBB....^..",
                "..##...BBB.......",
                "........##..CCC..",
                "............CCC..",
                ".....^......CCC..",
                ".................",
                "....1...2...3....",
            }),
            ("뒷골목 미로", new[] // 21×13 근접 난전 — 시선이 다 끊겨 어쌔신·탱커의 맵
            {
                "....4.....5.....6....",
                "..#...#.......#...#..",
                "....#...##.##...#....",
                ".##..#....#....#..##.",
                "........#...#........",
                ".AAA.#...BBB...#.CCC.",
                ".AAA..#..BBB..#..CCC.",
                ".AAA.#...BBB...#.CCC.",
                "........#...#........",
                ".##..#....#....#..##.",
                "....#...##.##...#....",
                "..#...#.......#...#..",
                "....1.....2.....3....",
            }),
            ("재개발 대로", new[] // 21×9 가로로 긴 개활 대로 — 저격 차선 + A·C 옆 감제 고지
            {
                "....4.....5.....6....",
                ".....................",
                ".##......#.#......##.",
                ".AAA..#..BBB..#..CCC.",
                ".AAA.^...BBB...^.CCC.",
                ".AAA..#..BBB..#..CCC.",
                ".##......#.#......##.",
                ".....................",
                "....1.....2.....3....",
            }),
            ("수직 상가", new[] // 13×17 세로로 긴 맵 — 남북 종단 로테이션, 거점이 층계처럼 걸린다
            {
                "...4..5..6...",
                ".............",
                "....#...#....",
                ".##.......##.",
                ".........CCC.",
                "..#......CCC.",
                ".........CCC.",
                ".....BBB.....",
                ".#...BBB...#.",
                ".....BBB.....",
                ".AAA.........",
                ".AAA......#..",
                ".AAA.........",
                ".##.......##.",
                "....#...#....",
                ".............",
                "...1..2..3...",
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
