using System;
using Unity.Collections;
using Unity.Netcode;
using SeoYuGi.Battle;

namespace SeoYuGi.Net
{
    /// <summary>
    /// 인게임 동기화 — 호스트가 시뮬 상태를 뿌리고 클라는 덮어쓴다 (호스트 권위).
    ///  - 스냅샷 12Hz (비신뢰): 유닛 스칼라 + 거점 게이지 + 힐팩 + 시계
    ///  - 라운드 경계 (신뢰): 시작 / 종료(+브리핑)
    /// 클라는 시뮬을 틱하지 않는다 — 시야만 로컬 재계산, 연출은 스냅샷 차분에서 파생.
    /// Battle.Core 타입에 직접 쓰므로 러너(Assembly-CSharp) 의존이 없다.
    /// </summary>
    public static class NetSync
    {
        const string MsgSnap = "sy_sn";
        const string MsgRound = "sy_rd";
        const string MsgEnd = "sy_ed";
        const string MsgIntent = "sy_it";
        const string MsgChatReq = "sy_cq";
        const string MsgChatShow = "sy_cs";
        const string MsgMoved = "sy_mv";
        const string MsgHacked = "sy_hk";
        const string MsgTele = "sy_tg";
        const string MsgTeleEnd = "sy_te";
        const string MsgSkill = "sy_sk";
        const string MsgKill = "sy_kl";
        const float SnapInterval = 1f / 12f;

        // ── 클라 수신 이벤트 (러너가 구독) ─────────────────
        public static event Action<int> OnBeginRound;                    // matchRound
        public static event Action<int, int, int, bool, string[]> OnRoundEnd; // winner, w0, w1, matchOver, briefing
        public static event Action<int, int> OnClientDamage;             // unitId, dmg — 스냅샷 차분
        public static event Action<int> OnClientDeath;                   // unitId
        public static event Action<int, int> OnClientZoneOwner;          // zoneIdx, newOwner
        public static event Action<int, int> OnChatShow;                 // unitId, lineId — 호스트 검증 통과분
        public static event Action<int, Coord[], bool> OnMoved;          // unitId, path, isYellow — 홉 연출용
        public static event Action<int> OnHacked;                        // unitId — 해킹 연출 릴레이
        public static event Action<int, float> OnClientHackCharge;       // unitId, 0..1 — 궁게이지 미러
        public static event Action<TelegraphStrike> OnTelegraph;         // 예고 주입 — 클라 미러용
        public static event Action<int, bool> OnTelegraphEnd;            // strikeId, hit — 판정 통보
        public static event Action<int, int> OnSkillCast;                // unitId, (int)SkillKind — 시전 연출
        public static event Action<int, int> OnKilled;                   // deadId, killerId(-1=환경사) — 킬피드 릴레이

        // ── 호스트 수신 이벤트 ─────────────────
        public static event Action<ulong, BattleIntent> OnIntentRequest; // sender, intent — 소유권 검증은 러너
        public static event Action<ulong, int, int> OnChatRequest;       // sender, unitId, lineId

        static BattleState battle;
        static RoundSystem round;
        static PickupSystem pickup;
        static int boundRound = -1; // 라운드 게이트 — 이전 라운드 스냅샷 드롭
        static float sendTimer;

        // 낙관 이동 예측 창 — 이 동안 해당 유닛의 위치·게이지 스냅샷을 무시해 고무줄 방지.
        // 창이 끝나면 호스트 값이 무조건 이긴다 (오예측 자동 교정).
        static int predictedUnit = -1;
        static float predictedUntil;

        /// <summary>클라 — 낙관 이동 직후 호출. seconds ≈ RTT + 스냅샷 주기.</summary>
        public static void MarkPredicted(int unitId, float seconds)
        {
            predictedUnit = unitId;
            predictedUntil = UnityEngine.Time.realtimeSinceStartup + seconds;
        }

