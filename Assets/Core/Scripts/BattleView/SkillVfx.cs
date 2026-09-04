using SeoYuGi.Battle;
using UnityEngine;

namespace SeoYuGi.BattleView
{
    /// <summary>
    /// 스킬별 시전 연출 — 캐릭터/스킬 특성에 맞는 색·형태 (기획 v1.7 스킬 10종).
    /// 생성 텍스처(VfxTextures)가 있으면 가산 빌보드, 없으면 기존 코드 연출로 폴백.
    ///
    /// 너구리: ShieldPush=강철 확산 / Smash=주황 파쇄
    /// 러너:   Dash=후방 불똥 / Scream=노랑 음파 3연
    /// 검은냥: Blink=보라 전기 / Claw=적색 삼연 베기
    /// 비둘기: Burst=주황 폭광 / BombDeliver=시안 상승 광구
    /// 까치:   KnockShot=총구 섬광 / Snipe=적색 차지 글로우
    /// </summary>
    public static class SkillVfx
    {
        public static void Cast(SkillKind kind, Vector3 origin)
        {
            switch (kind)
            {
                case SkillKind.ShieldPush: // 방패 밀침 — 단단한 강철빛 확산
                    RingWave.Spawn(origin, new Color(0.75f, 0.82f, 0.9f, 0.85f), 2.2f, 0.35f);
                    FxQuad.Burst(VfxTextures.Spark, origin, new Color(0.8f, 0.85f, 1f), 4, 0.5f, 2.2f);
                    break;

                case SkillKind.Smash: // 강타 — 무겁게 내려찍는 주황 파쇄
                    FxQuad.One(VfxTextures.Glow, origin + Vector3.up * 0.3f, new Color(1f, 0.55f, 0.2f), 2.4f, 0.9f, 0.3f);
                    FxQuad.Burst(VfxTextures.Spark, origin, new Color(1f, 0.6f, 0.25f), 6, 0.8f, 3f);
                    RingWave.Spawn(origin, new Color(1f, 0.6f, 0.2f, 0.8f), 2.6f, 0.4f);
                    break;

                case SkillKind.Dash: // 돌파 — 뒤로 흩날리는 불똥
                    FxQuad.Burst(VfxTextures.Spark, origin + Vector3.up * 0.4f, new Color(1f, 0.8f, 0.4f), 5, 0.6f, 4f);
                    break;

                case SkillKind.Scream: // 비명 교란 — 노랑 음파가 세 겹으로 퍼짐 (스턴의 시각 언어)
                    RingWave.Spawn(origin, new Color(1f, 0.85f, 0.3f, 0.9f), 2.4f, 0.35f);
                    RingWave.Spawn(origin, new Color(1f, 0.85f, 0.3f, 0.6f), 3.2f, 0.55f);
                    RingWave.Spawn(origin, new Color(1f, 0.85f, 0.3f, 0.35f), 4f, 0.75f);
                    break;

                case SkillKind.Blink: // 그림자 도약 — 보라 전기 찢김 (기존 보라 CellFlash 위에 얹힘)
                    FxQuad.One(VfxTextures.Electric, origin + Vector3.up * 0.5f, new Color(0.75f, 0.45f, 1f), 1.8f, 0.5f, 0.25f);
                    break;

                case SkillKind.Claw: // 할퀴기 — 적색 삼연 베기 (기울어진 불똥 세 줄)
                    for (int i = 0; i < 3; i++)
                        FxQuad.One(VfxTextures.Spark, origin + Vector3.up * (0.35f + i * 0.18f),
                            new Color(1f, 0.25f, 0.2f), 1.5f, 0.4f, 0.3f, spinDeg: -35f + i * 18f);
                    break;

                case SkillKind.Burst: // 파열탄 — 주황 폭광
                    FxQuad.One(VfxTextures.Glow, origin + Vector3.up * 0.4f, new Color(1f, 0.5f, 0.15f), 2.8f, 1f, 0.35f);
                    break;

                case SkillKind.BombDeliver: // 폭탄 배달 — 시안 광구가 떠오름 (비행 개시)
                    FxQuad.One(VfxTextures.Glow, origin + Vector3.up * 0.3f, new Color(0.4f, 0.9f, 1f), 1.6f, 1.2f, 0.6f,
                        velocity: Vector3.up * 2.2f);
                    RingWave.Spawn(origin, new Color(0.4f, 0.9f, 1f, 0.7f), 2f, 0.4f);
                    break;

                case SkillKind.KnockShot: // 밀쳐내기 사격 — 짧은 총구 섬광
                    FxQuad.One(VfxTextures.Glow, origin + Vector3.up * 0.55f, new Color(1f, 0.9f, 0.6f), 1.4f, 0.5f, 0.12f);
                    FxQuad.Burst(VfxTextures.Spark, origin + Vector3.up * 0.5f, new Color(1f, 0.85f, 0.5f), 3, 0.4f, 3f);
                    break;

                case SkillKind.Snipe: // 저격 — 적색 차지 글로우 (0.8초 예고와 맞물리는 긴장)
                    FxQuad.One(VfxTextures.Glow, origin + Vector3.up * 0.55f, new Color(1f, 0.2f, 0.15f), 0.6f, 1.6f, 0.7f);
                    break;
            }
        }
    }

