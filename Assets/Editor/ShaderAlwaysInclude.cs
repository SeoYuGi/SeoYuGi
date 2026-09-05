using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// 빌드 쉐이더 깨짐(분홍) 수정 — 코드가 Shader.Find로만 쓰는 쉐이더는 에셋 참조가 없어
/// 빌드에서 스트립되고, Find가 null을 돌려줘 머티리얼이 깨진다 (에디터에선 멀쩡).
/// GraphicsSettings의 Always Included Shaders에 등록해 강제 포함시킨다.
/// 메뉴로 1회 실행 + 빌드 시작 시 자동 검사(IPreprocessBuildWithReport) 이중 안전장치.
/// </summary>
public class ShaderAlwaysInclude : IPreprocessBuildWithReport
{
    // 코드에서 Shader.Find로 쓰는 전량 (grep 기준 2026-09-06)
    static readonly string[] Needed =
    {
        "Sprites/Default",                  // VFX·링·안개·화살표 등 절차 생성 머티리얼 대부분
        "Universal Render Pipeline/Unlit",  // 유닛 틴트·격자·잔상
        "Universal Render Pipeline/Lit",    // 기계팀 은색 금속 재질
        "SeoYuGi/UnitOutline",              // 유닛 외곽선 커스텀
        "UI/Default",                       // uGUI 기본 — 런타임 생성 UI 안전망
    };

    [MenuItem("SeoYuGi/Build/Fix Always Included Shaders")]
    public static void Fix()
    {
        int added = Ensure();
        Debug.Log(added > 0
            ? $"Always Included Shaders에 {added}종 추가 — 빌드에서 더는 스트립되지 않는다"
            : "Always Included Shaders 이상 없음 (전부 이미 등록됨)");
    }

    /// <summary>빌드 직전 자동 검사 — 등록이 빠져 있으면 채워 넣고 빌드를 계속한다.</summary>
    public int callbackOrder => 0;
    public void OnPreprocessBuild(BuildReport report)
    {
        int added = Ensure();
        if (added > 0) Debug.Log($"빌드 전 자동 등록: Always Included Shaders에 {added}종 추가");
    }

    static int Ensure()
    {
        var gs = AssetDatabase.LoadAssetAtPath<Object>("ProjectSettings/GraphicsSettings.asset");
        if (gs == null) { Debug.LogWarning("GraphicsSettings.asset을 찾지 못했다"); return 0; }
        var so = new SerializedObject(gs);
        var arr = so.FindProperty("m_AlwaysIncludedShaders");
        if (arr == null) { Debug.LogWarning("m_AlwaysIncludedShaders 프로퍼티가 없다"); return 0; }

        int added = 0;
        foreach (var name in Needed)
        {
            var shader = Shader.Find(name);
            if (shader == null) { Debug.LogWarning($"쉐이더 없음(이름 확인 필요): {name}"); continue; }

            bool exists = false;
            for (int i = 0; i < arr.arraySize; i++)
                if (arr.GetArrayElementAtIndex(i).objectReferenceValue == shader) { exists = true; break; }
            if (exists) continue;

            arr.InsertArrayElementAtIndex(arr.arraySize);
            arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = shader;
            added++;
        }
        if (added > 0)
        {
            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
        }
        return added;
    }
}
