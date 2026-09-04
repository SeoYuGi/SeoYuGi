# 인터페이스 계약 — 코어 틀 ↔ 학습 AI 모듈

> 목적: 코어(격자/전투/슬롯)와 예측 AI를 병렬 개발하기 위한 경계면 합의.
> AI 모듈은 `Assets/Game/Scripts/Prediction/`에 있고 **UnityEngine에 의존하지 않는 순수 C#**이다.
> 코어 쪽에서 아래 3가지만 지켜주면 나머지는 서로 안 건드려도 된다.

## 1. 좌표
- 격자는 `(x, y)` 정수 좌표. 원점 좌하단, x→오른쪽, y→위. 맵 9×9.
- AI 모듈은 자체 `Cell` 구조체(int x, y)를 쓴다. `Vector2Int`와의 변환은 코어 쪽 어댑터에서.

## 2. 코어 → AI : 행동 이벤트 (관찰)
모든 캐릭터의 확정된 행동마다 1회 호출:

```csharp
predictor.Observe(new ActionEvent {
    ActorId  = 3,                 // 슬롯 고유 id (0~5)
    Team     = TeamId.Human,      // Human / Machine
    Type     = ActionType.Move,   // Move / Attack / Heavy / Guard / Decoy
    From     = new Cell(4, 0),    // 행동 전 위치
    To       = new Cell(4, 1),    // 이동 목적지 or 공격 지정 칸 (Guard면 From과 동일)
    Time     = 12.3f,             // 라운드 시작 기준 초
});
```

- 예고만 되고 아직 판정 안 된 공격도 "지정한 순간" 보낸다 (의도가 학습 재료).
- 디코이 사용 시 코어는 `predictor.InjectDecoy(actorId, durationSec)`를 호출한다.

## 3. AI → 코어 : 예측 조회
```csharp
// 다음 이동 예측 top-N (히트맵 UI + 적 AI 조준에 사용)
IReadOnlyList<CellProb> preds = predictor.PredictNextCells(actorId, topN: 3);
// CellProb { Cell Cell; float Prob; }  Prob 합 ≤ 1

// 라운드 전환 시
predictor.SetRound(2);                    // 1=관찰만, 2=빈도, 3=조건부
string[] lines = predictor.GetBriefing(actorId);  // 브리핑 화면용 분석 문구
```

- **적팀 행동 결정(AIBrain)은 코어 소관.** AI 모듈은 "어디로 갈 확률" 데이터만 준다.
  AIBrain이 `PredictNextCells`를 조준에 쓰는 방식 권장 (예: R2부터 강공격을 top-1 칸에 설치).

## 4. 슬롯 구조 (권장)
- 캐릭터는 입력을 `IController`에서 받는다: `HumanLocal` / `AIBrain` / (스트레치) `NetworkRemote`.
- 이렇게 하면 AI 백필·로컬 2P·온라인이 전부 "슬롯에 다른 컨트롤러 꽂기"로 끝난다.

## 5. AI 뇌 (AiBrain) 통합
`Assets/Game/Scripts/Ai/` — 적팀 3기와 아군 백필 팀원이 전부 쓰는 조종 뇌. 순수 C#.

```csharp
// 슬롯 세팅 (매치 시작 시)
var predictor = new Predictor(predCfg);            // 매치당 1개, 적팀 뇌들이 공유
var enemyBrain = new AiBrain(actorId, AiConfig.ForClass(ClassId.Sniper), predictor);
var allyBrain  = new AiBrain(actorId, AiConfig.ForClass(ClassId.Runner));  // 아군 팀원: predictor 없음

// 매 프레임 (코어가 IWorldView 구현체를 넘긴다)
AiCommand cmd = brain.Tick(worldView);
if (cmd.Type != CommandType.None) ExecuteCommand(actorId, cmd); // AP 차감·실행은 코어 소관
```

- `IWorldView`(WorldView.cs)는 코어가 구현: 액터 상태·존 소유·예고 목록·AP 조회·통행 판정.
- AiBrain은 명령만 내놓고 **AP 차감·쿨타임·판정은 전부 코어가 집행** — AP 부족이면 코어가 무시해도 안전.
- 난이도: `AiConfig.AggressionDelay`(반응 지연)와 `PredictionConfig` 가중치 두 개만 만지면 됨.

## 6. 파일 소유권 (머지 충돌 방지)
- `Assets/Game/Scripts/Prediction/`, `Assets/Game/Scripts/Ai/` — AI 담당 (이쪽)
- 그 외 `Assets/Game/` 전부 — 코어 담당 (친구)
- 공유 타입(`ActionEvent`, `Cell`, `IWorldView` 등)은 위 두 폴더에 있고 코어가 참조·구현만 한다. 수정 필요하면 말하고 고치기.
