using UnityEngine;
using UnityEditor;

namespace Juicer.Editors
{
    [CustomEditor(typeof(Juicer_SquashAndStretch))]
    [CanEditMultipleObjects]
    public class Juicer_SquashAndStretchInspector : Editor
    {
        private SerializedProperty affect;
        private SerializedProperty maxStretch;
        private SerializedProperty duration;
        private SerializedProperty animateCurve;

        void OnEnable ()
        {
            affect = serializedObject.FindProperty("affect");
            maxStretch = serializedObject.FindProperty("maxStretch");
            duration = serializedObject.FindProperty("duration");
            animateCurve = serializedObject.FindProperty("animateCurve");
        }

        public override void OnInspectorGUI ()
        {
            serializedObject.Update();

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Squash and Stretch", EditorStyles.boldLabel);
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(5);

            EditorGUILayout.PropertyField(affect, new GUIContent("Affect"));
            EditorGUILayout.PropertyField(maxStretch, new GUIContent("Max Stretch"));
            EditorGUILayout.PropertyField(duration, new GUIContent("Duration"));
            EditorGUILayout.PropertyField(animateCurve, new GUIContent("Animate Curve"));

            EditorGUILayout.EndVertical();

            serializedObject.ApplyModifiedProperties();
        }
    }
}