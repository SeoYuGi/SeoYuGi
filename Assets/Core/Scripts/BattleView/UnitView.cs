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
            ApplyColor(value ? teamColor * 0.35f : teamColor);
        }

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

        void ApplyColor(Color color)
        {
            mpb.SetColor(BaseColorId, color);
            bodyRenderer.SetPropertyBlock(mpb);
        }

        Vector3 WorldOf(Coord c) => gridView.CoordToWorld(c) + Vector3.up * yOffset;
    }
}
