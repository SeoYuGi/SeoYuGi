using UnityEngine;

namespace SeoYuGi.BattleView
{
    /// <summary>
    /// 첫 판 가이드 (2026-09-05) — 별도 튜토리얼 화면이 아니라 실전 첫 라운드 위에 얹는 세 단계.
    /// 읽는 게 아니라 "하면 넘어간다":
    ///   ① 거점 — 라운드 개시 정지 동안 거점이 반짝이며 한 줄 (모든 모드)
    ///   ② 조작 — 내 유닛 위에 "파란 칸 클릭 = 이동" → 한 번 움직이면 끝. 적이 보이면 "A → 적 칸 = 공격" → 한 번 쏘면 끝
    ///   ③ 지휘 — 20초에 가이드 무전 타임: 얼음 + 무전창 자동 오픈 + 예시 문장 회전 ("라니는 B로, 나머지는 나한테 붙어")
    /// ②③은 싱글 지휘관 모드에서만. 한 번 끝내면 PlayerPrefs로 다시 안 뜬다.
    /// 프리셋 버튼을 새로 그리지 않는다 — 퀵챗 패널(우상단)이 이미 그 역할이고, 지휘의 킬포는 자유 문장이다.
    /// </summary>
    public static class Guide
    {
        const string DoneKey = "guide_done";

        public static bool Done
        {
            get { try { return PlayerPrefs.GetInt(DoneKey, 0) == 1; } catch { return true; } }
        }

        /// <summary>이 매치가 가이드를 진행 중인가 (②③) — 러너가 매치 시작에 정한다.</summary>
        public static bool Active { get; private set; }

        public static bool MoveDone { get; private set; }
        public static bool AttackDone { get; private set; }
        public static bool RadioDone { get; private set; }

        /// <summary>③ 가이드 무전 타임이 나올 전투 시각.</summary>
        public const float RadioAt = 20f;
        public const float RadioLength = 14f; // 첫 무전은 넉넉히 — 예시 읽고 한 줄 치는 시간

        /// <summary>무전창에 돌아가는 예시 — 여러 유닛·조건·적 지목·별명 호칭이 다 된다는 걸 읽지 않아도 보이게.</summary>
        public static readonly string[] RadioExamples =
        {
            "라니는 B 거점으로, 나머지는 나한테 붙어",
            "피 없는 애는 뒤로 빠지고 까돌은 고지대 잡아",
            "상대 지휘관부터 노려",
            "깜냥, 적 저격수 뒤로 돌아가",
            "전원 A 거점으로 모여서 버텨",
        };

        public static void Begin()
        {
            Active = true;
            MoveDone = AttackDone = RadioDone = false;
        }

        public static void MarkMove() => MoveDone = true;
        public static void MarkAttack() => AttackDone = true;
        public static void MarkRadio() => RadioDone = true;

        /// <summary>가이드 종료 — 다시 안 뜬다.</summary>
        public static void Finish()
        {
            Active = false;
            try { PlayerPrefs.SetInt(DoneKey, 1); PlayerPrefs.Save(); } catch { }
        }

        /// <summary>타이틀 "가이드 다시 보기" 용.</summary>
        public static void Reset()
        {
            try { PlayerPrefs.SetInt(DoneKey, 0); PlayerPrefs.Save(); } catch { }
        }
    }
}
