namespace SeoYuGi.Battle
{
    /// <summary>
    /// 플레이 모드 — 타이틀에서 고른다.
    ///
    /// Multi     : 기존 게임 그대로. 실사람 매칭, 부족분 봇. 내 유닛 1기만 조종한다.
    /// Commander : 내 유닛 1기는 똑같이 직접 조종하되, 팀원 2기에게 무전으로 지휘한다.
    ///             지휘는 방향성만 준다(어디로·뭉칠지·붙을지) — 스킬·조준·회피·경로는
    ///             기존 AiBrain이 그대로 처리한다. 전투는 자동 그대로다.
    ///
    /// 모드는 매치 단위 전역 상태다. 전투 로직은 이 값을 보지 않는다 —
    /// 지휘 입력을 받을지 말지만 갈린다.
    /// </summary>
    public enum GameMode
    {
        Multi = 0,
        Commander
    }

    public static class GameModeState
    {
        /// <summary>현재 매치의 모드. 타이틀에서 설정하고 매치 내내 유지된다.</summary>
        public static GameMode Current = GameMode.Multi;

        public static bool IsCommander => Current == GameMode.Commander;

        /// <summary>훈련장 (2026-09-05) — 가장 작은 맵, 내 유닛 + 죽지 않는 허수아비 하나. 승패·시간 없음, F1~F5로 캐릭터 교체.
        /// 모드 enum이 아니라 플래그 — 매치 구성·라운드 판정만 갈리고 전투 로직은 그대로.</summary>
        public static bool Training;
    }
}
