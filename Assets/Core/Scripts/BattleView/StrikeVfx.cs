using System;
using System.Collections;
using System.Collections.Generic;
using SeoYuGi.Battle;
using UnityEngine;

namespace SeoYuGi.BattleView
{
    /// <summary>
    /// 예고(텔레그래프)→판정 구간의 클래스별 연출.
    /// 예고: 까치=조준경 레티클+레이저, 기계팀=SF 록온 마커, 비둘기=폭탄 투사체 포물선.
    /// 판정: 너구리=칼 베기, 고라니=주먹, 검은냥=발톱, 비둘기=폭발(공용), 까치=트레이서,
    ///       기계팀=광선검 베기·연쇄 폭격.
    /// 스킬 종류는 강타 형상으로 추론(pushCells·stun·피해·칸수) — 네트 복제본에서도 동작.
    /// </summary>
    public static class StrikeVfx
    {
        static readonly Color SniperRed = new Color(0.95f, 0.12f, 0.1f, 0.95f);
        static readonly Color SaberCyan = new Color(0.45f, 0.95f, 1f);
        static readonly Color ClawRed = new Color(1f, 0.3f, 0.22f);
        static readonly Color SteelWhite = new Color(0.9f, 0.95f, 1f);

        /// <summary>예고 시작 — 반환 컨테이너는 판정 시 파괴(레이저·마커·투사체 수명 = 예고 시간).</summary>
        public static GameObject Telegraph(TelegraphStrike strike, UnitClass cls, bool attackerFlying,
            Vector3 casterWorld, IReadOnlyList<Vector3> cells, float seconds)
        {
            if (cells.Count == 0) return null;
            var root = new GameObject("StrikeTelegraphFx");
            bool machine = strike.team == 1;

            if (machine)
            {
                // 기계 — SF 록온 마커 (프리팹 없으면 시안 레티클 폴백)
                int shown = 0;
                foreach (var w in cells)
                {
                    if (shown++ >= 6) break;
                    var m = VfxLibrary.Spawn(VfxLibrary.HcfxLockOn, w + Vector3.up * 0.04f, seconds + 0.5f, 0.55f);
                    if (m != null) m.transform.SetParent(root.transform);
                    else ScopeMarker.Spawn(root.transform, w, new Color(0.4f, 0.9f, 1f, 0.9f), 0.7f);
                }
                if (cls == UnitClass.Sniper) // 감시 드론 — 레이저 조준선
                    LaserBeam.Spawn(root.transform, casterWorld + Vector3.up * 0.55f,
                        cells[cells.Count - 1] + Vector3.up * 0.05f, SaberCyan);
            }
            else if (cls == UnitClass.Sniper)
            {
                // 까치 — 저격수의 상징: 빨간 조준경 레티클 + 레이저 조준선
                foreach (var w in cells)
                    ScopeMarker.Spawn(root.transform, w, SniperRed, 0.85f);
                LaserBeam.Spawn(root.transform, casterWorld + Vector3.up * 0.55f,
                    cells[cells.Count - 1] + Vector3.up * 0.05f, SniperRed);
            }
            else if (cls == UnitClass.Grenadier && !attackerFlying)
            {
                // 비둘기 일반공격·파열탄 — 폭탄이 포물선으로 날아간다 (배달 비행은 유닛이 직접 운반)
                BombProjectile.Spawn(root.transform, casterWorld + Vector3.up * 0.5f, cells[0], seconds);
            }

            return root.transform.childCount > 0 ? root : DestroyAndNull(root);
        }

        static GameObject DestroyAndNull(GameObject go)
        {
            UnityEngine.Object.Destroy(go);
            return null;
        }

