using System.Collections;
using System.Collections.Generic;
using SeoYuGi.Battle;
using UnityEngine;

namespace SeoYuGi.BattleView
{
    /// <summary>유닛 1개 연출: 경로 홉 이동, 선택 표시, 쿨타임 어둡게.</summary>
    public class UnitView : MonoBehaviour
    {
        [SerializeField] Renderer bodyRenderer;   // 비우면 자식에서 자동 탐색
        [SerializeField] float yOffset = 0.5f;    // 큐브 절반 높이
        [SerializeField] float selectedScale = 1.2f;
        [SerializeField] float hopHeight = 0.25f; // 홉 포물선 높이

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public int UnitId { get; private set; }
        public bool IsMoving => moving != null;

        GridView gridView;
        Vector3 baseScale;
        Color teamColor;
        bool dimmed;
        MaterialPropertyBlock mpb;
        Coroutine moving;
        Coroutine hitRoutine;
        Vector3 preHitScale; // 피격 펀치 시작 전 스케일 — 중첩 피격 시 복원 기준
        Quaternion preHitRot = Quaternion.identity; // 리코일 틸트 복원 기준

        public void Bind(int unitId, Color teamColor, GridView gridView, Coord start)
        {
            UnitId = unitId;
            this.gridView = gridView;
            this.teamColor = teamColor;
            baseScale = transform.localScale;
            mpb = new MaterialPropertyBlock();

            if (bodyRenderer == null) bodyRenderer = GetComponentInChildren<Renderer>();
            // 클릭 레이캐스트용 콜라이더가 프리팹에 없으면 자동 추가
            if (GetComponentInChildren<Collider>() == null)
                gameObject.AddComponent<BoxCollider>();

            ApplyColor(teamColor);
            transform.position = WorldOf(start);
            CreateFootDisc(teamColor);
        }

        /// <summary>발밑 팀색 네온 원반 — 유닛을 바닥에서 띄워 보이게 하고 팀이 한눈에 읽히게. 스킨이 뭐든 공통.</summary>
        void CreateFootDisc(Color color)
        {
            var mat = VfxTextures.Glow;
            if (mat == null) return;
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(go.GetComponent<Collider>()); // 클릭 레이캐스트 방해 금지
            go.name = "FootDisc";
            go.transform.SetParent(transform, false);
            // 루트 스케일(클래스별 덩치)을 되돌려 월드 기준 크기·높이로 — 바닥 타일 윗면(+0.05) 바로 위
            go.transform.localScale = Vector3.one * (1.15f / Mathf.Max(0.01f, baseScale.x));
            go.transform.localPosition = new Vector3(0f, (-yOffset + 0.08f) / Mathf.Max(0.01f, baseScale.y), 0f);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            var block = new MaterialPropertyBlock();
            var c = color * 0.38f; c.a = 1f; // 가산 — 색 세기로 밝기 조절 (2026-09-05 매트화: 0.7은 여섯 유닛이 상시 발광해 화면을 태웠다)
            block.SetColor(BaseColorId, c);
            block.SetColor("_Color", c);
            r.SetPropertyBlock(block);
        }

        public void SetSelected(bool selected)
        {
            transform.localScale = selected ? baseScale * selectedScale : baseScale;
        }

        /// <summary>쿨타임 중 시각 표시(어둡게).</summary>
        public void SetDimmed(bool value)
        {
            if (dimmed == value) return;
            dimmed = value;
            if (hitRoutine == null) ApplyColor(CurrentColor); // 피격 플래시 중엔 코루틴이 색을 쥔다
        }

        Color CurrentColor => dimmed ? teamColor * 0.35f : teamColor;

        /// <summary>피격 연출: 흰 번쩍 + 펀치 스케일.</summary>
        /// <param name="hitDir">타격이 밀어내는 월드 방향 — 리코일 틸트. Zero면 틸트 없음.</param>
        public void PlayHit(Vector3 hitDir = default)
        {
            if (!gameObject.activeInHierarchy) return; // 시야 밖 — 연출 생략
            if (hitRoutine != null) StopCoroutine(hitRoutine);
            else
            {
                preHitScale = transform.localScale; // 연타 피격 시 이미 커진 스케일로 기준 오염 방지
                preHitRot = transform.rotation;
            }
            hitRoutine = StartCoroutine(HitRoutine(hitDir));
        }

