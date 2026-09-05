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
        /// <summary>
        /// mine = 내 팀 시전. 색은 전부 팀 네온(내 팀 틸 / 적 레드-오렌지) — 클래스별 고유색을 버리고
        /// "누구 편 스킬인가"가 색 하나로 읽히게 (2026-09-05 색 통일). 형태(링·버스트·글로우)가 클래스를 말한다.
        /// </summary>
        /// <summary>focus = 내 유닛의 시전 (러너가 판단). 공통 대형 신호는 focus에만 —
        /// 봇 6기 시전마다 섬광+링을 터뜨리면 화면이 번쩍임의 바다가 된다 (가시성 패스 2026-09-05).</summary>
        public static void Cast(SkillKind kind, Vector3 origin, bool mine, bool focus = false)
        {
            // 색 언어: 빨강은 바닥 위협 예고 전용 — 적 시전은 팀색 주황으로 분리 (빨강 오염이 "뭐가 위험인지"를 망쳤다)
            var c = mine ? StrikeVfx.MineNeon : new Color(1f, 0.55f, 0.2f, 0.95f);
            var hi = Color.Lerp(c, Color.white, 0.55f);
            Color Alpha(Color col, float a) { col.a = a; return col; }

            if (focus)
            {
                // 내 시전만 공통 대형 신호 — "내가 지금 스킬 썼다"는 몸으로 느껴야 하니까
                FxQuad.One(VfxTextures.Glow, origin + Vector3.up * 0.7f, hi, 2.4f, 0.9f, 0.32f);
                RingWave.Spawn(origin, Alpha(hi, 0.9f), 1.8f, 0.3f);
            }

            switch (kind)
            {
                // 색은 팀이 쥐고, 클래스는 '형태'로 갈린다 — 링(밀침) / 기둥(강타) / 줄기(돌파) / 겹링(비명) / 발톱(할퀴기)
                case SkillKind.ShieldPush: // 방패 밀침 — 넓은 링 한 겹이 바깥으로 쿵 (링 총량 다이어트)
                    RingWave.Spawn(origin, Alpha(hi, 0.95f), 4.6f, 0.45f);
                    break;

                case SkillKind.Smash: // 강타 — 하늘에서 내리꽂는 광기둥 + 바닥 파쇄 링 + 파편
                    ImpactVfx.Pillar(origin, hi);
                    RingWave.Spawn(origin, Alpha(c, 0.9f), 3f, 0.45f);
                    FxQuad.Burst(VfxTextures.Spark, origin, hi, 12, 1f, 3.6f);
                    break;

                case SkillKind.Dash: // 돌파 — 먼지 구름 + 사방으로 길게 뻗는 속도 줄기 (잔상은 러너가 경로 위에 얹는다)
                    VfxLibrary.Spawn(VfxLibrary.ToonPoofClouds, origin + Vector3.up * 0.15f, 1.8f, 0.6f);
                    for (int i = 0; i < 4; i++)
                    {
                        float a = i * 1.5708f + 0.4f;
                        var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                        FxQuad.One(VfxTextures.Wind, origin + Vector3.up * 0.4f, hi, 2f, 0.7f, 0.38f,
                            spinDeg: a * Mathf.Rad2Deg, velocity: dir * 5.5f);
                    }
                    break;

                case SkillKind.Scream: // 비명 교란 — 음파 두 겹 + 시전자 번쩍 (네 겹은 링 홍수였다)
                    FxQuad.One(VfxTextures.Glow, origin + Vector3.up * 0.6f, hi, 1.8f, 1.6f, 0.3f);
                    RingWave.Spawn(origin, Alpha(hi, 0.95f), 3f, 0.4f);
                    FxSequencer.Delay(0.15f, () => RingWave.Spawn(origin, Alpha(c, 0.6f), 5.5f, 0.65f));
                    break;

                case SkillKind.Blink: // 그림자 도약 — 이 origin은 도착지 (시뮬이 먼저 순간이동). 등장: 검은 연기 찢고 나타남
                    VfxLibrary.Spawn(VfxLibrary.ToonPoofDark, origin + Vector3.up * 0.3f, 1.8f, 0.75f);
                    FxQuad.One(VfxTextures.Electric, origin + Vector3.up * 0.5f, c, 2.6f, 0.6f, 0.4f);
                    FxSequencer.Delay(0.12f, () => FxQuad.One(VfxTextures.Electric, origin + Vector3.up * 0.55f, hi, 2.2f, 0.5f, 0.3f, spinDeg: 60f));
                    break;

                case SkillKind.Claw: // 할퀴기 — 발톱 자국 세 장이 시전자 위에서 교차 (판정 칸엔 StrikeVfx가 또 긁는다)
                    FxQuad.One(VfxTextures.Claw, origin + Vector3.up * 0.6f, c, 2.3f, 0.35f, 0.35f, spinDeg: -30f);
                    FxSequencer.Delay(0.07f, () => FxQuad.One(VfxTextures.Claw, origin + Vector3.up * 0.65f, hi, 2.5f, 0.35f, 0.35f, spinDeg: 40f));
                    FxSequencer.Delay(0.14f, () => FxQuad.One(VfxTextures.Claw, origin + Vector3.up * 0.55f, c, 2.2f, 0.35f, 0.35f, spinDeg: 95f));
                    break;

                case SkillKind.Burst: // 파열탄 — 폭광 + 파편
                    FxQuad.One(VfxTextures.Glow, origin + Vector3.up * 0.4f, c, 3.6f, 1.1f, 0.45f);
                    FxQuad.Burst(VfxTextures.Spark, origin + Vector3.up * 0.3f, hi, 8, 0.7f, 3.4f);
                    break;

                case SkillKind.BombDeliver: // 폭탄 배달 — 이륙 돌풍 + 상승 광구 (비행 연출은 UnitView)
                    VfxLibrary.Spawn(VfxLibrary.ToonPoofClouds, origin + Vector3.up * 0.15f, 1.9f, 0.7f);
                    FxQuad.One(VfxTextures.Glow, origin + Vector3.up * 0.3f, hi, 2.2f, 1.3f, 0.75f,
                        velocity: Vector3.up * 2.4f);
                    RingWave.Spawn(origin, Alpha(c, 0.8f), 2.8f, 0.5f);
                    break;

                case SkillKind.KnockShot: // 밀쳐내기 사격 — 샷건 흰 먼지 펑 + 총구 섬광
                    VfxLibrary.Spawn(VfxLibrary.ToonPoof, origin + Vector3.up * 0.35f, 1.9f, 0.8f);
                    FxQuad.One(VfxTextures.Glow, origin + Vector3.up * 0.55f, hi, 2.2f, 0.6f, 0.18f);
                    FxQuad.Burst(VfxTextures.Spark, origin + Vector3.up * 0.5f, hi, 6, 0.6f, 3.4f);
                    break;

                case SkillKind.Snipe: // 저격 — 차지 글로우 (0.8초 예고와 맞물리는 긴장). 예고 동안 계속 빛나게 길게
                    FxQuad.One(VfxTextures.Glow, origin + Vector3.up * 0.55f, c, 1f, 2f, 0.8f);
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
            f.color = VfxTextures.Dim(color); // 매트화 — 모든 절차 VFX가 이 통로를 지난다
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