        /// <summary>판정 순간 — 클래스·팀별 임팩트. cells는 시야 필터를 통과한 칸만.</summary>
        public static void Resolve(TelegraphStrike strike, UnitClass cls, IReadOnlyList<Vector3> cells, bool hit)
        {
            if (cells.Count == 0) return;
            bool machine = strike.team == 1;
            var center = cells[0];

            if (machine)
            {
                switch (cls)
                {
                    case UnitClass.Tank:
                    case UnitClass.Balance:
                    case UnitClass.Assassin:
                        // 기계 근접 — 광선검 베기 (시안 슬래시 + SF 히트)
                        SlashQuad(center, SaberCyan, spin: UnityEngine.Random.Range(-40f, 40f), scale: 1.25f);
                        VfxLibrary.Spawn(VfxLibrary.HcfxHit1, center + Vector3.up * 0.35f, 1.5f, 0.6f);
                        break;
                    default:
                        // 기계 원거리 — 록온 후 우다다 연쇄 폭격
                        FxSequencer.Stagger(cells, 0.07f, w =>
                        {
                            if (VfxLibrary.Spawn(VfxLibrary.HcfxExplosion, w + Vector3.up * 0.15f, 2f, 0.55f) == null)
                                ImpactVfx.Sparks(w, machine: true, scale: 1.2f); // 폴백
                        });
                        break;
                }
                return;
            }

            switch (cls)
            {
                case UnitClass.Tank:
                    if (strike.pushCells >= 2)
                    {
                        // 방패 밀어붙이기 — 미는 방향 가로 바람 + 먼지 구름
                        var dir = new Vector3(strike.pushDir.x, 0f, strike.pushDir.y);
                        WindStreaks(center, dir);
                        VfxLibrary.Spawn(VfxLibrary.ToonPoofClouds, center + Vector3.up * 0.2f, 1.6f, 0.55f);
                    }
                    else if (strike.damage >= 2)
                    {
                        // 강타 — 둔탁한 크리티컬 펀치
                        VfxLibrary.Spawn(VfxLibrary.ToonPunchCritical, center + Vector3.up * 0.45f, 1.6f, 0.75f);
                    }
                    else
                    {
                        // 기본공격 — 칼 베기 호
                        SlashQuad(center, SteelWhite, spin: UnityEngine.Random.Range(-30f, 30f), scale: 1.15f);
                        if (hit) VfxLibrary.Spawn(VfxLibrary.ToonPunchSmooth, center + Vector3.up * 0.4f, 1.4f, 0.5f);
                    }
                    break;

                case UnitClass.Balance:
                    if (strike.stunSeconds > 0f) break; // 비명 — 시전 링이 주인공, 판정은 스턴 텍스트
                    // 기본공격 — 주먹
                    VfxLibrary.Spawn(VfxLibrary.ToonPunchNormal, center + Vector3.up * 0.45f, 1.5f, 0.6f);
                    break;

                case UnitClass.Assassin:
                    if (strike.damage >= 3)
                    {
                        // 발톱 쥐어짜기 — 발톱 2연격 촥촥 (교차 방향)
                        SlashClaw(center, spin: -28f);
                        FxSequencer.Delay(0.13f, () => SlashClaw(center, spin: 62f));
                    }
                    else
                    {
                        SlashClaw(center, spin: UnityEngine.Random.Range(-25f, 25f)); // 기본 — 발톱 1번
                    }
                    break;

                case UnitClass.Grenadier:
                    // 폭탄 터짐 (공용) — 중심 크게, 주변 십자는 작게
                    if (VfxLibrary.Spawn(VfxLibrary.ToonExplosion, center + Vector3.up * 0.2f, 2f, 0.6f) == null)
                        ImpactVfx.Sparks(center, machine: false, scale: 1.5f);
                    for (int i = 1; i < cells.Count && i <= 4; i++)
                        VfxLibrary.Spawn(VfxLibrary.ToonExplosionSimple, cells[i] + Vector3.up * 0.15f, 1.8f, 0.4f);
                    break;

                case UnitClass.Sniper:
                    // 저격 판정 — 총성 트레이서가 조준선을 따라 번쩍
                    if (cells.Count >= 1)
                        LaserBeam.Flash(cells[0] + Vector3.up * 0.3f, cells[cells.Count - 1] + Vector3.up * 0.3f,
                            new Color(1f, 0.55f, 0.4f), 0.12f);
                    if (hit && VfxLibrary.Spawn(VfxLibrary.HcfxFlash, cells[cells.Count - 1] + Vector3.up * 0.3f, 1.2f, 0.5f) == null)
                        ImpactVfx.Sparks(cells[cells.Count - 1], machine: false, scale: 1f);
                    break;
            }
        }

