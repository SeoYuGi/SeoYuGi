using System;

namespace SeoYuGi.Battle
{
    /// <summary>
    /// 그리드 정수 좌표. 좌하단 원점, (x, y).
    /// UnityEngine.Vector2Int 대신 사용 — Core는 엔진 비의존(기획서 §6.1).
    /// 방향/거리 연산을 여기에 캡슐화해 육각/8방향 확장 지점을 한정한다(§9).
    /// </summary>
    [Serializable]
    public struct Coord : IEquatable<Coord>
    {
        public int x;
        public int y;

        public Coord(int x, int y)
        {
            this.x = x;
            this.y = y;
        }

        public static readonly Coord Zero = new Coord(0, 0);
        public static readonly Coord Up = new Coord(0, 1);
        public static readonly Coord Down = new Coord(0, -1);
        public static readonly Coord Left = new Coord(-1, 0);
        public static readonly Coord Right = new Coord(1, 0);

        /// <summary>4방향. 순서 고정(결정론) — Up, Down, Left, Right.</summary>
        public static readonly Coord[] Directions4 = { Up, Down, Left, Right };

        /// <summary>8방향. 순서 고정(결정론) — 십자 4방 뒤 대각 4방.</summary>
        public static readonly Coord[] Directions8 =
        {
            Up, Down, Left, Right,
            new Coord(1, 1), new Coord(1, -1), new Coord(-1, 1), new Coord(-1, -1)
        };

        public static Coord operator +(Coord a, Coord b) => new Coord(a.x + b.x, a.y + b.y);
        public static Coord operator -(Coord a, Coord b) => new Coord(a.x - b.x, a.y - b.y);
        public static bool operator ==(Coord a, Coord b) => a.x == b.x && a.y == b.y;
        public static bool operator !=(Coord a, Coord b) => !(a == b);

        public static int Manhattan(Coord a, Coord b) => Math.Abs(a.x - b.x) + Math.Abs(a.y - b.y);

        public bool Equals(Coord other) => this == other;
        public override bool Equals(object obj) => obj is Coord other && this == other;
        public override int GetHashCode() => x * 397 + y;
        public override string ToString() => $"({x},{y})";
    }
}
