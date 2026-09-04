using UnityEngine;
using UnityEditor;

namespace Juicer.Editors
{
    [CustomEditor(typeof(Juicer_HitFlash))]
    [CanEditMultipleObjects]
    public class Juicer_HitFlashInspector : Editor
    {
        private SerializedProperty spriteRenderer;
        private SerializedProperty flashColor;
        private SerializedProperty flashDuration;
        private SerializedProperty flashCurve;
        private SerializedProperty affectChildren;

        private SerializedProperty requiredShader;

        void OnEnable ()
        {
            spriteRenderer = serializedObject.FindProperty("spriteRenderer");
            flashColor = serializedObject.FindProperty("flashColor");
            flashDuration = serializedObject.FindProperty("flashDuration");
            flashCurve = serializedObject.FindProperty("flashCurve");
            affectChildren = serializedObject.FindProperty("affectChildren");

            requiredShader = serializedObject.FindProperty("requiredShader");
        }

        public override void OnInspectorGUI ()
        {
            serializedObject.Update();

            EditorGUILayout.BeginVertical("box");
            
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Hit Flash", EditorStyles.boldLabel);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(5);

            EditorGUILayout.PropertyField(spriteRenderer, new GUIContent("Sprite Renderer"));
            EditorGUILayout.PropertyField(flashColor, new GUIContent("Color"));
            EditorGUILayout.PropertyField(flashDuration, new GUIContent("Duration"));
            EditorGUILayout.PropertyField(flashCurve, new GUIContent("Falloff Curve"));
            EditorGUILayout.PropertyField(affectChildren, new GUIContent("Affect Children"));

            EditorGUILayout.EndVertical();

            if(spriteRenderer.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox("A Sprite Renderer needs to be assigned.", MessageType.Warning);
            }
            else if((spriteRenderer.objectReferenceValue as SpriteRenderer).sharedMaterial.shader != requiredShader.objectReferenceValue as Shader)
            {
                EditorGUILayout.HelpBox("The Sprite Renderer needs a material with the 'Juicer_SpriteUnlit' shader to function.", MessageType.Warning);
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}