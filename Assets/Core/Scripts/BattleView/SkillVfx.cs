using System.Collections.Generic;
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
    /// <summary>스킬·클래스 시그니처 팔레트 — 시전 VFX와 발밑 클래스 링이 같은 색을 쓴다.</summary>
    public static class SkillHues
    {
        public static readonly Color Steel = new Color(0.75f, 0.85f, 1f);   // 너구리 (밀침·강타)
        public static readonly Color Wind = new Color(0.55f, 0.9f, 1f);     // 질주·비행
        public static readonly Color Sonic = new Color(1f, 0.9f, 0.3f);     // 러너 (비명)
        public static readonly Color Shadow = new Color(0.7f, 0.4f, 1f);    // 검은냥 (점멸)
        public static readonly Color Slash = new Color(1f, 0.35f, 0.7f);    // 할퀴기
        public static readonly Color Flame = new Color(1f, 0.7f, 0.25f);    // 비둘기 (파열탄·폭탄)
        public static readonly Color Muzzle = new Color(1f, 0.95f, 0.7f);   // 까치 (사격)
        public static readonly Color Charge = new Color(1f, 0.45f, 0.35f);  // 저격 차지
    }

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
            // 소속 vs 정체 분리 (2026-09-05 "스킬이펙트가 서로 구분이 안 가"):
            // 팀색 링 한 겹이 "누구 편"을, 스킬 고유색+형태가 "무슨 스킬"을 말한다.
            // 전부 팀색으로 칠했더니 죄다 같은 링·글로우가 됐다 — 고유색 부활. (빨강은 여전히 바닥 위협 예고 전용)
            var teamC = mine ? StrikeVfx.MineNeon : new Color(1f, 0.55f, 0.2f, 0.95f);
            Color Alpha(Color col, float a) { col.a = a; return col; }

            RingWave.Spawn(origin, Alpha(teamC, 0.8f), 2.2f, 0.28f); // 소속 링 — 얇고 빠르게

            if (focus)
                FxQuad.One(VfxTextures.Glow, origin + Vector3.up * 0.7f, Color.Lerp(teamC, Color.white, 0.5f), 3.4f, 0.9f, 0.32f);

            // 스킬 고유색 — 형태와 함께 시그니처를 만든다 (SkillHue와 동기)
            var steel = SkillHues.Steel;
            var wind = SkillHues.Wind;
            var sonic = SkillHues.Sonic;
            var shadow = SkillHues.Shadow;
            var slashC = SkillHues.Slash;
            var flame = SkillHues.Flame;
            var muzzle = SkillHues.Muzzle;
            var chargeC = SkillHues.Charge;
            Color Hi(Color col) { return Color.Lerp(col, Color.white, 0.45f); }

            switch (kind)
            {
                // 색은 팀이 쥐고, 클래스는 '형태'로 갈린다 — 링(밀침) / 기둥(강타) / 줄기(돌파) / 겹링(비명) / 발톱(할퀴기)
                case SkillKind.ShieldPush: // 방패 밀침 — 넓은 링 한 겹이 바깥으로 쿵 (링 총량 다이어트)
                    RingWave.Spawn(origin, Alpha(Hi(steel), 0.95f), 7f, 0.5f);
                    FxQuad.One(VfxTextures.Glow, origin + Vector3.up * 0.4f, steel, 3f, 1f, 0.3f);
                    break;

                case SkillKind.Smash: // 던져버리기 — 붙잡는 순간: 바닥 링 + 파편. 광기둥은 내려찍기로 읽혀 뺐다
                    RingWave.Spawn(origin, Alpha(steel, 0.9f), 5f, 0.5f);
                    FxQuad.Burst(VfxTextures.Spark, origin, Hi(flame), 18, 1.4f, 5f);
                    FxQuad.One(VfxTextures.Glow, origin + Vector3.up * 0.4f, flame, 4f, 0.9f, 0.3f);
                    break;

                case SkillKind.Dash: // 돌파 — 속도 줄기 (카툰 구름 제거, 잔상은 러너가 경로 위에 얹는다)
                    for (int i = 0; i < 4; i++)
                    {
                        float a = i * 1.5708f + 0.4f;
                        var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                        FxQuad.One(VfxTextures.Wind, origin + Vector3.up * 0.4f, Hi(wind), 3.2f, 0.8f, 0.42f,
                            spinDeg: a * Mathf.Rad2Deg, velocity: dir * 7f);
                    }
                    break;

                case SkillKind.Scream: // 비명 교란 — 음파 두 겹 + 시전자 번쩍 (네 겹은 링 홍수였다)
                    FxQuad.One(VfxTextures.Glow, origin + Vector3.up * 0.6f, sonic, 3f, 1.6f, 0.35f);
                    RingWave.Spawn(origin, Alpha(Hi(sonic), 0.95f), 5f, 0.45f);
                    FxSequencer.Delay(0.15f, () => RingWave.Spawn(origin, Alpha(sonic, 0.7f), 8.5f, 0.7f));
                    break;

                case SkillKind.Blink: // 그림자 도약 — 이 origin은 도착지 (시뮬이 먼저 순간이동). 등장: 전기 2연 번쩍
                    FxQuad.One(VfxTextures.Electric, origin + Vector3.up * 0.5f, shadow, 4.2f, 0.7f, 0.4f);
                    FxSequencer.Delay(0.12f, () => FxQuad.One(VfxTextures.Electric, origin + Vector3.up * 0.55f, Hi(shadow), 3.6f, 0.6f, 0.3f, spinDeg: 60f));
                    break;

                case SkillKind.Claw: // 할퀴기 — 발톱 자국 세 장이 시전자 위에서 교차 (판정 칸엔 StrikeVfx가 또 긁는다)
                    FxQuad.One(VfxTextures.Claw, origin + Vector3.up * 0.6f, slashC, 3.6f, 0.4f, 0.35f, spinDeg: -30f);
                    FxSequencer.Delay(0.07f, () => FxQuad.One(VfxTextures.Claw, origin + Vector3.up * 0.7f, Hi(slashC), 4f, 0.4f, 0.35f, spinDeg: 40f));
                    FxSequencer.Delay(0.14f, () => FxQuad.One(VfxTextures.Claw, origin + Vector3.up * 0.55f, slashC, 3.4f, 0.4f, 0.35f, spinDeg: 95f));
                    break;

                case SkillKind.Burst: // 파열탄 — 폭광 + 파편
                    FxQuad.One(VfxTextures.Glow, origin + Vector3.up * 0.4f, flame, 5.5f, 1.1f, 0.5f);
                    FxQuad.Burst(VfxTextures.Spark, origin + Vector3.up * 0.3f, Hi(flame), 14, 1f, 4.6f);
                    break;

                case SkillKind.Snatch: // 낚아채기 — 이륙 도약(폭탄 로프트 아님). 발밑 링만, 발톱은 UnitView 비행 궤적이
                    RingWave.Spawn(origin, Alpha(wind, 0.85f), 3.6f, 0.45f);
                    FxQuad.One(VfxTextures.Wind, origin + Vector3.up * 0.3f, Hi(wind), 2.6f, 0.7f, 0.35f, velocity: Vector3.up * 4f);
                    break;

                case SkillKind.BombDeliver: // 폭탄 배달 — 상승 광구 + 링 (카툰 구름 제거, 비행 연출은 UnitView)
                    FxQuad.One(VfxTextures.Glow, origin + Vector3.up * 0.3f, Hi(flame), 3.4f, 1.3f, 0.75f,
                        velocity: Vector3.up * 2.8f);
                    RingWave.Spawn(origin, Alpha(flame, 0.85f), 4.4f, 0.55f);
                    break;

                case SkillKind.KnockShot: // 밀쳐내기 사격 — 총구 섬광 + 불똥 (흰 먼지 박스 제거)
                    FxQuad.One(VfxTextures.Glow, origin + Vector3.up * 0.55f, muzzle, 3.4f, 0.7f, 0.2f);
                    FxQuad.Burst(VfxTextures.Spark, origin + Vector3.up * 0.5f, muzzle, 10, 0.9f, 4.6f);
                    break;

                case SkillKind.Snipe: // 저격 — 차지 글로우 (0.8초 예고와 맞물리는 긴장). 예고 동안 계속 빛나게 길게
                    FxQuad.One(VfxTextures.Glow, origin + Vector3.up * 0.55f, chargeC, 1.8f, 2.4f, 0.8f);
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

        // 풀 (2026-09-05 성능 패스): 이펙트 대형화로 생성량이 늘어 Instantiate/Destroy 부하가 커졌다.
        // 난전 한 번에 수십 장 — 재사용으로 GC·스파이크를 눌러둔다.
        static readonly Stack<FxQuad> pool = new Stack<FxQuad>();
        const int PoolCap = 128;

        /// <summary>단발. spinDeg = 초기 회전(베기 각도 등), velocity = 월드 이동.</summary>
        public static void One(Material mat, Vector3 pos, Color color, float scale, float scaleGrow, float life,
            float spinDeg = 0f, Vector3 velocity = default)
        {
            if (mat == null) return; // 텍스처 미도착 — 폴백은 호출부의 기존 연출

            FxQuad f = null;
            while (pool.Count > 0 && f == null)
            {
                f = pool.Pop();
                if (f == null) continue; // 씬 전환으로 파괴된 개체 — 버린다 (UnityEngine.Object null 체크)
            }
            if (f == null)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Object.Destroy(go.GetComponent<Collider>());
                go.name = "FxQuad";
                f = go.AddComponent<FxQuad>();
                f.rend = go.GetComponent<Renderer>();
                f.rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                f.rend.receiveShadows = false;
                f.mpb = new MaterialPropertyBlock();
            }

            f.gameObject.SetActive(true);
            f.transform.position = pos;
            f.mat = mat;
            f.rend.sharedMaterial = mat;
            f.color = VfxTextures.Dim(color); // 매트화 — 모든 절차 VFX가 이 통로를 지난다
            f.fromScale = scale;
            f.toScale = scale * (1f + scaleGrow);
            f.life = life;
            f.elapsed = 0f;
            f.spinDeg = spinDeg;
            f.velocity = velocity;
            f.transform.localScale = Vector3.one * scale;
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

        void LateUpdate()
        {
            elapsed += Time.unscaledDeltaTime;
            float k = elapsed / life;
            if (k >= 1f)
            {
                // 풀로 복귀 — 가득이면 진짜 파괴
                gameObject.SetActive(false);
                if (pool.Count < PoolCap) pool.Push(this);
                else Destroy(gameObject);
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
