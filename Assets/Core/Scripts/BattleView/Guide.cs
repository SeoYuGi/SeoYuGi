using UnityEngine;

namespace SeoYuGi.BattleView
{
    /// <summary>
    /// 첫 판 가이드 — 별도 튜토리얼 화면이 아니라 실전 첫 라운드 위에 얹는 체크리스트.
    /// 읽는 게 아니라 "하면 넘어간다". 순서 강제 — 지금 단계의 행동만 체크된다.
    /// 2026-09-06 3단계 → 10단계 확장: 이동/질주/힐팩/공격/회피/예측/엄폐/스킬/거점/지휘.
    /// 가이드 완료 전엔 봇이 정지하고 라운드 시계도 멈춘다(러너가 관리). 한 번 끝내면 PlayerPrefs로 다시 안 뜬다.
    /// </summary>
    public static class Guide
    {
        const string DoneKey = "guide_done";

        public static bool Done
        {
            get { try { return PlayerPrefs.GetInt(DoneKey, 0) == 1; } catch { return true; } }
        }

        /// <summary>이 매치가 가이드를 진행 중인가 — 러너가 매치 시작에 정한다.</summary>
        public static bool Active { get; private set; }

        /// <summary>타이틀 "튜토리얼" 버튼으로 들어온 매치 — 첫 판 여부와 무관하게 가이드를 돈다.
        /// 심사장 PC는 이미 여러 판 돌아 첫 판 플래그가 꺼져 있을 수 있어서 이 입구가 필요하다.</summary>
        public static bool TutorialMode { get; private set; }

        /// <summary>가이드가 붙어야 하는 매치인가 — 첫 판이거나 튜토리얼 입구로 들어왔거나.</summary>
        public static bool Wanted => TutorialMode || !Done;

        public static void StartTutorial() => TutorialMode = true;
        public static void EndTutorial() => TutorialMode = false;

        // ── 단계 (순서 강제) ─────────────────────────────────

        public enum Step
        {
            Move,     // 파란 칸 클릭 = 이동
            Dash,     // 노란 칸 질주 — 쿨타임을 몸으로 체험
            Attack,   // A → 적 칸 = 공격
            Dodge,    // 스크립트 예고를 피하기
            Predict,  // 빈 칸 예측샷 — "예측해보세요!"
            Cover,    // 벽 뒤 이동 → 시연샷 강제 빗나감
            Heal,     // 힐팩 밟기 — 맞아본 다음이라야 회복할 게 있다 (풀피면 훈련 피해 1을 준다)
            Skill,    // S 스킬 1회
            Zone,     // 거점 밟아 게이지 올리기
            Radio,    // 가이드 무전 타임
            Count,    // 단계 수 (마커)
        }

        /// <summary>체크리스트 라벨 — HUD가 그대로 그린다. Step enum 순서.</summary>
        public static readonly string[] Labels =
            { "이동", "질주", "공격", "회피", "예측", "엄폐", "힐팩", "스킬", "거점", "지휘" };

        /// <summary>단계별 안내 문구 — 유닛 위 플로팅 힌트와 단계 공지 공용.</summary>
        public static string Hint(Step s) => s switch
        {
            Step.Move => "파란 칸 클릭 = 이동",
            Step.Dash => "노란 칸까지 달려보세요. 대신 잠시 못 움직입니다",
            Step.Heal => "맞은 체력을 힐팩을 밟아 회복하세요",
            Step.Attack => "적을 찾아 A 누르고 적 칸 클릭 = 공격",
            Step.Dodge => "빨간 예고가 내 칸에! 터지기 전에 옆으로 피하세요",
            Step.Predict => "적이 계속 움직입니다. 움직임을 읽고 맞히세요. 예측해보세요!",
            Step.Cover => "벽 옆 칸으로 숨어보세요. 정면 엄폐는 공격이 절반 확률로 빗나갑니다",
            Step.Skill => "S 키로 스킬을 조준하고 써보세요",
            Step.Zone => "거점을 밟아 게이지를 채우세요",
            Step.Radio => "분대에 말로 지시해 보세요",
            _ => "",
        };

        static readonly bool[] done = new bool[(int)Step.Count];

        /// <summary>지금 해야 하는 단계 — 앞에서부터 첫 미완료. 전부 끝나면 Count.</summary>
        public static Step Current
        {
            get
            {
                for (int i = 0; i < done.Length; i++)
                    if (!done[i]) return (Step)i;
                return Step.Count;
            }
        }

        public static bool IsDone(Step s) => s < Step.Count && done[(int)s];
        public static bool AllDone => Current == Step.Count;

        /// <summary>튜토리얼 조준 잠금 — 공격 조준은 공격 단계부터, 스킬 조준은 스킬 단계부터.
        /// 배우기 전의 조준이 켜지면 파란 이동 칸이 가려 진행이 막힌다.</summary>
        public static bool AimAllowed(bool skill) => !Active || Current >= (skill ? Step.Skill : Step.Attack);

        /// <summary>지금 단계일 때만 체크 — 순서 강제. 성공하면 true (러너가 피드백 연출).</summary>
        public static bool TryMark(Step s)
        {
            if (!Active || s != Current) return false;
            done[(int)s] = true;
            return true;
        }

        // ── 지휘 단계 ────────────────────────────────────────

        public const float RadioLength = 15f; // 가이드 무전 — 예시 읽고 한 줄 치는 시간

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
            for (int i = 0; i < done.Length; i++) done[i] = false;
        }

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