        /// <summary>공통 피격 리액션 — 기계=SF 히트, 동물=만화 펀치. 기존 스파크 위에 얹는 층.</summary>
        public static void HitReaction(Vector3 pos, bool machine)
        {
            if (machine) VfxLibrary.Spawn(VfxLibrary.HcfxHit2, pos + Vector3.up * 0.4f, 1.4f, 0.5f);
            else VfxLibrary.Spawn(VfxLibrary.ToonPunchSmooth, pos + Vector3.up * 0.45f, 1.3f, 0.42f);
        }

        // ── 내부 도우미 ──────────────────────────────────────────

        static void SlashQuad(Vector3 pos, Color color, float spin, float scale)
        {
            FxQuad.One(VfxTextures.Slash, pos + Vector3.up * 0.5f, color, scale, 0.45f, 0.22f, spinDeg: spin);
        }

        static void SlashClaw(Vector3 pos, float spin)
        {
            var mat = VfxTextures.Claw;
            if (mat == null) return;
            FxQuad.One(mat, pos + Vector3.up * 0.5f, ClawRed, 1.35f, 0.35f, 0.26f, spinDeg: spin);
        }

        static void WindStreaks(Vector3 pos, Vector3 dir)
        {
            var mat = VfxTextures.Wind;
            if (mat == null || dir.sqrMagnitude < 0.01f) return;
            // 미는 방향으로 흐르는 바람 줄기 3장 — 빌보드 대신 방향 유지가 맞지만
            // FxQuad는 빌보드라 속도만 방향을 준다 (탑뷰에서 충분히 읽힌다)
            for (int i = 0; i < 3; i++)
                FxQuad.One(mat, pos + Vector3.up * (0.3f + i * 0.15f), new Color(0.85f, 0.9f, 1f),
                    1.3f, 0.5f, 0.28f, spinDeg: 0f, velocity: dir.normalized * (3.2f + i * 0.8f));
        }
    }

    /// <summary>조준경 레티클 — 바닥에 눕힌 쿼드, 맥동 + 천천히 회전. 부모 파괴 시 함께 소멸.</summary>
    public class ScopeMarker : MonoBehaviour
    {
        Color color;
        float baseScale;
        Renderer rend;
        MaterialPropertyBlock mpb;

        public static void Spawn(Transform parent, Vector3 cellWorld, Color color, float scale)
        {
            var mat = VfxTextures.Crosshair;
            if (mat == null) return;
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            UnityEngine.Object.Destroy(go.GetComponent<Collider>());
            go.name = "ScopeMarker";
            go.transform.SetParent(parent);
            go.transform.position = cellWorld + Vector3.up * 0.03f;
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // 바닥에 눕힘
            var m = go.AddComponent<ScopeMarker>();
            m.color = color;
            m.baseScale = scale;
            m.rend = go.GetComponent<Renderer>();
            m.rend.sharedMaterial = mat;
            m.rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m.mpb = new MaterialPropertyBlock();
        }

        void LateUpdate()
        {
            float pulse = 1f + 0.08f * Mathf.Sin(Time.unscaledTime * 9f); // 조여드는 맥동
            transform.localScale = Vector3.one * (baseScale * pulse);
            transform.Rotate(0f, 0f, 25f * Time.unscaledDeltaTime, Space.Self);
            mpb.SetColor("_BaseColor", color);
            mpb.SetColor("_Color", color);
            rend.SetPropertyBlock(mpb);
        }
    }

    /// <summary>레이저 조준선 — 가는 라인, 알파 플리커. 부모 파괴 시 소멸.</summary>
    public class LaserBeam : MonoBehaviour
    {
        LineRenderer line;
        Color color;

        public static void Spawn(Transform parent, Vector3 from, Vector3 to, Color color)
        {
            var go = new GameObject("LaserBeam");
            go.transform.SetParent(parent);
            var b = go.AddComponent<LaserBeam>();
            b.color = color;
            b.line = MakeLine(go, from, to, color, 0.035f);
        }

        /// <summary>판정 순간 1회성 굵은 섬광 — duration 후 자체 소멸.</summary>
        public static void Flash(Vector3 from, Vector3 to, Color color, float duration)
        {
            var go = new GameObject("TracerFlash");
            var line = MakeLine(go, from, to, color, 0.12f);
            go.AddComponent<TracerFade>().Init(line, color, duration);
        }

