using System;

namespace SeoYuGi.Battle
{
    /// <summary>그리드 파라미터(기획서 §2.1). 기본 12×12.</summary>
    [Serializable]
    public class GridConfig
    {
        public int width = 12;
        public int height = 12;
    }
}
