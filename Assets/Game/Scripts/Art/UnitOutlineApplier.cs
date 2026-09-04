using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using SeoYuGi.BattleView;

namespace SeoYuGi.Art
{
    /// <summary>
    /// 모든 유닛 스킨에 검은 외곽선을 씌운다 — 바닥·안개·이펙트에서 캐릭터 실루엣을 떼어내는 가독성 장치.
    /// 방식: 렌더러 머티리얼 배열 끝에 외곽선 패스(SeoYuGi/UnitOutline, Cull Front)를 한 장 더 얹는다.
    /// 스킨드 메시·정적 메시 둘 다 그대로 먹고, 씬 배선·프리팹 수정 없음. UnitSkinApplier가 스킨을
    /// 나중에 갈아끼워도(대기↔이동 교체) 매 프레임 새 렌더러를 잡아 씌운다.
    /// </summary>
    public static class UnitOutlineBootstrap
    {
        static readonly string[] SceneNames = { "SampleScene", "GoraniSkinTest" };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            if (System.Array.IndexOf(SceneNames, SceneManager.GetActiveScene().name) < 0) return;
            new GameObject("UnitOutlineApplier").AddComponent<UnitOutlineApplier>();
        }
    }

    public class UnitOutlineApplier : MonoBehaviour
    {
        const string SkinName = "Skin"; // UnitSkinApplier가 만드는 자식 이름 — 그 아래 렌더러만 대상

        // 팀별 외곽선 — 내 팀 파랑 / 적 빨강 (팀 색 언어). 검은 선보다 "누구 편"이 한 번에 읽힌다.
        static readonly Color AllyOutline = new Color(0.2f, 0.55f, 1f);
        static readonly Color EnemyOutline = new Color(1f, 0.22f, 0.14f);
        static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");

        Material allyMat, enemyMat;
        BattleRunner runner;
        readonly HashSet<Renderer> done = new HashSet<Renderer>();

        void Awake()
        {
            var shader = Shader.Find("SeoYuGi/UnitOutline");
            if (shader == null)
            {
                Debug.LogWarning("[UnitOutline] SeoYuGi/UnitOutline 셰이더 없음 — 외곽선 생략");
                enabled = false;
                return;
            }
            allyMat = new Material(shader);
            allyMat.SetColor(OutlineColorId, AllyOutline);
            enemyMat = new Material(shader);
            enemyMat.SetColor(OutlineColorId, EnemyOutline);
        }

        void LateUpdate()
        {
            if (runner == null) runner = FindFirstObjectByType<BattleRunner>();
            if (runner == null || runner.Battle == null) return;
            var me = runner.Battle.GetUnit(runner.PlayerUnitId);
            int playerTeam = me != null ? me.team : 0;

            foreach (var view in FindObjectsByType<UnitView>(FindObjectsSortMode.None))
            {
                var skin = view.transform.Find(SkinName);
                if (skin == null) continue; // 스킨 미도착(폴백 큐브) — 외곽선 없이 둔다
                var unit = runner.Battle.GetUnit(view.UnitId);
                var mat = unit != null && unit.team == playerTeam ? allyMat : enemyMat;
                foreach (var r in skin.GetComponentsInChildren<Renderer>(true))
                {
                    if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                    if (!done.Add(r)) continue;
                    var mats = r.sharedMaterials;
                    var withOutline = new Material[mats.Length + 1];
                    mats.CopyTo(withOutline, 0);
                    withOutline[mats.Length] = mat; // 마지막 서브메시에 추가 패스로 그려진다
                    r.sharedMaterials = withOutline;
                }
            }
            done.RemoveWhere(r => r == null); // 라운드 전환으로 파괴된 렌더러 정리
        }
    }
}
