/// <summary>
/// SFX 종류 enum. SoundManager.PlaySFX(SFXType)로 호출.
/// 호출부는 AudioClip을 직접 들고 다닐 필요 없음.
///
/// ── 현재 연결 가능 ────────────────────────────────────────
/// PlayerFootstep : PlayerDustTrail.Update() — isMoving 감지 + 0.35초 쿨다운
/// PlayerBounce   : PlayerBounce.OnCollisionEnter() — 충돌 처리 후 1줄 추가
///
/// ── 예측 (해당 시스템 구현 후 enum 추가 + SoundLibrary.asset 클립 연결) ──
/// BlockPickUp       — PlayerBuildingHandler.TryPickUp()
/// BlockPlace        — PlaceBuildingCommand.Execute()
/// BlockFixed        — GridManager.TryAdvanceProcess() (Fixed 상태 전환 시)
/// BlockFinished     — GridManager.TryAdvanceProcess() (Finished 상태 전환 시)
/// BlockCollapse     — CollapseManager [ClientRpc]
/// BlockCollapseChain— CollapseManager [ClientRpc] (연쇄 붕괴)
/// TimerWarning      — 타이머 매니저 (30초 경고, 1회)
/// TimerTick         — 타이머 매니저 (마지막 10초 틱)
/// TimerEnd          — 타이머 매니저 (타이머 0)
/// ButtonClick       — 각 HUD / Popup
/// GameStart         — 건축 시작 연출
/// VoteOpen          — 건축 종료 투표 열림
/// VoteAgree         — 투표 동의
/// ScoreReveal       — 점수 공개 연출
/// </summary>
public enum SFXType
{
    PlayerFootstep,
    PlayerBounce,
    UIClick,
    PickUpObject,
    LandObject,
    ThrowObject,
    FallObjectWhileThrowing,
    Hammering,
    Painting,
    BumpPlayers,
    Dash,
    Jump,
    GameOver,
    // ── 경복궁(08/28) — 클립: Assets/Sound/Clips/Gyeongbokgung, 연결: Tools ▸ Sound ▸ 경복궁 사운드 연결 ──
    FireIgnite,    // 발화 순간(화마 급강하)
    FireBurning,   // 타는 중 루프 — SoundLibrary가 아니라 Resources/Sfx/FireBurning을 화염 그룹 AudioSource가 직접 재생
    WaterFill,     // 드므에서 물 뜨기
    WaterPour,     // 양동이 물 붓기(진화 완료)
    HolyChime,     // 사방신 낙하·안착·봉인(신성한 소리)

    // ── 아이템(2vs2 경쟁) — 클립: Assets/Sound/Clips/Gameplay, 연결: Tools ▸ Sound ▸ 아이템·날씨·맵 사운드 연결 ──
    // 클립이 SoundLibrary에 없으면 ItemFx의 기존 합성음(뾰롱류)으로 폴백 — 미연결 상태여도 무음이 되지 않는다.
    // (주의) 이 아래는 SoundLibrary.asset에 enum 정수값으로 저장된다 — 중간 삽입/삭제 시
    //        뒤 항목들의 저장값이 밀리므로, 순서를 바꾸면 asset의 type 인덱스도 같이 마이그레이션할 것.
    ItemBoxSpawn,       // 상자 등장 뾰롱 — ItemFx.Spawned
    ItemPickup,         // 상자 획득 뾰롱 — ItemFx.PickedUp (발동 공통 스윕은 합성음 고정 — 전용 타입 없음)
    ItemCannonFire,     // 대포 발사 '펑~' — ItemFx.CannonShot (착탄음은 LandObject 별도)
    ItemEarthquake,     // 지진 '쿠르릉' 돌 구르는 소리 — GridNetwork.EarthquakeFxRpc
    ItemOrderHack,      // 주문 해킹 '삐리릭' 오류음 — 전 클라 2D(시전자·피격자 모두)
    ItemSlowdown,       // 공정/이동속도 저하 하강음(띠로리, 8bit) — 전 클라 2D
    ItemSpeedup,        // 공정/이동속도 상승 상승음(저하의 반대 느낌) — 전 클라 2D
    ItemFog,            // 안개 '피유융' 연막탄 바람소리 — 전 클라 2D

    // ── 날씨 — 앰비언스는 루프(PlayLoop), 미끄덩은 원샷 ──
    WeatherRainLoop,    // 비 — 빗소리 루프(세션 날씨·아이템 날씨 공용, TeamWeatherFx)
    WeatherWindLoop,    // 강풍 — 바람소리 루프
    WeatherTyphoonLoop, // 태풍 — 비+바람 합친 태풍 루프(클립 하나로 믹스해 제작)
    WeatherSlip,        // 미끄덩 — 빗물/눈에 미끄러졌을 때 킹받는 소리(PlayerStun.StunSlip)

    // ── 맵 기믹 ── (예고 경보/팡파레는 쓰지 않기로 — 토스트만)
    DdpFloodLoop,       // DDP 수문 물 콸콸 루프(Flowing 동안, 수로 위치 3D)
    LotteParadeMusic,   // 롯월 퍼레이드 행진곡 루프(Running 동안, 선두 카를 따라다니는 3D)
}
