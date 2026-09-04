namespace SeoYuGi.Battle
{
    public enum CellType
    {
        Empty,
        Obstacle
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