        /// <summary>접속 직후 1회 — NetLobby.Begin에서 호출.</summary>
        public static void Register()
        {
            var mm = NetworkManager.Singleton.CustomMessagingManager;
            mm.RegisterNamedMessageHandler(MsgSnap, OnSnapMsg);
            mm.RegisterNamedMessageHandler(MsgRound, OnRoundMsg);
            mm.RegisterNamedMessageHandler(MsgEnd, OnEndMsg);
            mm.RegisterNamedMessageHandler(MsgIntent, OnIntentMsg);
            mm.RegisterNamedMessageHandler(MsgChatReq, OnChatReqMsg);
            mm.RegisterNamedMessageHandler(MsgChatShow, OnChatShowMsg);
            mm.RegisterNamedMessageHandler(MsgMoved, OnMovedMsg);
            mm.RegisterNamedMessageHandler(MsgHacked, OnHackedMsg);
            mm.RegisterNamedMessageHandler(MsgTele, OnTeleMsg);
            mm.RegisterNamedMessageHandler(MsgTeleEnd, OnTeleEndMsg);
            mm.RegisterNamedMessageHandler(MsgSkill, OnSkillMsg);
            mm.RegisterNamedMessageHandler(MsgKill, OnKillMsg);
        }

        /// <summary>호스트 — 스킬 시전 릴레이 (targeted). 즉발기는 예고가 없어 이게 유일한 통보.</summary>
        public static void HostSendSkillCast(ulong clientId, int unitId, int kind)
        {
            using var w = new FastBufferWriter(16, Allocator.Temp);
            w.WriteValueSafe(unitId);
            w.WriteValueSafe(kind);
            NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(MsgSkill, clientId, w);
        }

        static void OnSkillMsg(ulong sender, FastBufferReader r)
        {
            if (NetworkManager.Singleton.IsHost || sender != NetworkManager.ServerClientId) return;
            r.ReadValueSafe(out int unitId);
            r.ReadValueSafe(out int kind);
            OnSkillCast?.Invoke(unitId, kind);
        }

        /// <summary>호스트 — 예고를 특정 클라에게. 수신 필터(팀·시야)는 러너가 결정.</summary>
        public static void HostSendTelegraph(ulong clientId, TelegraphStrike s)
        {
            using var w = new FastBufferWriter(32 + s.cells.Count * 8, Allocator.Temp);
            w.WriteValueSafe(s.id);
            w.WriteValueSafe(s.attackerId);
            w.WriteValueSafe(s.team);
            w.WriteValueSafe(s.impactTime);
            w.WriteValueSafe((byte)s.cells.Count);
            foreach (var c in s.cells)
            {
                w.WriteValueSafe((byte)c.x);
                w.WriteValueSafe((byte)c.y);
            }
            NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(MsgTele, clientId, w);
        }

        /// <summary>호스트 — 판정 통보 (전원). 못 받은 예고 id는 클라가 조용히 무시.</summary>
        public static void HostSendTelegraphEnd(int strikeId, bool hit)
        {
            using var w = new FastBufferWriter(16, Allocator.Temp);
            w.WriteValueSafe(strikeId);
            w.WriteValueSafe(hit);
            NetworkManager.Singleton.CustomMessagingManager.SendNamedMessageToAll(MsgTeleEnd, w);
        }

        static void OnTeleMsg(ulong sender, FastBufferReader r)
        {
            if (NetworkManager.Singleton.IsHost || sender != NetworkManager.ServerClientId) return;
            var s = new TelegraphStrike();
            r.ReadValueSafe(out s.id);
            r.ReadValueSafe(out s.attackerId);
            r.ReadValueSafe(out s.team);
            r.ReadValueSafe(out s.impactTime);
            r.ReadValueSafe(out byte count);
            for (int i = 0; i < count; i++)
            {
                r.ReadValueSafe(out byte x);
                r.ReadValueSafe(out byte y);
                s.cells.Add(new Coord(x, y));
            }
            OnTelegraph?.Invoke(s);
        }

