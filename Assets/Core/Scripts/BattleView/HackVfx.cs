using System.Collections;
using UnityEngine;

namespace SeoYuGi.BattleView
{
    /// <summary>
    /// 해킹 발동 연출 — 매치 1회뿐인 예측 카운터 장비(기획서 '해킹', 구 디코이)의 시각 언어.
    /// 시전자에서 시안 링이 3연발로 퍼지고, 교란 지속 내내 화면이 지직거린다.
    /// 코드 생성 (프리팹·씬 배선 불필요).
    /// </summary>
    public static class HackVfx
    {
        static readonly Color Cyan = new Color(0.35f, 0.95f, 1f, 0.9f);

        /// <summary>origin = 시전자 발밑. seconds = 예측 교란 지속(HackSystem.Duration).</summary>
        public static void Play(MonoBehaviour host, Vector3 origin, float seconds)
        {
            ImpactFx.SetGlitch(seconds);
            CameraShaker.Shake(0.32f);
            if (host != null && host.isActiveAndEnabled) host.StartCoroutine(Rings(origin));
            else RingWave.Spawn(origin, Cyan, 4.5f, 0.65f); // 코루틴 못 돌리면 1발이라도
        }

        static IEnumerator Rings(Vector3 origin)
        {
            for (int i = 0; i < 3; i++)
            {
                RingWave.Spawn(origin, Cyan, 4.5f, 0.65f);
                yield return new WaitForSecondsRealtime(0.12f);
            }
        }
    }
}
