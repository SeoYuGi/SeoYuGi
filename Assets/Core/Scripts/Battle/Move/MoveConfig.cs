using System;

namespace SeoYuGi.Battle
{
    /// <summary>
    /// 유닛 1기의 이동 특성. 캐릭터(클래스)별로 다른 프로필을 꽂을 수 있다.
    /// 예: 러너 = freeRange·maxRange 크게, 스나이퍼 = 작게 + 쿨타임 길게.
    /// </summary>
    [Serializable]
    public class MoveProfile
    {
        public int freeRange = 2;                 // 게이지 최대 = 파랑 이동 한도(칸)
        public int maxRange = 4;                  // 클릭 한 번에 갈 수 있는 최대 거리(칸)
        public float gaugeRegenPerSecond = 0.5f;  // 게이지 초당 회복량
        public float regenDelaySeconds = 1f;      // 이동 직후 게이지 회복 정지 시간 (홉 스팸 방지)
        public float yellowCooldownSeconds = 3f;  // 노랑 이동 후 이동 불가 시간
    }

    /// <summary>전투 공통 이동 설정. 프로필 없는 유닛은 defaultProfile을 쓴다.</summary>
    [Serializable]
    public class MoveConfig
    {
        public MoveProfile defaultProfile = new MoveProfile();
        public float hopDuration = 0.15f; // 1칸 홉 연출 시간 (View용)
    }
}