        static void OnTeleEndMsg(ulong sender, FastBufferReader r)
        {
            if (NetworkManager.Singleton.IsHost || sender != NetworkManager.ServerClientId) return;
            r.ReadValueSafe(out int strikeId);
            r.ReadValueSafe(out bool hit);
            OnTelegraphEnd?.Invoke(strikeId, hit);
        }

        /// <summary>호스트 — 이동 경로 릴레이 (targeted). 수신 대상(팀·시야)은 러너가 결정.</summary>
        public static void HostSendMoved(ulong clientId, int unitId,
            System.Collections.Generic.IReadOnlyList<Coord> path, bool yellow)
        {
            using var w = new FastBufferWriter(16 + path.Count * 8, Allocator.Temp);
            w.WriteValueSafe(unitId);
            w.WriteValueSafe(yellow);
            w.WriteValueSafe((byte)path.Count);
            foreach (var c in path)
            {
                w.WriteValueSafe((byte)c.x);
                w.WriteValueSafe((byte)c.y);
            }
            NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(MsgMoved, clientId, w);
        }

        /// <summary>호스트 — 해킹 발동 릴레이 (글리치·자막이 클라에도 뜨게).</summary>
        public static void HostSendHacked(int unitId)
        {
            using var w = new FastBufferWriter(8, Allocator.Temp);
            w.WriteValueSafe(unitId);
            NetworkManager.Singleton.CustomMessagingManager.SendNamedMessageToAll(MsgHacked, w);
        }

        static void OnMovedMsg(ulong sender, FastBufferReader r)
        {
            if (NetworkManager.Singleton.IsHost || sender != NetworkManager.ServerClientId) return;
            r.ReadValueSafe(out int unitId);
            r.ReadValueSafe(out bool yellow);
            r.ReadValueSafe(out byte count);
            var path = new Coord[count];
            for (int i = 0; i < count; i++)
            {
                r.ReadValueSafe(out byte x);
                r.ReadValueSafe(out byte y);
                path[i] = new Coord(x, y);
            }
            OnMoved?.Invoke(unitId, path, yellow);
        }

        static void OnHackedMsg(ulong sender, FastBufferReader r)
        {
            if (NetworkManager.Singleton.IsHost || sender != NetworkManager.ServerClientId) return;
            r.ReadValueSafe(out int unitId);
            OnHacked?.Invoke(unitId);
        }

        /// <summary>호스트 — 처치 릴레이 (킬피드가 전 클라에 뜨게). killerId -1 = 환경사.</summary>
        public static void HostSendKill(int deadId, int killerId)
        {
            using var w = new FastBufferWriter(8, Allocator.Temp);
            w.WriteValueSafe(deadId);
            w.WriteValueSafe(killerId);
            NetworkManager.Singleton.CustomMessagingManager.SendNamedMessageToAll(MsgKill, w);
        }

        static void OnKillMsg(ulong sender, FastBufferReader r)
        {
            if (NetworkManager.Singleton.IsHost || sender != NetworkManager.ServerClientId) return;
            r.ReadValueSafe(out int deadId);
            r.ReadValueSafe(out int killerId);
            OnKilled?.Invoke(deadId, killerId);
        }

        // ── 클라 → 호스트 ─────────────────────────────

        /// <summary>클라 — 행동 인텐트 제출. 실행·검증은 전부 호스트.</summary>
        public static void ClientSendIntent(in BattleIntent intent)
        {
            using var w = new FastBufferWriter(32, Allocator.Temp);
            w.WriteValueSafe((byte)intent.kind);
            w.WriteValueSafe(intent.unitId);
            w.WriteValueSafe(intent.target.x);
            w.WriteValueSafe(intent.target.y);
            w.WriteValueSafe(intent.skillIndex);
            NetworkManager.Singleton.CustomMessagingManager
                .SendNamedMessage(MsgIntent, NetworkManager.ServerClientId, w);
        }