        static LineRenderer MakeLine(GameObject go, Vector3 from, Vector3 to, Color color, float width)
        {
            var line = go.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.SetPosition(0, from);
            line.SetPosition(1, to);
            line.startWidth = line.endWidth = width;
            line.material = VfxTextures.Glow != null ? VfxTextures.Glow : new Material(Shader.Find("Sprites/Default"));
            line.startColor = line.endColor = color;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return line;
        }

        void LateUpdate()
        {
            float flicker = 0.55f + 0.45f * Mathf.PerlinNoise(Time.unscaledTime * 14f, 0.3f);
            var c = color;
            c.a *= flicker;
            line.startColor = line.endColor = c;
        }
    }

    /// <summary>트레이서 1회성 페이드.</summary>
    public class TracerFade : MonoBehaviour
    {
        LineRenderer line;
        Color color;
        float life, t;

        public void Init(LineRenderer l, Color c, float duration)
        {
            line = l;
            color = c;
            life = duration;
        }

        void LateUpdate()
        {
            t += Time.unscaledDeltaTime;
            float k = t / life;
            if (k >= 1f)
            {
                Destroy(gameObject);
                return;
            }
            var c = color;
            c.a = 1f - k;
            line.startColor = line.endColor = c;
            line.startWidth = line.endWidth = Mathf.Lerp(0.12f, 0.02f, k);
        }
    }

    /// <summary>폭탄 투사체 — 포물선 비행, 착탄 시각 = 예고 판정 시각. 부모 파괴 시 소멸.</summary>
    public class BombProjectile : MonoBehaviour
    {
        Vector3 a, b;
        float duration, t;

        public static void Spawn(Transform parent, Vector3 from, Vector3 toCell, float flightSeconds)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            UnityEngine.Object.Destroy(go.GetComponent<Collider>());
            go.name = "BombProjectile";
            go.transform.SetParent(parent);
            go.transform.position = from;
            go.transform.localScale = Vector3.one * 0.17f;
            var rend = go.GetComponent<Renderer>();
            rend.material.color = new Color(0.16f, 0.16f, 0.18f); // 만화 폭탄 — 검은 구
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var trail = go.AddComponent<TrailRenderer>();
            trail.time = 0.25f;
            trail.startWidth = 0.08f;
            trail.endWidth = 0f;
            trail.material = VfxTextures.Glow != null ? VfxTextures.Glow : rend.material;
            trail.startColor = new Color(1f, 0.8f, 0.4f, 0.8f);
            trail.endColor = new Color(1f, 0.5f, 0.2f, 0f);

            var p = go.AddComponent<BombProjectile>();
            p.a = from;
            p.b = toCell + Vector3.up * 0.15f;
            p.duration = Mathf.Max(0.15f, flightSeconds);
        }

        void Update()
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / duration);
            var p = Vector3.Lerp(a, b, k);
            p.y += 1.3f * 4f * k * (1f - k); // 포물선
            transform.position = p;
            if (k >= 1f) gameObject.SetActive(false); // 착탄 — 폭발은 판정 연출이 맡는다
        }
    }

    /// <summary>지연 실행 도우미 — 다중 칸 연쇄 폭격·2연격 타이밍.</summary>
    public class FxSequencer : MonoBehaviour
    {
        public static void Delay(float seconds, Action action)
        {
            var go = new GameObject("FxDelay");
            var seq = go.AddComponent<FxSequencer>();
            seq.StartCoroutine(seq.Run(seconds, action));
        }

        public static void Stagger(IReadOnlyList<Vector3> cells, float interval, Action<Vector3> perCell)
        {
            var go = new GameObject("FxStagger");
            var seq = go.AddComponent<FxSequencer>();
            seq.StartCoroutine(seq.RunStagger(cells, interval, perCell));
        }

        IEnumerator Run(float seconds, Action action)
        {
            yield return new WaitForSeconds(seconds);
            action?.Invoke();
            Destroy(gameObject);
        }

        IEnumerator RunStagger(IReadOnlyList<Vector3> cells, float interval, Action<Vector3> perCell)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                perCell(cells[i]);
                if (interval > 0f) yield return new WaitForSeconds(interval);
            }
            Destroy(gameObject);
        }
    }
}
