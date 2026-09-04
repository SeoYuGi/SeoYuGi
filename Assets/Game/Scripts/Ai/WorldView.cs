using System.Collections.Generic;
using SeoYuGi.Prediction;

namespace SeoYuGi.Ai
{
    // 기획서 v1.3 클래스 5종: 탱커(너구리)/밸런스(치즈태비)/어쌔신(검은고양이)/중거리(비둘기)/장거리(까치)
    public enum ClassId { Tank, Balance, Assassin, Grenadier, Sniper }

    public enum CommandType { None, Move, Attack, Heavy, Guard, Decoy }

    /// AI가 코어에 내리는 명령. Move/Attack은 Target 칸, Heavy는 클래스별 해석
    /// (러너=돌파 방향 칸, 스나이퍼=조준 열의 한 칸), Guard/Decoy는 Target 무시.
    public struct AiCommand
    {
        public CommandType Type;
        public Cell Target;

        public static AiCommand None => new AiCommand { Type = CommandType.None };
        public static AiCommand Of(CommandType type, Cell target) =>
            new AiCommand { Type = type, Target = target };
    }

    public struct ActorState
    {
        public int Id;
        public TeamId Team;
        public ClassId Class;
        public Cell Pos;
        public int Hp;
        public bool Alive;
        public bool IsHuman; // 인간 조종 슬롯 — 적팀 학습·조준 우선 대상
    }

    public struct ZoneState
    {
        public Cell Cell;              // 패치 중심 (거리 계산·라벨용)
        public Cell[] Cells;           // 패치 전체 칸 — null이면 Cell 1칸 취급
        public bool HasOwner;
        public TeamId Owner;
    }

    /// 설치 공격 예고: Cell에 ImpactTime(절대 시각)에 판정이 떨어진다.
    public struct Telegraph
    {
        public Cell Cell;
        public TeamId Team;
        public float ImpactTime;
    }

    /// 코어가 구현해서 AiBrain에 넘기는 읽기 전용 월드 스냅샷.
    public interface IWorldView
    {
        float Time { get; }
        int Round { get; }
        IReadOnlyList<ActorState> Actors { get; }
        IReadOnlyList<ZoneState> Zones { get; }
        IReadOnlyList<Telegraph> Telegraphs { get; }
        float GetAp(int actorId);
        bool IsWalkable(Cell cell);              // 맵 안 && 벽 아님 && 점유 안 됨
        bool IsVisibleTo(TeamId team, Cell cell); // 해당 팀의 공유 시야 안인가 (벽 LOS 반영)
        bool HasDecoy(int actorId);               // 디코이 미사용 여부 (1기당 매치 1회, 집행은 코어)
    }
}