        /// <summary>클라 — 빠른채팅 요청. 쿨다운·팀 필터는 호스트가 판정.</summary>
        public static void ClientSendChat(int unitId, int lineId)
        {
            using var w = new FastBufferWriter(16, Allocator.Temp);
            w.WriteValueSafe(unitId);
            w.WriteValueSafe(lineId);
            NetworkManager.Singleton.CustomMessagingManager
                .SendNamedMessage(MsgChatReq, NetworkManager.ServerClientId, w);
        }

        /// <summary>호스트 — 검증 통과한 채팅을 같은 팀 클라에게 표시 지시.</summary>
        public static void HostSendChatShow(ulong clientId, int unitId, int lineId)
        {
            using var w = new FastBufferWriter(16, Allocator.Temp);
            w.WriteValueSafe(unitId);
            w.WriteValueSafe(lineId);
            NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(MsgChatShow, clientId, w);
        }

        static void OnIntentMsg(ulong sender, FastBufferReader r)
        {
            if (!NetworkManager.Singleton.IsHost) return;
            r.ReadValueSafe(out byte kind);
            r.ReadValueSafe(out int unitId);
            r.ReadValueSafe(out int x);
            r.ReadValueSafe(out int y);
            r.ReadValueSafe(out byte skillIndex);
            OnIntentRequest?.Invoke(sender, new BattleIntent
            {
                kind = (IntentKind)kind,
                unitId = unitId,
                target = new Coord(x, y),
                skillIndex = skillIndex
            });
        }

        static void OnChatReqMsg(ulong sender, FastBufferReader r)
        {
            if (!NetworkManager.Singleton.IsHost) return;
            r.ReadValueSafe(out int unitId);
            r.ReadValueSafe(out int lineId);
            OnChatRequest?.Invoke(sender, unitId, lineId);
        }

        static void OnChatShowMsg(ulong sender, FastBufferReader r)
        {
            if (sender != NetworkManager.ServerClientId) return;
            r.ReadValueSafe(out int unitId);
            r.ReadValueSafe(out int lineId);
            OnChatShow?.Invoke(unitId, lineId);
        }

        /// <summary>클라 — BuildRound 직후 코어 참조 바인딩. 이걸 해야 스냅샷이 적용된다.</summary>
        public static void ClientBind(BattleState b, RoundSystem r, PickupSystem p, int matchRound)
        {
            battle = b;
            round = r;
            pickup = p;
            boundRound = matchRound;
        }

        // ── 호스트 송신 ─────────────────────────────

        /// <summary>호스트 — 라운드 조립 완료 알림. 클라는 수신 즉시 같은 라운드를 조립한다.</summary>
        public static void HostSendBeginRound(int matchRound)
        {
            using var w = new FastBufferWriter(8, Allocator.Temp);
            w.WriteValueSafe(matchRound);
            NetworkManager.Singleton.CustomMessagingManager.SendNamedMessageToAll(MsgRound, w);
        }

