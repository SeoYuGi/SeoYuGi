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
        // ── 공격 색 언어 (2026-09-05 통일): 내 팀 = 틸 네온, 적 = 레드-오렌지 네온. 클래스별 고유색은 버렸다 —
        //    "누구 편 공격인가"가 색 하나로 읽혀야 한다 (기획서 §06: 아군 틸 / AI 시그널 레드-오렌지).
        public static readonly Color MineNeon = new Color(0.45f, 1f, 0.95f, 0.95f);
        public static readonly Color EnemyNeon = new Color(1f, 0.32f, 0.15f, 0.95f);
        public static Color TeamColor(bool mine) => mine ? MineNeon : EnemyNeon;
        public static Color TeamHi(bool mine) => Color.Lerp(TeamColor(mine), Color.white, 0.55f); // 밝은 변주 (베기 날·트레이서)

        static readonly Color SniperRed = EnemyNeon; // 예고선·경고에 쓰는 "적 위협" 색과 동일

        static readonly Color MineWhite = new Color(0.7f, 1f, 0.95f, 0.9f); // 내 공격선 — 틸-흰

        /// <summary>
        /// 예고 시작 — 반환 컨테이너는 판정 시 파괴(레이저·마커·투사체 수명 = 예고 시간).
        /// 모든 공격에 시전자→조준 칸 공격선: 적 = 빨강, 내 팀 = 흰 (색 언어) — "누가 누굴 때리는지"가 선으로 읽힌다.
        /// 조준경은 저격수의 조준 칸 하나에만 (열 전체에 찍으면 소음).
        /// </summary>
        public static GameObject Telegraph(TelegraphStrike strike, UnitClass cls, bool attackerFlying,
            Vector3 casterWorld, IReadOnlyList<Vector3> cells, float seconds, bool mine, Vector3 aimWorld)
        {
            if (cells.Count == 0) return null;
            var root = new GameObject("StrikeTelegraphFx");
            var lineColor = mine ? MineWhite : SniperRed;

            LaserBeam.Spawn(root.transform, casterWorld + Vector3.up * 0.55f, aimWorld + Vector3.up * 0.15f, lineColor);

            SkillIcon(root.transform, strike.kind, aimWorld, lineColor); // 무슨 스킬인지 — 연계를 짜려면 알아야 한다

            if (cls == UnitClass.Sniper)
                ScopeMarker.Spawn(root.transform, aimWorld, lineColor, 1f); // 조준경 — 조준 칸 하나

            if (cls == UnitClass.Grenadier && !attackerFlying && strike.team != 1)
            {
                // 비둘기 일반공격·파열탄 — 폭탄이 포물선으로 날아간다 (배달 비행은 유닛이 직접 운반)
                BombProjectile.Spawn(root.transform, casterWorld + Vector3.up * 0.5f, cells[0], seconds);
            }

            return root;
        }

        /// <summary>
        /// 예고 스킬 아이콘 — 조준 칸 바닥에 눕혀 그린다(빌보드 없음, 그리드와 같이 읽힌다).
        /// UI 아이콘은 어두운 패널 위 흰 실루엣 전제라 밝은 바닥에 그냥 놓으면 묻힌다.
        /// 그래서 3겹으로 깐다: 팀색 테두리 판 → 어두운 속판 → 흰 아이콘.
        /// 아이콘은 틴트하지 않는다 — 팀 색은 테두리가 말하고, 형상은 흰색이 제일 잘 읽힌다.
        /// </summary>
        static void SkillIcon(Transform parent, SkillKind kind, Vector3 aimWorld, Color color)
        {
            var tex = Resources.Load<Texture2D>("UI/" + SkillIconName(kind));
            if (tex == null) return;

            var rim = color; rim.a = 0.9f;
            Plate(parent, DiscTex(), aimWorld, 0.085f, 0.88f, rim);                                  // 팀색 테두리
            Plate(parent, DiscTex(), aimWorld, 0.088f, 0.74f, new Color(0.04f, 0.05f, 0.08f, 0.88f)); // 어두운 속판
            Plate(parent, tex, aimWorld, 0.091f, 0.52f, new Color(1f, 1f, 1f, 0.98f));               // 흰 아이콘
        }

        /// <summary>바닥에 눕힌 사각 쿼드 한 장 — 아이콘 판 3겹의 공용 부품.</summary>
        static void Plate(Transform parent, Texture2D tex, Vector3 world, float lift, float size, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            UnityEngine.Object.Destroy(go.GetComponent<Collider>()); // 클릭 레이캐스트 방해 금지
            go.name = "SkillIconPlate";
            go.transform.SetParent(parent, false);
            go.transform.position = world + Vector3.up * lift;
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale = Vector3.one * size;

            var mat = new Material(Shader.Find("Sprites/Default")); // 알파 — 매트
            mat.mainTexture = tex;
            mat.color = color;
            var rend = go.GetComponent<Renderer>();
            rend.material = mat;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
        }

        static Texture2D discTex;

        /// <summary>아이콘 받침용 원판 — 절차 생성 1회. 가장자리 안티에일리어싱.</summary>
        static Texture2D DiscTex()
        {
            if (discTex != null) return discTex;
            const int n = 64;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
            t.wrapMode = TextureWrapMode.Clamp;
            float c = (n - 1) * 0.5f;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                t.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(c - d)));
            }
            t.Apply();
            discTex = t;
            return discTex;
        }

        /// <summary>스킬 → 아이콘 리소스명. ClassCard의 카드용 표와 같은 매핑(레이어가 달라 각자 보유).</summary>
        static string SkillIconName(SkillKind k)
        {
            switch (k)
            {
                case SkillKind.Smash: return "Icon_Skill_Smash";
                case SkillKind.Dash: return "Icon_Skill_Dash";
                case SkillKind.Blink: return "Icon_Skill_Blink";
                case SkillKind.Burst: return "Icon_Skill_Burst";
                case SkillKind.Snipe: return "Icon_Skill_Snipe";
                case SkillKind.ShieldPush: return "Icon_Guard";
                case SkillKind.Claw: return "Icon_Attack";
                case SkillKind.KnockShot: return "Icon_Attack";
                case SkillKind.BombDeliver: return "Icon_Skill_BombDeliver";
                case SkillKind.BasicAttack: return "Icon_Attack";
                default: return "Icon_Skill_Generic";
            }
        }

        /// <summary>판정 순간 — 클래스·팀별 임팩트. cells는 시야 필터를 통과한 칸만. mine = 내 팀 공격(색 언어).</summary>
        public static void Resolve(TelegraphStrike strike, UnitClass cls, IReadOnlyList<Vector3> cells, bool hit, bool mine)
        {
            if (cells.Count == 0) return;
            bool machine = strike.team == 1;
            var center = cells[0];
            var team = TeamColor(mine);
            var teamHi = TeamHi(mine);

            if (machine)
            {
                switch (cls)
                {
                    case UnitClass.Tank:
                    case UnitClass.Balance:
                    case UnitClass.Assassin:
                        // 기계 근접 — 광선검 베기 (팀색 슬래시) + 금속 불꽃 (HCFX 파스텔은 톤 불일치로 제거)
                        SlashQuad(center, teamHi, spin: UnityEngine.Random.Range(-40f, 40f), scale: 1.25f);
                        if (VfxLibrary.Spawn(VfxLibrary.PpfxSparks, center + Vector3.up * 0.3f, 1.5f, 0.22f, hierarchyScale: true) == null)
                            ImpactVfx.Sparks(center, machine: true);
                        break;
                    default:
                        // 기계 원거리 — 연쇄 불꽃 (칸 안 크기, 칸마다 살짝 시차)
                        FxSequencer.Stagger(cells, 0.07f, w =>
                        {
                            if (VfxLibrary.Spawn(VfxLibrary.PpfxSparks, w + Vector3.up * 0.25f, 1.5f, 0.2f, hierarchyScale: true) == null)
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
                        // 강타 — 카툰 크리티컬 (Wallcoeur), 없으면 ToonFX 펀치
                        if (VfxLibrary.Spawn(VfxLibrary.WallCritical, center + Vector3.up * 0.3f, 1.6f, 0.4f, hierarchyScale: true) == null)
                            VfxLibrary.Spawn(VfxLibrary.ToonPunchCritical, center + Vector3.up * 0.45f, 1.6f, 0.75f);
                    }
                    else
                    {
                        // 기본공격 — 칼 베기 호 (팀색 날)
                        SlashQuad(center, teamHi, spin: UnityEngine.Random.Range(-30f, 30f), scale: 1.15f);
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
                        SlashClaw(center, team, spin: -28f);
                        FxSequencer.Delay(0.13f, () => SlashClaw(center, team, spin: 62f));
                    }
                    else
                    {
                        SlashClaw(center, team, spin: UnityEngine.Random.Range(-25f, 25f)); // 기본 — 발톱 1번
                    }
                    break;

                case UnitClass.Grenadier:
                    // 폭탄 터짐 — 전쟁 톤 폭발을 칸 크기(0.18)로. 주변 십자는 먼지. 없으면 ToonFX 폴백
                    if (VfxLibrary.Spawn(VfxLibrary.WarExplosionSmall, center + Vector3.up * 0.05f, 2.5f, 0.18f, hierarchyScale: true) == null &&
                        VfxLibrary.Spawn(VfxLibrary.ToonExplosion, center + Vector3.up * 0.2f, 2f, 0.6f) == null)
                        ImpactVfx.Sparks(center, machine: false, scale: 1.5f);
                    for (int i = 1; i < cells.Count && i <= 4; i++)
                        if (VfxLibrary.Spawn(VfxLibrary.PpfxDustHit, cells[i] + Vector3.up * 0.05f, 1.8f, 0.2f, hierarchyScale: true) == null)
                            VfxLibrary.Spawn(VfxLibrary.ToonExplosionSimple, cells[i] + Vector3.up * 0.15f, 1.8f, 0.4f);
                    break;

                case UnitClass.Sniper:
                    // 저격 판정 — 총성 트레이서가 조준선을 따라 번쩍
                    // 저격은 지정 칸 1개 — 하늘에서 꽂히는 짧은 트레이서 (시전자 위치는 여기 없다)
                    if (cells.Count >= 1)
                        LaserBeam.Flash(cells[0] + Vector3.up * 1.6f, cells[0] + Vector3.up * 0.25f, teamHi, 0.12f);
                    if (hit)
                    {
                        // 탄착 — 피격자는 공격자 반대 팀: 기계가 쐈으면 동물(흙먼지), 동물이 쐈으면 기계(금속 불꽃)
                        var end = cells[cells.Count - 1];
                        string impact = strike.team == 1 ? VfxLibrary.WarImpactDirt : VfxLibrary.WarImpactMetal;
                        if (VfxLibrary.Spawn(impact, end + Vector3.up * 0.1f, 2f, 0.25f, hierarchyScale: true) == null)
                            ImpactVfx.Sparks(end, machine: false, scale: 1f);
                    }
                    break;
            }
        }

        /// <summary>공통 피격 리액션 — 기계=금속 불꽃(전쟁 팩, 칸 안 크기), 동물=만화 펀치. 기존 스파크 위에 얹는 층.</summary>
        public static void HitReaction(Vector3 pos, bool machine)
        {
            if (machine)
            {
                // 타격 반응 상향 (2026-09-05 "전반적으로 약하다") — 스파크 0.22→0.3, 카툰 임팩트 0.35→0.5. 칸 크기 안에서 최대치.
                if (VfxLibrary.Spawn(VfxLibrary.PpfxSparks, pos + Vector3.up * 0.3f, 1.4f, 0.3f, hierarchyScale: true) == null)
                    ImpactVfx.Sparks(pos, machine: true);
            }
            else if (VfxLibrary.Spawn(VfxLibrary.WallToonImpact, pos + Vector3.up * 0.4f, 1.3f, 0.5f, hierarchyScale: true) == null)
                VfxLibrary.Spawn(VfxLibrary.ToonPunchSmooth, pos + Vector3.up * 0.45f, 1.3f, 0.55f); // 카툰 타격팩 없으면 ToonFX
        }

        /// <summary>격파 — 전쟁 톤 폭발+바닥 연기를 칸 크기로 (큰 연출은 격파에만). 기계는 전기 폭발을 얹는다.</summary>
        public static void Kill(Vector3 pos, bool machine)
        {
            VfxLibrary.Spawn(VfxLibrary.WarExplosionSmall, pos + Vector3.up * 0.05f, 3f, 0.16f, hierarchyScale: true);
            VfxLibrary.Spawn(VfxLibrary.WarSmokeGround, pos, 4f, 0.14f, hierarchyScale: true);
            if (machine) VfxLibrary.Spawn(VfxLibrary.PpfxElectricExplosion, pos + Vector3.up * 0.2f, 2.5f, 0.2f, hierarchyScale: true);
        }

        // ── 내부 도우미 ──────────────────────────────────────────

        static void SlashQuad(Vector3 pos, Color color, float spin, float scale)
        {
            FxQuad.One(VfxTextures.Slash, pos + Vector3.up * 0.5f, color, scale, 0.45f, 0.22f, spinDeg: spin);
        }

        static void SlashClaw(Vector3 pos, Color color, float spin)
        {
            var mat = VfxTextures.Claw;
            if (mat == null) return;
            FxQuad.One(mat, pos + Vector3.up * 0.5f, color, 1.35f, 0.35f, 0.26f, spinDeg: spin);
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
            go.transform.position = cellWorld + Vector3.up * 0.13f; // 타일 윗면(+0.05) 위 — 0.03이면 타일 속에 파묻혀 안 보였다
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
            b.line = MakeLine(go, from, to, color, 0.06f); // 0.035는 탑뷰에서 실처럼 사라졌다
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
