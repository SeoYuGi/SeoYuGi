using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using SeoYuGi.Battle;
using SeoYuGi.BattleView;

namespace SeoYuGi.Art
{
    /// <summary>
    /// 모든 유닛 큐브에 (팀, 클래스)별 GLB 스킨을 씌운다.
    /// 씬 배선 불필요 — SampleScene(팀 합의 작업 씬)에서 자동 발동하고,
    /// 라운드마다 유닛이 재생성되므로 매 프레임 미적용 유닛을 찾아 적용한다.
    /// 롤식 TacticalCamera도 함께 장착한다.
    /// </summary>
    public static class UnitSkinBootstrap
    {
        static readonly string[] SceneNames = { "SampleScene", "GoraniSkinTest" };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            if (System.Array.IndexOf(SceneNames, SceneManager.GetActiveScene().name) < 0) return;
            new GameObject("UnitSkinApplier").AddComponent<UnitSkinApplier>();

            var cam = Camera.main;
            if (cam != null && cam.GetComponent<TacticalCamera>() == null)
            {
                var quarter = cam.GetComponent<QuarterViewCamera>();
                if (quarter != null) quarter.enabled = false;
                cam.gameObject.AddComponent<TacticalCamera>();
            }
        }
    }

    public class UnitSkinApplier : MonoBehaviour
    {
        const string SkinName = "Skin";
        const float BaseHeight = 1.4f;       // 기본 클래스(스케일 1)의 목표 키 — 1.1은 전체 맵 뷰에서 ~35px라 실루엣이 안 읽혔다 (2026-09-05)
        const float BaseCubeScale = 0.7f;    // BattleRunner 폴백 큐브의 기준 스케일
        const float UnitYOffset = 0.5f;      // UnitView.yOffset — 피벗에서 바닥까지

        // 팀 0 = 길동물, 팀 1 = 도시관리 AI. Resources의 GLB 이름과 매핑.
        static readonly Dictionary<(int team, UnitClass cls), string> Models = new()
        {
            { (0, UnitClass.Tank),      "raccoon_tank_rigged" },
            { (0, UnitClass.Balance),   "gorani_runner_rigged" },
            { (0, UnitClass.Assassin),  "blackcat_assassin_rigged" },
            { (0, UnitClass.Grenadier), "pigeon_grenadier_rigged" },
            { (0, UnitClass.Sniper),    "magpie_sniper_rigged" },
            // 기계팀은 같은 동물 모델을 쓰고 표면만 금속으로 바꾼다 (MakeMachine).
            // "기계 동물 vs 진짜 동물"이라는 그림이고, 덤으로 기계팀도 애니메이션을 얻는다 —
            // 구 드론 모델(cleaning_bot 등)에는 _idle/_anim/_atk 변형이 없어 둥실거리기만 했다.
            { (1, UnitClass.Tank),      "raccoon_tank_rigged" },
            { (1, UnitClass.Balance),   "gorani_runner_rigged" },
            { (1, UnitClass.Assassin),  "blackcat_assassin_rigged" },
            { (1, UnitClass.Grenadier), "pigeon_grenadier_rigged" },
            { (1, UnitClass.Sniper),    "magpie_sniper_rigged" },
        };

        // 같은 클립이라도 클래스별 배속으로 성격 부여 (묵직↔날렵)
        static readonly Dictionary<UnitClass, float> AnimSpeed = new()
        {
            { UnitClass.Tank, 0.85f },
            { UnitClass.Balance, 1.05f },
            { UnitClass.Assassin, 1.15f },
            { UnitClass.Grenadier, 0.9f },
            { UnitClass.Sniper, 0.95f },
        };

        BattleRunner runner;
        CombatSystem combatRef; // 라운드마다 재조립 — 인스턴스 바뀌면 재구독
        readonly Dictionary<string, GameObject> cache = new();
        readonly HashSet<UnitView> attempted = new();
        readonly Dictionary<int, MoveSwapSkin> machines = new();

        void Awake()
        {
            runner = FindFirstObjectByType<BattleRunner>();
        }

        void LateUpdate()
        {
            if (runner == null || runner.Battle == null) return;

            // 공격 모션 트리거 — 캐스팅 순간 원샷 재생 (라운드 전환 시 재구독)
            if (runner.Combat != null && runner.Combat != combatRef)
            {
                combatRef = runner.Combat;
                combatRef.OnTelegraph += strike => PlayAttackMotion(strike.attackerId, SkillKind.Smash);
                combatRef.OnSkillCast += PlayAttackMotion;
            }

            foreach (var view in FindObjectsByType<UnitView>(FindObjectsSortMode.None))
            {
                if (attempted.Contains(view)) continue;
                attempted.Add(view);
                Apply(view);
            }

            // 파괴된 뷰 참조 정리 (라운드 전환)
            attempted.RemoveWhere(v => v == null);
        }

        void PlayAttackMotion(int unitId, SkillKind kind)
        {
            if (machines.TryGetValue(unitId, out var m) && m != null)
            {
                // 까치 사격 클립은 뽑는 동작이 길어 노출 상한을 넉넉히
                bool snipe = kind == SkillKind.Snipe || kind == SkillKind.KnockShot;
                m.PlayAttack(snipe ? 2.2f : 1.2f);
            }
            // 폭탄 배달 비행은 UnitView.PlayBombFlight(왕복)가 담당 — 구 BombHop 아크 폐기
        }

        void Apply(UnitView view)
        {
            UnitState unit = null;
            foreach (var u in runner.Battle.Units)
                if (u.id == view.UnitId) { unit = u; break; }
            if (unit == null) return;

            if (!Models.TryGetValue((unit.team, unit.unitClass), out var resource)) return;

            var prefab = LoadModel(resource);
            if (prefab == null) return; // 모델 없으면 큐브 유지 (미도착분 폴백)

            // BattleRunner가 클래스별로 큐브 스케일을 키워놓음 — 그 비율만큼 스킨도 크게
            float classScale = view.transform.localScale.y / BaseCubeScale;
            float height = BaseHeight * classScale;

            var skin = new GameObject(SkinName);
            skin.transform.SetParent(view.transform, false);

            // 대기 = 대기 애니 모델(<resource>_idle) 우선, 없으면 다이내믹 포즈 정적 모델
            GameObject idleGo;
            var idlePrefab = Resources.Load<GameObject>(resource + "_idle");
            if (idlePrefab != null)
            {
                idleGo = Instantiate(idlePrefab, skin.transform);
                var idleClips = Resources.LoadAll<AnimationClip>(resource + "_idle");
                if (idleClips.Length > 0)
                    idleGo.AddComponent<SkinLoopAnimator>().Init(idleClips[0], AnimSpeed[unit.unitClass]);
            }
            else
            {
                idleGo = Instantiate(prefab, skin.transform);
            }
            idleGo.transform.localPosition = Vector3.zero;
            idleGo.transform.localRotation = Quaternion.identity;
            FitToUnit(idleGo, view.transform, height);

            // 이동 = A포즈 달리기 애니 모델 (<resource>_anim), 공격 = <resource>_atk (있으면)
            GameObject runGo = LoadVariant(resource + "_anim", skin, view, height, unit.unitClass, out _);
            GameObject atkGo = LoadVariant(resource + "_atk", skin, view, height, unit.unitClass, out var atkAnim);

            // 까치 사격 모션은 맨손(총이 등 메시에 박힘) — 손 본에 소품 라이플 부착
            if (atkGo != null && unit.unitClass == UnitClass.Sniper)
                HandRifle.Attach(atkGo, height * 0.55f);

            if (runGo != null || atkGo != null)
            {
                var swap = skin.AddComponent<MoveSwapSkin>();
                swap.Init(view, idleGo, runGo, atkGo, atkAnim);
                machines[unit.id] = swap;
            }
            else if (unit.team == 0)
            {
                // 리깅 불가 캐릭터(비둘기) — 절차적 파닥·통통 연출로 대체
                skin.AddComponent<WaddleBounce>().Init(view, idleGo);
            }

            // 기계팀 — 같은 동물 모델의 표면만 금속으로. 실루엣은 같고 재질로 갈린다.
            if (unit.team == 1) MakeMachine(skin);

            var cube = view.GetComponent<MeshRenderer>();
            if (cube != null) cube.enabled = false;
        }

        // URP Lit 과 glTFast(glTF/PbrMetallicRoughness) 양쪽 프로퍼티 이름을 다 본다 —
        // GLB가 어느 셰이더로 임포트됐는지는 프로젝트 설정에 따라 갈린다.
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int GltfBaseColorId = Shader.PropertyToID("baseColorFactor");
        static readonly int MetallicId = Shader.PropertyToID("_Metallic");
        static readonly int GltfMetallicId = Shader.PropertyToID("metallicFactor");
        static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        static readonly int GltfRoughnessId = Shader.PropertyToID("roughnessFactor");

        /// <summary>강철 색조. 알베도 텍스처에 곱해지므로 원래 무늬가 금속 각인처럼 남는다.</summary>
        static readonly Color SteelTint = new Color(0.62f, 0.66f, 0.72f);

        /// <summary>
        /// 기계 동물 — 팀1은 같은 동물 모델을 쓰되 표면만 금속으로 바꾼다.
        ///
        /// 텍스처를 지우지 않는 것이 요령이다. 지우면 밋밋한 회색 덩어리가 되지만,
        /// 남기면 털·깃 무늬가 금속 표면의 패널 라인처럼 읽혀 "기계 너구리"가 된다.
        /// 금속감은 색이 아니라 metallic·smoothness가 만든다 — 주변을 반사해야 쇠로 보인다.
        ///
        /// 원본 머티리얼당 금속 사본을 한 번만 만들어 캐시하고 sharedMaterials로 붙인다.
        /// r.materials를 쓰면 렌더러마다 인스턴스가 새로 생기는데, 유닛은 라운드마다
        /// 재생성되므로 그 사본들이 계속 쌓인다(파괴되지 않는다). 원본 에셋은 건드리지 않으므로
        /// 같은 모델을 쓰는 동물팀은 영향받지 않는다.
        /// </summary>
        static readonly Dictionary<Material, Material> metalCache = new();

        static void MakeMachine(GameObject skin)
        {
            foreach (var r in skin.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                var src = r.sharedMaterials;
                var outMats = new Material[src.Length];
                for (int i = 0; i < src.Length; i++) outMats[i] = MetalVersionOf(src[i]);
                r.sharedMaterials = outMats;
            }
        }

        static Material MetalVersionOf(Material src)
        {
            if (src == null) return null;
            if (metalCache.TryGetValue(src, out var cached) && cached != null) return cached;

            var m = new Material(src); // 텍스처·셰이더는 그대로 이어받는다
            if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, Tint(m.GetColor(BaseColorId)));
            else if (m.HasProperty(GltfBaseColorId)) m.SetColor(GltfBaseColorId, Tint(m.GetColor(GltfBaseColorId)));

            if (m.HasProperty(MetallicId)) m.SetFloat(MetallicId, 0.85f);
            if (m.HasProperty(GltfMetallicId)) m.SetFloat(GltfMetallicId, 0.85f);

            if (m.HasProperty(SmoothnessId)) m.SetFloat(SmoothnessId, 0.6f);
            if (m.HasProperty(GltfRoughnessId)) m.SetFloat(GltfRoughnessId, 0.4f); // roughness = 1 - smoothness

            metalCache[src] = m;
            return m;
        }

        /// <summary>원래 색의 명암은 살리고 채도만 죽여 강철 색조를 입힌다.</summary>
        static Color Tint(Color c)
        {
            float lum = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
            return new Color(SteelTint.r * lum, SteelTint.g * lum, SteelTint.b * lum, c.a);
        }

        /// <summary>모델 변형(<이름>_anim/_atk) 로드 + 배치 + 클립 재생. 없으면 null.</summary>
        GameObject LoadVariant(string path, GameObject skin, UnitView view, float height,
            UnitClass cls, out SkinLoopAnimator anim)
        {
            anim = null;
            var prefab = Resources.Load<GameObject>(path);
            if (prefab == null) return null;

            var go = Instantiate(prefab, skin.transform);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            FitToUnit(go, view.transform, height);

            var clips = Resources.LoadAll<AnimationClip>(path);
            if (clips.Length > 0)
            {
                anim = go.AddComponent<SkinLoopAnimator>();
                anim.Init(clips[0], AnimSpeed[cls]);
            }
            return go;
        }

        GameObject LoadModel(string resource)
        {
            if (cache.TryGetValue(resource, out var p)) return p;
            p = Resources.Load<GameObject>(resource);
            if (p == null)
                Debug.LogWarning($"[UnitSkins] Resources에 {resource} 없음 — 큐브 유지");
            cache[resource] = p; // null도 캐시해서 경고 1회만
            return p;
        }

        static void FitToUnit(GameObject skin, Transform unit, float targetHeight)
        {
            var rends = skin.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return;

            var b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            if (b.size.y > 0.001f)
                skin.transform.localScale *= targetHeight / b.size.y;

            // 스케일 반영 후 다시 재서 발바닥을 칸 바닥에, 몸통 중심을 유닛 축에 맞춤
            b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            var pos = skin.transform.position;
            pos.y += (unit.position.y - UnitYOffset) - b.min.y;
            pos.x += unit.position.x - b.center.x;
            pos.z += unit.position.z - b.center.z;
            skin.transform.position = pos;
        }
    }
}
