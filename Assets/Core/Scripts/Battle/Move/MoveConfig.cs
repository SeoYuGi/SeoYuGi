using System;

namespace SeoYuGi.Battle
{
    /// <summary>탱고파이브식 실시간 이동 파라미터. 전부 인스펙터 조정 가능.</summary>
    [Serializable]
    public class MoveConfig
    {
        public int freeRange = 2;                // 게이지 최대 = 파랑 이동 한도(칸)
        public int maxRange = 4;                 // 클릭 한 번에 갈 수 있는 최대 거리(칸)
        public float gaugeRegenPerSecond = 1f;   // 게이지 초당 회복량
        public float yellowCooldownSeconds = 2.5f; // 노랑 이동 후 행동 불가 시간
        public float hopDuration = 0.15f;        // 1칸 홉 연출 시간 (View용)
    }
}
