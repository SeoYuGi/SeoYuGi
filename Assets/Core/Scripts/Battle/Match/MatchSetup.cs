using System.Collections.Generic;

namespace SeoYuGi.Battle
{
    /// <summary>슬롯 주인 — 계약서 §4의 HumanLocal / AIBrain / NetworkRemote.</summary>
    public enum SlotOwner : byte
    {
        Bot = 0,
        LocalHuman,
        RemoteHuman
    }

    /// <summary>슬롯 1개 구성. 멀티 로비가 이 배열을 채우고, 빈 슬롯은 Bot으로 남는다.</summary>
    public struct SlotConfig
    {
        public int unitId;
        public int team;
        public UnitClass cls;
        public string callsign;
        public SlotOwner owner;
        public ulong ownerClientId; // RemoteHuman일 때만 유효

        public bool IsHuman => owner != SlotOwner.Bot;
    }

    /// <summary>매치 1판 구성 — 맵·슬롯·적팀 롤 시드. 호스트가 만들고 클라에 그대로 복제된다.</summary>
    public class MatchSetup
    {
        public int mapIndex;
        public int enemyRollSeed; // 적팀 클래스 롤 재현용
        public bool commander;    // 지휘관 대전 — 인간이 있는 팀의 봇은 그 인간의 무전 지휘를 받는다 (2026-09-05)
        public SlotConfig[] slots;

        /// <summary>인간 조종 슬롯의 unitId 집합 — Predictor 학습 대상.</summary>
        public HashSet<int> HumanUnitIds()
        {
            var set = new HashSet<int>();
            foreach (var s in slots)
                if (s.IsHuman) set.Add(s.unitId);
            return set;
        }
    }
}