    /// <summary>
    /// 가산 텍스처 빌보드 쿼드 1장 — 크기 보간 + 페이드 후 소멸. 텍스처 없으면 조용히 생략.
    /// 파티클 시스템보다 가볍고, 생성 텍스처의 톤을 그대로 살린다.
    /// </summary>
    public class FxQuad : MonoBehaviour
    {
        Material mat;
        Color color;
        float fromScale, toScale, life, elapsed, spinDeg;
        Vector3 velocity;
        Renderer rend;
        MaterialPropertyBlock mpb;

        /// <summary>단발. spinDeg = 초기 회전(베기 각도 등), velocity = 월드 이동.</summary>
        public static void One(Material mat, Vector3 pos, Color color, float scale, float scaleGrow, float life,
            float spinDeg = 0f, Vector3 velocity = default)
        {
            if (mat == null) return; // 텍스처 미도착 — 폴백은 호출부의 기존 연출
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(go.GetComponent<Collider>());
            go.name = "FxQuad";
            go.transform.position = pos;

            var f = go.AddComponent<FxQuad>();
            f.mat = mat;
            f.color = color;
            f.fromScale = scale;
            f.toScale = scale * (1f + scaleGrow);
            f.life = life;
            f.spinDeg = spinDeg;
            f.velocity = velocity;
        }

        /// <summary>여러 장을 무작위 방향으로 흩뿌림 — 파편 버스트.</summary>
        public static void Burst(Material mat, Vector3 pos, Color color, int count, float scale, float speed)
        {
            if (mat == null) return;
            for (int i = 0; i < count; i++)
            {
                float a = (i / (float)count) * Mathf.PI * 2f + (i * 0.7f);
                var dir = new Vector3(Mathf.Cos(a), 0.4f + (i % 3) * 0.25f, Mathf.Sin(a)).normalized;
                One(mat, pos + Vector3.up * 0.3f, color, scale, 0.4f, 0.35f,
                    spinDeg: a * Mathf.Rad2Deg, velocity: dir * speed);
            }
        }

        void Start()
        {
            rend = GetComponent<Renderer>();
            rend.sharedMaterial = mat;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
            mpb = new MaterialPropertyBlock();
            transform.localScale = Vector3.one * fromScale;
        }

        void LateUpdate()
        {
            elapsed += Time.unscaledDeltaTime;
            float k = elapsed / life;
            if (k >= 1f)
            {
                Destroy(gameObject);
                return;
            }

            transform.position += velocity * Time.unscaledDeltaTime;
            transform.localScale = Vector3.one * Mathf.Lerp(fromScale, toScale, k);

            // 카메라 빌보드 + 초기 스핀 유지
            if (Camera.main != null)
                transform.rotation = Camera.main.transform.rotation * Quaternion.Euler(0f, 0f, spinDeg);

            var c = color * (1f - k * k); // 가산 — 색 자체를 줄이면 페이드
            c.a = 1f;
            mpb.SetColor("_BaseColor", c);
            mpb.SetColor("_Color", c);
            rend.SetPropertyBlock(mpb);
        }
    }
}
