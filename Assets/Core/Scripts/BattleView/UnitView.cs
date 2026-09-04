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

        /// <summary>빠른 미끄러짐 (대시·밀침) — 순간이동처럼 안 보이게.</summary>
        public void PlaySlide(Coord dest, float duration = 0.12f)
        {
            if (moving != null) StopCoroutine(moving);
            moving = StartCoroutine(SlideRoutine(WorldOf(dest), duration));
        }

        IEnumerator SlideRoutine(Vector3 b, float duration)
        {
            Vector3 a = transform.position;
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                transform.position = Vector3.Lerp(a, b, t / duration);
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
            if (moving != null) StopCoroutine(moving);
            moving = StartCoroutine(PathRoutine(path, hopDuration));
        }

        IEnumerator PathRoutine(IReadOnlyList<Coord> path, float hopDuration)
        {
            foreach (var cell in path)
            {
                Vector3 a = transform.position;
                Vector3 b = WorldOf(cell);
                for (float t = 0f; t < hopDuration; t += Time.deltaTime)
                {
                    float k = t / hopDuration;
                    var p = Vector3.Lerp(a, b, k);
                    p.y += hopHeight * 4f * k * (1f - k); // 포물선
                    transform.position = p;
                    yield return null;
                }
                transform.position = b;
            }
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
