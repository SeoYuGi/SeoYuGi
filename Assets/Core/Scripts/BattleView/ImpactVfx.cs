using UnityEngine;

namespace SeoYuGi.BattleView
{
    /// <summary>
    /// 타격 월드 VFX — 코드 생성 (프리팹·씬 배선 불필요).
    /// Sparks: 피격 파편 버스트 — 기계=전기 스파크(시안), 동물=먼지(회갈색).
    /// Pillar: 판정 순간 세로 섬광 기둥 — 예고→판정 텐션의 해소 프레임.
    /// </summary>
    public static class ImpactVfx
    {
        static Material particleMat;

        static Material Mat
        {
            get
            {
                if (particleMat == null)
                    particleMat = new Material(Shader.Find("Sprites/Default"));
                return particleMat;
            }
        }

        public static void Sparks(Vector3 pos, bool machine, float scale = 1f)
        {
            var go = new GameObject("Sparks");
            go.transform.position = pos + Vector3.up * 0.45f;

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); // playOnAwake 재생 중엔 duration 세팅 불가
            var main = ps.main;
            main.duration = 0.3f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.38f);
            main.startSpeed = machine
                ? new ParticleSystem.MinMaxCurve(3f * scale, 5.5f * scale)
                : new ParticleSystem.MinMaxCurve(1.2f * scale, 2.5f * scale);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.13f * scale);
            main.gravityModifier = machine ? 1.6f : 0.5f;
            main.startColor = machine // 매트화 — 기계 전기 스파크의 흰 끝점이 제일 번쩍였다
                ? new ParticleSystem.MinMaxGradient(VfxTextures.Dim(new Color(0.6f, 0.95f, 1f)), VfxTextures.Dim(Color.white))
                : new ParticleSystem.MinMaxGradient(new Color(0.55f, 0.47f, 0.38f), new Color(0.35f, 0.3f, 0.25f));
            main.maxParticles = 48;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)(machine ? 16 * scale : 11 * scale)) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.12f;

            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            colorOverLifetime.color = grad;

            // 기계=전기 아크, 동물=불똥 궤적 텍스처 — 없으면 기존 민무늬 폴백
            var texMat = machine ? VfxTextures.Electric : VfxTextures.Spark;
            ps.GetComponent<ParticleSystemRenderer>().material = texMat != null ? texMat : Mat;
            ps.Play();
            Object.Destroy(go, 1.2f);
        }

        public static void Pillar(Vector3 pos, Color color)
        {
            var go = Prims.Create(PrimitiveType.Quad);
            Object.Destroy(go.GetComponent<Collider>());
            go.name = "ImpactPillar";
            go.transform.position = pos + Vector3.up * 0.1f;
            var fade = go.AddComponent<PillarFade>();
            fade.color = VfxTextures.Dim(color); // 매트화 — 기둥도 전역 밝기 계수 (누락 보정 2026-09-05)
            // 광구 텍스처가 있으면 부드러운 빛기둥 — 없으면 민무늬 쿼드
            var glow = VfxTextures.Glow;
            go.GetComponent<Renderer>().material = glow != null ? glow : Mat;
        }
    }

    /// <summary>섬광 기둥 1개 — 솟았다가 0.22초에 걸쳐 늘어나며 사라진다. 카메라 빌보드.</summary>
    public class PillarFade : MonoBehaviour
    {
        public Color color = new Color(1f, 0.85f, 0.55f);
        const float Life = 0.22f;

        float t;
        Renderer rend;
        MaterialPropertyBlock mpb;
        Camera cam;

        void Start()
        {
            rend = GetComponent<Renderer>();
            mpb = new MaterialPropertyBlock();
            cam = Camera.main;
        }

        void LateUpdate()
        {
            t += Time.unscaledDeltaTime;
            float k = t / Life;
            if (k >= 1f)
            {
                Destroy(gameObject);
                return;
            }

            // 폭은 줄고 높이는 치솟는다 — 섬광 느낌
            float width = Mathf.Lerp(0.55f, 0.06f, k);
            float height = Mathf.Lerp(0.8f, 3.2f, Mathf.Sqrt(k));
            transform.localScale = new Vector3(width, height, 1f);
            transform.position = new Vector3(transform.position.x, 0.1f + height * 0.5f, transform.position.z);

            if (cam != null) // Y축 빌보드 — 카메라를 향하되 수직 유지
            {
                var look = cam.transform.position - transform.position;
                look.y = 0f;
                if (look.sqrMagnitude > 0.001f)
                    transform.rotation = Quaternion.LookRotation(-look);
            }

            var c = color * (1f - k); // 가산 텍스처 — 색으로 페이드
            c.a = 1f - k;             // 민무늬 폴백 — 알파로 페이드
            mpb.SetColor("_Color", c);
            mpb.SetColor("_BaseColor", c);
            rend.SetPropertyBlock(mpb);
        }
    }
}