        IEnumerator HitRoutine(Vector3 hitDir)
        {
            // 리코일: 타격 방향으로 상체가 훅 기울었다 복귀 — 위치는 안 건드려 이동 로직과 무충돌
            var tiltRot = preHitRot;
            if (hitDir.sqrMagnitude > 0.01f)
            {
                var axis = Vector3.Cross(Vector3.up, hitDir.normalized);
                tiltRot = Quaternion.AngleAxis(-16f, axis) * preHitRot;
            }

            ApplyColor(Color.white);
            transform.localScale = preHitScale * 1.25f;
            transform.rotation = tiltRot;
            yield return new WaitForSeconds(0.08f);

            for (float t = 0f; t < 0.15f; t += Time.deltaTime)
            {
                float k = t / 0.15f;
                ApplyColor(Color.Lerp(Color.white, CurrentColor, k));
                transform.localScale = Vector3.Lerp(preHitScale * 1.25f, preHitScale, k);
                transform.rotation = Quaternion.Slerp(tiltRot, preHitRot, k);
                yield return null;
            }
            ApplyColor(CurrentColor);
            transform.localScale = preHitScale;
            transform.rotation = preHitRot;
            hitRoutine = null;
        }

        /// <summary>즉시 위치 동기화 (점멸·시야 재등장 등 순간이동이 맞는 경우).</summary>
        public void SnapTo(Coord c) => transform.position = WorldOf(c);

        /// <summary>진행 중인 이동 연출 중단 — 점멸 등 순간이동이 걷기를 덮어써야 할 때.</summary>
        public void CancelMove()
        {
            if (moving != null) StopCoroutine(moving);
            moving = null;
        }

        /// <summary>폭탄 배달 왕복 비행 — 떠올라 목표까지 날아가 폭탄을 놓고 원위치로 복귀.
        /// 시뮬 위치는 출발 칸 그대로 — 연출 내내 moving으로 잠가 SyncPresentation 간섭 차단.</summary>
        public void PlayBombFlight(Vector3 targetWorld, float outDuration, float backDuration)
        {
            if (moving != null) StopCoroutine(moving);
            moving = StartCoroutine(BombFlightRoutine(targetWorld + Vector3.up * yOffset, outDuration, backDuration));
        }

        IEnumerator BombFlightRoutine(Vector3 target, float outDuration, float backDuration)
        {
            Vector3 home = transform.position;
            const float height = 1.5f;
            float nextPuff = 0f;

            for (float t = 0f; t < outDuration; t += Time.deltaTime)
            {
                float k = t / outDuration;
                var p = Vector3.Lerp(home, target, Mathf.SmoothStep(0f, 1f, k));
                p.y += height * Mathf.SmoothStep(0f, 1f, Mathf.Min(k * 2.5f, 1f)); // 초반 급상승 후 순항
                transform.position = p;
                if (t >= nextPuff) // 슝슝 — 바람 줄기 궤적
                {
                    nextPuff = t + 0.12f;
                    FxQuad.One(VfxTextures.Wind, p + Vector3.up * 0.1f, new Color(0.8f, 0.9f, 1f),
                        0.9f, 0.4f, 0.3f, velocity: (home - target).normalized * 2.5f);
                }
                yield return null;
            }

            for (float t = 0f; t < backDuration; t += Time.deltaTime)
            {
                float k = t / backDuration;
                var p = Vector3.Lerp(target, home, Mathf.SmoothStep(0f, 1f, k));
                p.y += height * Mathf.SmoothStep(0f, 1f, Mathf.Min((1f - k) * 2.5f, 1f)); // 막판 하강
                transform.position = p;
                yield return null;
            }

            transform.position = home;
            moving = null;
        }

        /// <summary>빠른 미끄러짐 (대시·밀침) — 순간이동처럼 안 보이게.</summary>
        public void PlaySlide(Coord dest, float duration = 0.12f)
        {
            if (moving != null) StopCoroutine(moving);
            moving = StartCoroutine(SlideRoutine(WorldOf(dest), duration));
        }

        IEnumerator SlideRoutine(Vector3 b, float duration)
        {
            Vector3 a = transform.position;
            var fromRot = transform.rotation;
            var toRot = FacingFor(a, b);
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                float k = t / duration;
                transform.position = Vector3.Lerp(a, b, k);
                transform.rotation = Quaternion.Slerp(fromRot, toRot, Mathf.Clamp01(k * 3f));
                yield return null;
            }
            transform.position = b;
            moving = null;
        }

        /// <summary>현재 표시 위치가 해당 칸과 어긋나 있는가.</summary>
        public bool IsAt(Coord c) => (transform.position - WorldOf(c)).sqrMagnitude < 0.0001f;

        /// <summary>경로를 칸 단위 홉으로 재생. 재생 중 새 경로가 오면 기존 것 중단 후 이어감.</summary>
        public void PlayPath(IReadOnlyList<Coord> path, float hopDuration)
        {
            if (dying != null) return;
            if (moving != null) StopCoroutine(moving);
            moving = StartCoroutine(PathRoutine(path, hopDuration));
        }

        // ── 타격감 2차 패스 (2026-09-05) ─────────────────────

        Coroutine dying;
        /// <summary>사망 연출 중 — SyncPresentation이 이 동안 뷰를 살려둔다.</summary>
        public bool IsDying => dying != null;

