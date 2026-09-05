using System;
using System.Collections.Generic;
using System.Text;

namespace SeoYuGi.Battle
{
    public enum RoundRuleKind
    {
        ZoneLockdown,   // 거점을 순서대로만 — 열린 거점이 점령되면 다음 거점이 풀린다
        HighlandPower,  // 고지대에서 쏜 공격 피해 2배
        SwiftFoot,      // 이동 게이지·최대거리 1.5배
        ShortFuse,      // 제한시간 30초 단축
        NoTakeback      // 탈환 불가 — 한 번 주인이 생긴 거점은 뺏기지 않는다
    }

    /// <summary>
    /// 라운드 규칙 — 그 라운드에만 적용되는 변형. 매 라운드 뽑히지는 않는다.
    ///
    /// 규칙은 데이터일 뿐이고, 각 시스템이 자기 몫만 조회한다. 그래야 규칙을 추가할 때
    /// 표 한 줄로 끝나고 전투 로직이 규칙을 알 필요가 없다.
    /// </summary>
    public class RoundRule
    {
        public RoundRuleKind kind;

        /// <summary>거점별 활성 여부 (ZoneLockdown 전용). 그 외 규칙에서는 null.</summary>
        public bool[] zoneActive;

        public string title = "";
        public string detail = "";

        // ── 각 시스템이 물어보는 것 ──────────────────────────

        public bool ZoneEnabled(int index) =>
            zoneActive == null || index < 0 || index >= zoneActive.Length || zoneActive[index];

        /// <summary>고지대에서 쏜 공격의 피해 배율. 시전 시점에 굳힌다 —
        /// 예고가 긴 스킬은 판정 때 시전자가 이미 내려와 있을 수 있는데,
        /// "고지대에서 쐈다"가 기준이면 쏘는 순간이 맞다.</summary>
        public int HighlandDamageScale => kind == RoundRuleKind.HighlandPower ? 2 : 1;

        public float MoveScale => kind == RoundRuleKind.SwiftFoot ? 1.5f : 1f;

        public float RoundSecondsDelta => kind == RoundRuleKind.ShortFuse ? -30f : 0f;

        /// <summary>탈환 불가 — 주인이 정해진 거점은 그대로 굳는다.</summary>
        public bool NoTakebacks => kind == RoundRuleKind.NoTakeback;

        /// <summary>
        /// 봉쇄 해제 — 열린 거점이 점령되면 다음 거점이 풀린다.
        /// 한 번에 하나씩만 열려 전선이 순서대로 밀린다.
        /// </summary>
        public void OnZoneCaptured(int index)
        {
            if (kind != RoundRuleKind.ZoneLockdown || zoneActive == null) return;
            for (int i = 0; i < zoneActive.Length; i++)
            {
                if (zoneActive[i]) continue;
                zoneActive[i] = true;   // 잠겨 있던 것 중 첫 번째를 연다
                return;
            }
        }
    }

    /// <summary>라운드 규칙 추첨. 시드 기반이라 호스트와 클라가 같은 규칙을 뽑는다.</summary>
    public static class RoundRules
    {
        /// <summary>규칙이 뽑힐 확률. 1라운드는 기본기를 익히는 판이라 낮게 둔다.</summary>
        public static float ChanceFor(int round) => round <= 1 ? 0.30f : 0.60f;

        /// <summary>
        /// 그 라운드의 규칙 하나. 안 뽑히면 null (= 평범한 라운드).
        /// zoneCount가 1이면 거점 봉쇄는 후보에서 빠진다 — 점령할 곳이 없어진다.
        /// </summary>
        public static RoundRule Roll(int round, int zoneCount, int seed)
        {
            var rng = new Random(seed);
            if (rng.NextDouble() >= ChanceFor(round)) return null;

            var pool = new List<RoundRuleKind>
            {
                RoundRuleKind.HighlandPower,
                RoundRuleKind.SwiftFoot,
                RoundRuleKind.ShortFuse,
                RoundRuleKind.NoTakeback
            };
            if (zoneCount >= 2) pool.Add(RoundRuleKind.ZoneLockdown);

            var kind = pool[rng.Next(pool.Count)];
            return kind == RoundRuleKind.ZoneLockdown ? BuildLockdown(zoneCount, rng) : Build(kind);
        }

        static RoundRule Build(RoundRuleKind kind)
        {
            switch (kind)
            {
                case RoundRuleKind.HighlandPower:
                    return new RoundRule
                    {
                        kind = kind, title = "고지 장악",
                        detail = "고지대에서 쏜 공격의 피해가 두 배입니다."
                    };
                case RoundRuleKind.SwiftFoot:
                    return new RoundRule
                    {
                        kind = kind, title = "경보 해제",
                        detail = "이동 거리가 1.5배로 늘어납니다."
                    };
                case RoundRuleKind.NoTakeback:
                    return new RoundRule
                    {
                        kind = kind, title = "탈환 불가",
                        detail = "한 번 점령된 거점은 되찾을 수 없습니다."
                    };
                default:
                    return new RoundRule
                    {
                        kind = RoundRuleKind.ShortFuse, title = "단축 작전",
                        detail = "제한시간이 30초 짧습니다."
                    };
            }
        }

        /// <summary>
        /// 거점 하나만 열고 나머지는 봉쇄. 열린 거점을 점령하면 다음이 풀린다(OnZoneCaptured).
        /// 시작 거점은 매번 다르다.
        /// </summary>
        static RoundRule BuildLockdown(int zoneCount, Random rng)
        {
            int open = rng.Next(zoneCount);
            var active = new bool[zoneCount];
            active[open] = true;

            var locked = new StringBuilder();
            for (int i = 0; i < zoneCount; i++)
            {
                if (i == open) continue;
                if (locked.Length > 0) locked.Append('·');
                locked.Append(ZoneName(i));
            }

            return new RoundRule
            {
                kind = RoundRuleKind.ZoneLockdown,
                zoneActive = active,
                title = "거점 봉쇄",
                detail = $"{locked} 거점이 봉쇄됐습니다. {ZoneName(open)} 거점을 점령하면 다음이 열립니다."
            };
        }

        static readonly string[] Letters = { "A", "B", "C", "D", "E" };
        static string ZoneName(int i) => i >= 0 && i < Letters.Length ? Letters[i] : (i + 1).ToString();
    }
}
