using System;

namespace SeoYuGi.Battle
{
    /// <summary>슬롯이 코어에 요청하는 행동 1건. 로컬 인간·봇·원격 인간이 전부 이 형태로 제출한다.</summary>
    public enum IntentKind : byte
    {
        None = 0,
        Move,
        Attack,
        Skill,
        Guard,
        Hack
    }

    /// <summary>
    /// 행동 인텐트 — 계약서 §4 "슬롯에 컨트롤러 꽂기"의 공용 화폐.
    /// 실행(AP 차감·판정)은 전부 코어 소관. 여기는 요청만 담는다.
    /// </summary>
    public struct BattleIntent
    {
        public IntentKind kind;
        public int unitId;
        public Coord target; // Guard/Hack은 무시

        public static BattleIntent Move(int unitId, Coord target) =>
            new BattleIntent { kind = IntentKind.Move, unitId = unitId, target = target };
        public static BattleIntent Attack(int unitId, Coord target) =>
            new BattleIntent { kind = IntentKind.Attack, unitId = unitId, target = target };
        public static BattleIntent Skill(int unitId, Coord target) =>
            new BattleIntent { kind = IntentKind.Skill, unitId = unitId, target = target };
        public static BattleIntent Guard(int unitId) =>
            new BattleIntent { kind = IntentKind.Guard, unitId = unitId };
        public static BattleIntent Hack(int unitId) =>
            new BattleIntent { kind = IntentKind.Hack, unitId = unitId };
    }

    /// <summary>
    /// 제출 결과. 로컬 싱크는 즉시 실행해 진짜 결과를, 네트워크 싱크는 Pending을 돌려준다.
    /// 거부 사유는 기존 enum을 그대로 실어 호출부의 버저·로그 동작을 보존한다.
    /// </summary>
    public struct IntentResult
    {
        public bool accepted;
        public bool pending;          // 네트워크 제출 — 결과는 나중에 온다
        public MoveDenied moveDenied; // kind == Move일 때만 유효
        public ActDenied actDenied;   // Attack/Skill/Guard일 때만 유효

        public static readonly IntentResult Accepted = new IntentResult { accepted = true };
        public static readonly IntentResult Pending = new IntentResult { pending = true };
    }

    /// <summary>행동 제출의 단일 통로 — 이 인터페이스 하나가 싱글/멀티의 이음매다.</summary>
    public interface IIntentSink
    {
        IntentResult Submit(in BattleIntent intent);
    }
}
