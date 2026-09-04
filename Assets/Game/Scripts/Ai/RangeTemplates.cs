using System.Collections.Generic;
using SeoYuGi.Prediction;

namespace SeoYuGi.Ai
{
    /// <summary>
    /// 클래스·능력별 "사용 가능 범위" 오프셋 템플릿 (기획 다이어그램 2026-09-05 원본).
    /// 게임 화면에는 이 템플릿에서 벽 LOS·고지대 규칙으로 걸러진 '실제 공격 가능 칸'만 표시하고,
    /// 그중 1칸을 골라 발동한다. 필터링은 코어(VisionSystem/타깃팅) 소관 — 여긴 순수 모양 데이터.
    /// AiBrain과 범위 표시 UI가 같은 테이블을 쓰면 어긋날 일이 없다.
    /// </summary>
    public static class RangeTemplates
    {
        /// <summary>8방 인접 3×3 링 — 너구리·고라니·검은냥 기본공격, 발톱, 비명 교란, 방패밀기, 강타, 까치 넉백탄.</summary>
        public static readonly Cell[] Adjacent8 = Ring(1);

        /// <summary>반경 2 원형(5×5에서 네 모서리 제외) — 비둘기 기본공격.</summary>
        public static readonly Cell[] Circle2 = Build(2, (dx, dy) =>
            (dx != 0 || dy != 0) && !(Abs(dx) == 2 && Abs(dy) == 2));

        /// <summary>5×5 링(자기 제외 24칸) — 까치 기본공격. (검은냥 도약 착지와 같은 모양 = Square2)</summary>
        public static readonly Cell[] MagpieBasic = Build(2, (dx, dy) => dx != 0 || dy != 0);

        /// <summary>맨해튼 4 다이아몬드 + 십자 방향 5칸 연장 — 까치 조준 사격 지정 범위 (열 전체에서 변경).</summary>
        public static readonly Cell[] SnipeRange = BuildRect(5, (dx, dy) =>
            (dx != 0 || dy != 0) && (Abs(dx) + Abs(dy) <= 4 || (dx == 0 && Abs(dy) == 5) || (dy == 0 && Abs(dx) == 5)));

        /// <summary>십자 직선 2칸 — 고라니 돌파(방향 선택).</summary>
        public static readonly Cell[] Cross2 = Cross(2);

        /// <summary>5×5 링(자기 제외 24칸) — 검은냥 그림자 도약 착지 후보.</summary>
        public static readonly Cell[] Square2 = Build(2, (dx, dy) => dx != 0 || dy != 0);

        /// <summary>5×5 전체(중심 포함) — 비둘기 파열탄 지정 가능 범위.</summary>
        public static readonly Cell[] Square2WithCenter = Build(2, (dx, dy) => true);

        /// <summary>십자 5칸(중심+4방) — 파열탄·폭탄 배달의 투하(피해) 모양. 지정 칸 기준 오프셋.</summary>
        public static readonly Cell[] BlastCross = { new Cell(0, 0), new Cell(0, 1), new Cell(0, -1), new Cell(1, 0), new Cell(-1, 0) };

        // TODO(기획 확인 대기): 비둘기 폭탄 배달 지정 범위(맨해튼 4 다이아 + 연장?)

        /// <summary>클래스별 기본공격 범위.</summary>
        public static Cell[] BasicAttack(ClassId cls)
        {
            switch (cls)
            {
                case ClassId.Grenadier: return Circle2;
                case ClassId.Sniper: return MagpieBasic;
                default: return Adjacent8;
            }
        }

        public static bool Contains(Cell[] template, Cell from, Cell target)
        {
            int dx = target.X - from.X, dy = target.Y - from.Y;
            foreach (var o in template)
                if (o.X == dx && o.Y == dy) return true;
            return false;
        }

        static Cell[] Ring(int r)
        {
            return Build(r, (dx, dy) => (dx != 0 || dy != 0) && Abs(dx) <= r && Abs(dy) <= r);
        }

        static Cell[] Cross(int len)
        {
            var list = new List<Cell>();
            for (int i = 1; i <= len; i++)
            {
                list.Add(new Cell(0, i));
                list.Add(new Cell(0, -i));
                list.Add(new Cell(i, 0));
                list.Add(new Cell(-i, 0));
            }
            return list.ToArray();
        }

        static Cell[] BuildRect(int r, System.Func<int, int, bool> pred) => BuildImpl(r, pred);

        static Cell[] Build(int r, System.Func<int, int, bool> pred) => BuildImpl(r, pred);

        static Cell[] BuildImpl(int r, System.Func<int, int, bool> pred)
        {
            var list = new List<Cell>();
            for (int dx = -r; dx <= r; dx++)
            for (int dy = -r; dy <= r; dy++)
                if (pred(dx, dy)) list.Add(new Cell(dx, dy));
            return list.ToArray();
        }

        static int Abs(int v) => v < 0 ? -v : v;
    }
}