        /// <summary>
        /// 호스트 — 매 프레임 호출. 내부에서 12Hz로 스로틀.
        /// 팀별 필터: 그 팀 소속 or 그 팀 시야 안의 유닛만 전송 — 패킷 스니핑 맵핵 차단.
        /// 빠진 유닛은 클라가 마지막 값 유지 (어차피 시야 밖 = 화면에서 숨김).
        /// </summary>
        public static void HostTick(float realDt, BattleState b, RoundSystem r, PickupSystem p,
            int matchRound, VisionSystem vision,
            Func<int, float> hackCharge = null, Func<int, bool> teamRevealed = null)
        {
            sendTimer += realDt;
            if (sendTimer < SnapInterval) return;
            sendTimer = 0f;

            for (int team = 0; team < 2; team++)
            {
                bool any = false;
                foreach (var s in NetLobby.Slots)
                    if (s.owner == SlotOwner.RemoteHuman && s.team == team) { any = true; break; }
                if (!any) continue; // 그 팀에 원격 인간 없음 — 전송 생략

                using var w = new FastBufferWriter(1024, Allocator.Temp);
                w.WriteValueSafe(matchRound);
                w.WriteValueSafe(b.time);

                // 해킹 시야 강탈 중인 팀엔 적 전원 포함 — 시야 필터 일시 해제
                bool reveal = teamRevealed != null && teamRevealed(team);

                byte visibleCount = 0;
                foreach (var u in b.Units)
                    if (u.team == team || reveal || vision.IsVisibleTo(team, u.pos)) visibleCount++;
                w.WriteValueSafe(visibleCount);
                foreach (var u in b.Units)
                {
                    if (u.team != team && !reveal && !vision.IsVisibleTo(team, u.pos)) continue;
                    w.WriteValueSafe((byte)u.id);
                    w.WriteValueSafe((byte)u.pos.x);
                    w.WriteValueSafe((byte)u.pos.y);
                    w.WriteValueSafe((sbyte)u.hp);
                    w.WriteValueSafe(u.alive);
                    w.WriteValueSafe(u.attackReadyAt);
                    w.WriteValueSafe(hackCharge != null ? hackCharge(u.id) : 0f);
                    w.WriteValueSafe(u.moveGauge);
                    w.WriteValueSafe(u.moveCooldown);
                    w.WriteValueSafe(u.regenDelay);
                    w.WriteValueSafe(u.attackBuffUntil);
                    w.WriteValueSafe(u.stunnedUntil);
                    w.WriteValueSafe(u.flyingUntil);
                    w.WriteValueSafe(u.skillReadyAt[0]);
                    w.WriteValueSafe(u.skillReadyAt[1]);
                }

                w.WriteValueSafe((byte)r.Zones.Count);
                foreach (var z in r.Zones)
                {
                    w.WriteValueSafe((sbyte)z.owner);
                    w.WriteValueSafe((sbyte)z.capturingTeam);
                    w.WriteValueSafe(z.progress);
                }

                w.WriteValueSafe((byte)p.Packs.Count);
                foreach (var pack in p.Packs)
                {
                    w.WriteValueSafe(pack.active);
                    w.WriteValueSafe(pack.respawnAt);
                }

                foreach (var s in NetLobby.Slots)
                    if (s.owner == SlotOwner.RemoteHuman && s.team == team)
                        NetworkManager.Singleton.CustomMessagingManager
                            .SendNamedMessage(MsgSnap, s.clientId, w, NetworkDelivery.Unreliable);
            }
        }

        /// <summary>호스트 — 라운드 종료. 브리핑은 클라별 유닛 기준이라 targeted 전송.</summary>
        public static void HostSendRoundEnd(ulong clientId, int winner, int w0, int w1, bool matchOver, string[] briefing)
        {
            using var w = new FastBufferWriter(2048, Allocator.Temp);
            w.WriteValueSafe(winner);
            w.WriteValueSafe(w0);
            w.WriteValueSafe(w1);
            w.WriteValueSafe(matchOver);
            w.WriteValueSafe(briefing?.Length ?? 0);
            if (briefing != null)
                foreach (var line in briefing)
                    w.WriteValueSafe(line);
            NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(MsgEnd, clientId, w);
        }

        // ── 클라 수신 ─────────────────────────────

        static void OnRoundMsg(ulong sender, FastBufferReader r)
        {
            if (NetworkManager.Singleton.IsHost || sender != NetworkManager.ServerClientId) return;
            r.ReadValueSafe(out int matchRound);
            OnBeginRound?.Invoke(matchRound);
        }

        static void OnEndMsg(ulong sender, FastBufferReader r)
        {
            if (NetworkManager.Singleton.IsHost || sender != NetworkManager.ServerClientId) return;
            r.ReadValueSafe(out int winner);
            r.ReadValueSafe(out int w0);
            r.ReadValueSafe(out int w1);
            r.ReadValueSafe(out bool matchOver);
            r.ReadValueSafe(out int lineCount);
            var lines = new string[lineCount];
            for (int i = 0; i < lineCount; i++)
                r.ReadValueSafe(out lines[i]);
            boundRound = -1; // 라운드 종료 — 잔여 스냅샷 드롭
            OnRoundEnd?.Invoke(winner, w0, w1, matchOver, lines);
        }

