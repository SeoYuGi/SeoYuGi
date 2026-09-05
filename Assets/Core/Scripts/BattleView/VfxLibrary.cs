using System.Collections.Generic;
using UnityEngine;

namespace SeoYuGi.BattleView
{
    /// <summary>
    /// 이펙트 프리팹 스포너 — Resources 경로 로드 + 캐시 + 수명 자동 파괴.
    /// 프리팹이 없으면 null 반환 — 호출부는 기존 코드 생성 연출로 폴백.
    /// Toon* = ToonFX(동물 톤), Hcfx* = VFX_Klaus HCFX(기계/SF 톤).
    /// </summary>
    public static class VfxLibrary
    {
        static readonly Dictionary<string, GameObject> cache = new Dictionary<string, GameObject>();
        const string Hcfx = "Hyper Casual FX Vol.2/";

        // ── ToonFX (동물 팀 톤) ──
        public const string ToonPunchNormal = "Punches/PunchNormal";           // 고라니 주먹·공통 타격
        public const string ToonPunchSmooth = "Punches/PunchSmooth";           // 가벼운 타격 리액션
        public const string ToonPunchCritical = "Punches/PunchCritical";       // 너구리 강타
        public const string ToonPunchSpikyCrit = "Punches/PunchSpikyCritical"; // 크리티컬 계열
        public const string ToonExplosion = "Explosions/Explosion";            // 폭탄 폭발 (공용)
        public const string ToonExplosionSimple = "Explosions/ExplosionSimple";
        public const string ToonPoof = "Explosions/PoofExplosion";             // 흰 먼지 펑 (넉백샷)
        public const string ToonPoofDark = "Explosions/PoofExplosionDark";     // 검은 연기 (그림자 도약)
        public const string ToonPoofClouds = "Explosions/PoofClouds";          // 방밀 바람 구름
        public const string ToonSmoke = "Smoke/Smoke";

        // ── HCFX (기계 팀 / SF 톤) ──
        public const string HcfxHit1 = Hcfx + "HCFX_Hit_01";
        public const string HcfxHit2 = Hcfx + "HCFX_Hit_02";
        public const string HcfxExplosion = Hcfx + "HCFX_Explosion_01";
        public const string HcfxFlash = Hcfx + "HCFX_Flash_01";
        public const string HcfxAppearStart = Hcfx + "HCFX_Appear_01_Start"; // 사라짐
        public const string HcfxAppearEnd = Hcfx + "HCFX_Appear_01_End";     // 나타남
        public const string HcfxLockOn = Hcfx + "HCFX_Sign_PositionMark_Loop"; // 기계 록온 표적
        public const string HcfxBeam = Hcfx + "HCFX_Beam_01";
        public const string HcfxEnergy = Hcfx + "HCFX_Energy_01";
        public const string HcfxSmokeAir = Hcfx + "HCFX_Smoke_01_Air";       // 비행 바람

        // ── WarFX (JMO, 전쟁 톤 — Assets/JMO Assets/WarFX/Resources/WarFX). 5m급이라 반드시 0.1~0.2배 ──
        public const string WarExplosionSmall = "WarFX/WFX_Explosion Small";          // 격파·파열탄
        public const string WarExplosion = "WarFX/WFX_Explosion";                      // 대형 — 폭탄 착탄 ("터지지도 않아" 2026-09-05)
        public const string WarSmokeGroundBig = "WarFX/WFX_ExplosiveSmokeGround";      // 대형 지면 연기
        public const string WarSmokeGround = "WarFX/WFX_ExplosiveSmokeGround Small";  // 격파 뒤 바닥 연기
        public const string WarImpactMetal = "WarFX/WFX_BImpact Metal";               // 저격 명중 — 기계
        public const string WarImpactDirt = "WarFX/WFX_BImpact Dirt";                 // 저격 명중 — 동물

        // ── ParticleProFX (Assets/ParticleProFX/Resources/Library). Shape 스케일 모드라 hierarchyScale 필수 ──
        public const string PpfxSparks = "Library/Fire & Explosions/ppfxFastSparksExplosion"; // 기계 피격 불꽃
        public const string PpfxElectricExplosion = "Library/Effects/ppfxElectricExplosion";   // 기계 격파
        public const string PpfxDustHit = "Library/Fire & Explosions/ppfxDustHit01";          // 파열탄 주변 먼지

        // ── Wallcoeur Impact & Hit (카툰 타격 — Assets/VFXPACK_IMPACT_WALLCOEUR_FreeVersion/Resources/Wallcoeur) ──
        public const string WallToonImpact = "Wallcoeur/VFX_ImpactToon_1.1.0"; // 동물 피격
        public const string WallCritical = "Wallcoeur/VFX_Critical_01";        // 강타·크리티컬
        public const string WallCross = "Wallcoeur/VFX_ImpactCross_1.1.0";     // 십자 임팩트 (예비)

        /// <summary>
        /// 프리팹 스폰 — life 초 후 자동 파괴. 프리팹 없으면 null (폴백은 호출부).
        /// hierarchyScale: 파티클을 Hierarchy 스케일 모드로 강제 — Shape/Local 모드 팩(PPFX 등)은
        /// transform 스케일이 파티클 크기를 안 줄여 원본(수 m) 크기로 터진다. 전쟁 팩은 항상 true.
        /// </summary>
        public static GameObject Spawn(string path, Vector3 pos, float life = 2f, float scale = 1f, bool hierarchyScale = false)
        {
            var prefab = Load(path);
            if (prefab == null) return null;
            var go = Object.Instantiate(prefab, pos, Quaternion.identity);
            if (hierarchyScale)
                foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = ps.main;
                    main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                }
            if (!Mathf.Approximately(scale, 1f)) go.transform.localScale *= scale;
            Object.Destroy(go, life);
            return go;
        }

        static GameObject Load(string path)
        {
            if (cache.TryGetValue(path, out var p)) return p;
            p = Resources.Load<GameObject>(path);
            if (p == null) Debug.LogWarning($"VfxLibrary. 프리팹 없음: {path} (코드 연출 폴백)");
            cache[path] = p; // null도 캐시 — 경고 1회만
            return p;
        }
    }
}
