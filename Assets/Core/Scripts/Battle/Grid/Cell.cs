namespace SeoYuGi.Battle
{
    public enum CellType
    {
        Empty,
        Obstacle,
        Highland, // 고지대: 걸을 수 있음(진입 비용 2) · 시야 +1 · 저격 벽 관통 · 항상 노출
        Void      // 구덩이/공백: 이동 불가 · 시야와 저격은 통과 · 타일 렌더 없음 — 맵 실루엣용
    }

    /// <summary>셀 데이터(기획서 §2.2). occupantUnitId == -1 이면 비어 있음.</summary>
    public struct Cell
    {
        public const int NoUnit = -1;

        public Coord coord;
        public CellType type;
        public int occupantUnitId;
    }
}