        static void OnSnapMsg(ulong sender, FastBufferReader r)
        {
            if (NetworkManager.Singleton.IsHost || sender != NetworkManager.ServerClientId) return;

            r.ReadValueSafe(out int matchRound);
            if (battle == null || matchRound != boundRound) return; // 조립 전/이전 라운드 — 드롭

            r.ReadValueSafe(out float time);
            battle.time = time;

            r.ReadValueSafe(out byte unitCount);
            for (int i = 0; i < unitCount; i++)
            {
                r.ReadValueSafe(out byte id);
                r.ReadValueSafe(out byte x);
                r.ReadValueSafe(out byte y);
                r.ReadValueSafe(out sbyte hp);
                r.ReadValueSafe(out bool alive);
                r.ReadValueSafe(out float attackReadyAt);
                r.ReadValueSafe(out float hackG);
                r.ReadValueSafe(out float gauge);
                r.ReadValueSafe(out float cooldown);
                r.ReadValueSafe(out float regen);
                r.ReadValueSafe(out float buffUntil);
                r.ReadValueSafe(out float stunnedUntil);
                r.ReadValueSafe(out float flyingUntil);
                r.ReadValueSafe(out float skill0At);
                r.ReadValueSafe(out float skill1At);

                var u = battle.GetUnit(id);
                if (u == null) continue;

                // 차분 연출 — 덮어쓰기 전에 감지
                if (hp < u.hp && alive) OnClientDamage?.Invoke(id, u.hp - hp);
                if (!alive && u.alive) OnClientDeath?.Invoke(id);

                // 낙관 이동 예측 창 — 내 유닛의 운동 상태(위치·게이지)는 잠시 로컬이 이긴다
                bool predicted = id == predictedUnit &&
                                 UnityEngine.Time.realtimeSinceStartup < predictedUntil;

                var newPos = new Coord(x, y);
                if (!predicted && !u.pos.Equals(newPos))
                {
                    if (u.alive) battle.Grid.MoveOccupant(u.pos, newPos); // 점유 맵 동기 — 시야 계산 입력
                    u.pos = newPos;
                }
                if (!alive && u.alive) battle.Grid.RemoveUnit(u.pos);
                u.hp = hp;
                u.alive = alive;
                u.attackReadyAt = attackReadyAt;
                OnClientHackCharge?.Invoke(id, hackG); // 궁게이지 미러 — HUD 표시용
                if (!predicted)
                {
                    u.moveGauge = gauge;
                    u.moveCooldown = cooldown;
                    u.regenDelay = regen;
                }
                u.attackBuffUntil = buffUntil;
                u.stunnedUntil = stunnedUntil;
                u.flyingUntil = flyingUntil;
                u.skillReadyAt[0] = skill0At;
                u.skillReadyAt[1] = skill1At;
            }

            r.ReadValueSafe(out byte zoneCount);
            for (int i = 0; i < zoneCount && i < round.Zones.Count; i++)
            {
                r.ReadValueSafe(out sbyte owner);
                r.ReadValueSafe(out sbyte capturing);
                r.ReadValueSafe(out float progress);

                var z = round.Zones[i];
                if (owner != z.owner && owner >= 0) OnClientZoneOwner?.Invoke(i, owner);
                z.owner = owner;
                z.capturingTeam = capturing;
                z.progress = progress;
            }

            r.ReadValueSafe(out byte packCount);
            for (int i = 0; i < packCount && i < pickup.Packs.Count; i++)
            {
                r.ReadValueSafe(out bool active);
                r.ReadValueSafe(out float respawnAt);
                pickup.Packs[i].active = active;
                pickup.Packs[i].respawnAt = respawnAt;
            }
        }
    }
}
