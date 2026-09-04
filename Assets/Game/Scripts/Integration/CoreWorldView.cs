using System.Collections.Generic;
using SeoYuGi.Ai;
using SeoYuGi.Battle;
using SeoYuGi.Prediction;
using AiCell = SeoYuGi.Prediction.Cell; // Battle.Cell과 이름 충돌 — 반드시 alias로 구분

namespace SeoYuGi.Integration
{
    /// <summary>
    /// Core(BattleState/CombatSystem/RoundSystem/VisionSystem) → IWorldView 어댑터. AiBrain이 읽는 스냅샷.
    /// 매 프레임 Refresh() 호출 후 브레인들에 넘길 것. 라운드마다 새로 만든다(matchRound 고정 주입).
    ///
    /// 스텁 상태: HasDecoy = false (디코이 장비 구현 전).
    /// </summary>
    public class CoreWorldView : IWorldView
    {
        readonly BattleState state;
        readonly CombatSystem combat;
        readonly RoundSystem round;
        readonly VisionSystem vision;
        readonly int humanUnitId;
        readonly int matchRound;

        readonly List<ActorState> actors = new List<ActorState>();
        readonly List<ZoneState> zones = new List<ZoneState>();
        readonly List<Telegraph> telegraphs = new List<Telegraph>();
        AiCell[][] zoneCellCache; // 거점 칸은 라운드 내 불변 — 1회 변환

        public CoreWorldView(BattleState state, CombatSystem combat, RoundSystem round,
            VisionSystem vision, int humanUnitId, int matchRound)
        {
            this.state = state;
            this.combat = combat;
            this.round = round;
            this.vision = vision;
            this.humanUnitId = humanUnitId;
            this.matchRound = matchRound;
        }

        /// <summary>브레인 틱 전에 매 프레임 1회 호출 — 액터/예고 스냅샷 갱신.</summary>
        public void Refresh()
        {
            actors.Clear();
            foreach (var u in state.Units)
                actors.Add(new ActorState
                {
                    Id = u.id,
                    Team = (TeamId)u.team,
                    Class = (ClassId)(int)u.unitClass, // enum 순서 동일 계약
                    Pos = new AiCell(u.pos.x, u.pos.y),
                    Hp = u.hp,
                    Alive = u.alive,
                    IsHuman = u.id == humanUnitId
                });

            zones.Clear();
            for (int i = 0; i < round.Zones.Count; i++)
            {
                var z = round.Zones[i];
                if (zoneCellCache == null || zoneCellCache.Length != round.Zones.Count)
                    zoneCellCache = new AiCell[round.Zones.Count][];
                if (zoneCellCache[i] == null)
                {
                    zoneCellCache[i] = new AiCell[z.cells.Count];
                    for (int j = 0; j < z.cells.Count; j++)
                        zoneCellCache[i][j] = new AiCell(z.cells[j].x, z.cells[j].y);
                }
                zones.Add(new ZoneState
                {
                    Cell = new AiCell(z.Center.x, z.Center.y),
                    Cells = zoneCellCache[i], // 패치 전체 — AI가 빈 칸으로 분산 진입
                    HasOwner = z.owner >= 0,
                    Owner = z.owner >= 0 ? (TeamId)z.owner : default
                });
            }

            telegraphs.Clear();
            foreach (var strike in combat.ActiveStrikes)
            foreach (var c in strike.cells)
                telegraphs.Add(new Telegraph
                {
                    Cell = new AiCell(c.x, c.y),
                    Team = (TeamId)strike.team,
                    ImpactTime = strike.impactTime
                });
        }

        public float Time => state.time;
        public int Round => matchRound;
        public IReadOnlyList<ActorState> Actors => actors;
        public IReadOnlyList<ZoneState> Zones => zones;
        public IReadOnlyList<Telegraph> Telegraphs => telegraphs;

        public float GetAp(int actorId) => state.GetUnit(actorId)?.ap ?? 0f;

        public bool IsWalkable(AiCell cell) => state.Grid.IsWalkable(new Coord(cell.X, cell.Y));

        public bool IsVisibleTo(TeamId team, AiCell cell) =>
            vision.IsVisibleTo((int)team, new Coord(cell.X, cell.Y));

        public bool HasDecoy(int actorId) => false;
    }
}