        /// <summary>격파 — 맞은 방향으로 쓰러지며 가라앉는다. '펑' 사라지는 것보다 죽음이 읽힌다.</summary>
        public void PlayDeath(Vector3 hitDir)
        {
            if (dying != null) return;
            if (moving != null) { StopCoroutine(moving); moving = null; }
            dying = StartCoroutine(DeathRoutine(hitDir));
        }

        IEnumerator DeathRoutine(Vector3 hitDir)
        {
            var start = transform.position;
            var rot0 = transform.rotation;
            if (hitDir.sqrMagnitude < 0.01f) hitDir = transform.forward;
            var axis = Vector3.Cross(Vector3.up, hitDir.normalized);
            const float Dur = 0.5f;
            for (float t = 0f; t < Dur; t += Time.deltaTime)
            {
                float k = t / Dur;
                transform.rotation = Quaternion.AngleAxis(80f * Mathf.SmoothStep(0f, 1f, k), axis) * rot0;
                transform.position = start + Vector3.down * (0.35f * k * k);
                yield return null;
            }
            dying = null; // 다음 프레임 SyncPresentation이 숨긴다
            transform.rotation = rot0;
            transform.position = start;
        }

        /// <summary>공격 시전 런지 — 타겟 쪽으로 훅 갔다 돌아온다. "누가 때렸는지"가 몸짓으로 읽힌다.</summary>
        public void PlayLunge(Vector3 towardWorld)
        {
            if (dying != null || moving != null) return;
            moving = StartCoroutine(LungeRoutine(towardWorld)); // moving 슬롯 공유 — 연출 중 스냅 방지
        }

        IEnumerator LungeRoutine(Vector3 target)
        {
            var a = transform.position;
            var dir = target - a;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) { moving = null; yield break; }
            var b = a + dir.normalized * 0.25f;
            for (float t = 0f; t < 0.07f; t += Time.deltaTime) { transform.position = Vector3.Lerp(a, b, t / 0.07f); yield return null; }
            for (float t = 0f; t < 0.12f; t += Time.deltaTime) { transform.position = Vector3.Lerp(b, a, t / 0.12f); yield return null; }
            transform.position = a;
            moving = null;
        }

        [SerializeField] float facingYawOffset = 0f; // 모델 정면이 +Z가 아니면 여기서 보정 (예: 180)

        /// <summary>진행 방향을 바라보는 회전 — 옆으로 걸을 때 정면만 보던 문제. 수직 성분 무시.</summary>
        Quaternion FacingFor(Vector3 from, Vector3 to)
        {
            var dir = to - from;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return transform.rotation;
            return Quaternion.LookRotation(dir.normalized, Vector3.up) * Quaternion.Euler(0f, facingYawOffset, 0f);
        }

        IEnumerator PathRoutine(IReadOnlyList<Coord> path, float hopDuration)
        {
            foreach (var cell in path)
            {
                Vector3 a = transform.position;
                Vector3 b = WorldOf(cell);
                var fromRot = transform.rotation;
                var toRot = FacingFor(a, b);
                for (float t = 0f; t < hopDuration; t += Time.deltaTime)
                {
                    float k = t / hopDuration;
                    var p = Vector3.Lerp(a, b, k);
                    p.y += hopHeight * 4f * k * (1f - k); // 포물선
                    transform.position = p;
                    transform.rotation = Quaternion.Slerp(fromRot, toRot, Mathf.Clamp01(k * 2.5f)); // 홉 초반에 몸을 돌린다
                    yield return null;
                }
                transform.position = b;
            }

            // 착지 스쿼시 — 마지막 홉이 바닥에 '톡' 닿는 맛 (타격감 2차 패스)
            var s0 = transform.localScale;
            var squash = new Vector3(s0.x * 1.08f, s0.y * 0.86f, s0.z * 1.08f);
            for (float t = 0f; t < 0.09f; t += Time.deltaTime)
            {
                transform.localScale = Vector3.Lerp(squash, s0, t / 0.09f);
                yield return null;
            }
            transform.localScale = s0;
            moving = null;
        }

        void OnDisable()
        {
            // 시야에서 숨겨질 때 코루틴이 강제 종료됨 — IsMoving이 영구 true로 남지 않게
            moving = null;
            if (hitRoutine != null)
            {
                // 플래시 도중 숨겨짐 — 스케일·색·회전 원복
                transform.localScale = preHitScale;
                transform.rotation = preHitRot;
                ApplyColor(CurrentColor);
                hitRoutine = null;
            }
        }

        void ApplyColor(Color color)
        {
            mpb.SetColor(BaseColorId, color);
            bodyRenderer.SetPropertyBlock(mpb);
        }

        Vector3 WorldOf(Coord c) => gridView.CoordToWorld(c) + Vector3.up * yOffset;
    }
}
