using System;
using System.Collections.Generic;

namespace SeoYuGi.Chat
{
    /// <summary>
    /// 빠른채팅 — 숫자키 1~8로 즉시 전송하는 전술 무전 프리셋.
    /// 방사형 휠 대신 숫자키인 이유: 실시간 게임이라 마우스가 조준에 묶여 있다.
    ///
    /// UnityEngine 비의존(시각은 호출부 담당) — 멀티에서 호스트가 검증할 때 그대로 재사용한다.
    /// 시각은 호출부(ChatBubble/BattleHud)가, 팀 필터도 호출부가 담당한다.
    /// </summary>
    public class QuickChat
    {
        /// <summary>숫자키 1~8에 대응. 지시 4 / 경고 1 / 사교 2 + 집결 1 배분.</summary>
        public static readonly string[] Lines =
        {
            "적 발견!",     // 1
            "지원 요청!",   // 2
            "거점으로!",    // 3
            "후퇴!",        // 4
            "뭉쳐!",        // 5
            "예고 조심!",   // 6 — 이 게임 고유(설치 공격 경고)
            "나이스!",      // 7
            "미안!"         // 8
        };

        /// <summary>해킹 발동 시 자동 전송. 매치 1회뿐인 필살기라 수동 슬롯을 낭비하지 않는다.</summary>
        public const int HackLine = -1;
        const string HackText = "해킹 간다!";

        public const float CooldownSeconds = 2f;

        readonly Dictionary<int, float> nextAllowed = new Dictionary<int, float>();

        /// <summary>(unitId, lineId) — 쿨다운을 통과한 발신만 발화.</summary>
        public event Action<int, int> OnMessage;

        public static bool IsValidLine(int lineId) => lineId == HackLine || (lineId >= 0 && lineId < Lines.Length);

        public static string TextOf(int lineId)
        {
            if (lineId == HackLine) return HackText;
            return lineId >= 0 && lineId < Lines.Length ? Lines[lineId] : null;
        }

        /// <summary>now = 임의의 단조 증가 초 단위 시계(호출부가 공급).</summary>
        public bool TrySend(int unitId, int lineId, float now)
        {
            if (!IsValidLine(lineId)) return false;
            // 해킹 문구는 쿨다운을 무시한다 — 매치 1회뿐이라 스팸이 불가능하고,
            // 직전에 채팅했다는 이유로 필살기 알림이 삼켜지면 안 된다.
            if (lineId != HackLine && nextAllowed.TryGetValue(unitId, out float t) && now < t) return false;

            nextAllowed[unitId] = now + CooldownSeconds;
            OnMessage?.Invoke(unitId, lineId);
            return true;
        }
    }
}
