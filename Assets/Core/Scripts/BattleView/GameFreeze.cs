using UnityEngine;

namespace SeoYuGi.BattleView
{
    /// <summary>
    /// 완전 정지(Time.timeScale = 0) 중앙 관리 — 무전 채팅·음성 녹음이 잡는다.
    /// 카운트 방식이라 겹쳐도 안전하고, HitStop이 만료 복원할 때 이걸 봐서
    /// 프리즈를 1.0으로 깨뜨리지 않는다.
    /// </summary>
    public static class GameFreeze
    {
        static int holds;

        public static bool Active => holds > 0;

        public static void Push()
        {
            holds++;
            Time.timeScale = 0f;
        }

        public static void Pop()
        {
            holds = Mathf.Max(0, holds - 1);
            if (holds == 0) Time.timeScale = 1f;
        }
    }
}
