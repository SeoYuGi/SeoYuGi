using UnityEngine;
using UnityEditor;

namespace Juicer.Editors
{
    [CustomEditor(typeof(Juicer_MoveWobble))]
    [CanEditMultipleObjects]
    public class Juicer_MoveWobbleInspector : Editor
    {
        private SerializedProperty frequency;
        private SerializedProperty amplitude;
        private SerializedProperty threshold;
        private SerializedProperty smoothing;

        void OnEnable ()
        {
            frequency = serializedObject.FindProperty("Frequency");
            amplitude = serializedObject.FindProperty("Amplitude");
            threshold = serializedObject.FindProperty("Threshold");
            smoothing = serializedObject.FindProperty("Smoothing");
        }

        public override void OnInspectorGUI ()
        {
            serializedObject.Update();

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Move Wobble", EditorStyles.boldLabel);
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(5);

            EditorGUILayout.PropertyField(frequency, new GUIContent("Frequency"));
            EditorGUILayout.PropertyField(amplitude, new GUIContent("Amplitude"));
            EditorGUILayout.PropertyField(threshold, new GUIContent("Threshold"));
            EditorGUILayout.PropertyField(smoothing, new GUIContent("Smoothing"));

            EditorGUILayout.EndVertical();

            serializedObject.ApplyModifiedProperties();
        }
    }
}