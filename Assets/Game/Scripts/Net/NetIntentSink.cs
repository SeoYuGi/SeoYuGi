using SeoYuGi.Battle;

namespace SeoYuGi.Net
{
    /// <summary>
    /// 클라이언트용 인텐트 싱크 — 호스트로 RPC 전송 후 Pending.
    /// 진짜 검증·실행은 호스트의 LocalIntentSink. 결과는 스냅샷으로 돌아온다.
    ///
    /// 이동만 낙관 적용: 클라 미러에서도 즉시 실행해 클릭 반응을 RTT에서 해방.
    /// 미러는 (그리드·게이지·점유)의 순수 함수라 대부분 호스트와 같은 결과가 나오고,
    /// 어긋나면 다음 스냅샷이 위치·게이지를 덮어쓴다 (SyncPresentation이 스냅 보정).
    /// 공격·스킬은 낙관 금지 — 잘못 그린 예고는 최악의 정보 오염.
    /// </summary>
    public class NetIntentSink : IIntentSink
    {
        readonly MoveSystem mirrorMove;

        public NetIntentSink(MoveSystem mirrorMove = null)
        {
            this.mirrorMove = mirrorMove;
        }

        public IntentResult Submit(in BattleIntent intent)
        {
            NetSync.ClientSendIntent(intent);

            if (intent.kind == IntentKind.Move && mirrorMove != null)
            {
                // 낙관 실행 — OnUnitMoved가 홉 애니·게이지 소모까지 그대로 태운다
                var attempt = mirrorMove.TryMove(intent.unitId, intent.target);
                if (attempt.success)
                    NetSync.MarkPredicted(intent.unitId, 0.4f); // 예측 창 — 스냅샷 고무줄 방지
                return new IntentResult { accepted = attempt.success, pending = true, moveDenied = attempt.denied };
            }

            return IntentResult.Pending;
        }
    }
}
