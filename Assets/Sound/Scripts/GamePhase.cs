/// <summary>
/// BGM 전환 기준이 되는 게임 페이즈.
/// SoundManager.SetPhase()로 변경 → BGM 자동 crossfade.
/// </summary>
public enum GamePhase
{
    Lobby,             // 로비 / 팀 메이킹 — 느긋한 BGM
    Building,          // 건축 타이머 진행 중 — 활기찬 BGM
    BuildingUrgent,    // 타이머 60초 이하 — 긴박한 BGM
    Result,            // 채점 / 결과 화면
}
