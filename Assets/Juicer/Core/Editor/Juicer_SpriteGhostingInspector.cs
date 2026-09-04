using UnityEngine;
using UnityEditor;

namespace Juicer.Editors
{
    [CustomEditor(typeof(Juicer_SpriteGhost))]
    [CanEditMultipleObjects]
    public class Juicer_SpriteGhostInspector : Editor
    {
        private SerializedProperty sampleType;
        private SerializedProperty samplesOverTime;
        private SerializedProperty samplesOverDistance;
        private SerializedProperty lifetime;
        private SerializedProperty startOnAwake;

        private SerializedProperty enableAlphaOverTime;
        private SerializedProperty alphaOverTime;

        private SerializedProperty enableColorOverTime;
        private SerializedProperty colorOverTime;

        private SerializedProperty enableSizeOverTime;
        private SerializedProperty sizeOverTime;

        private SerializedProperty poolGhostSprites;

        private bool showAdvancedSettings = false;

        void OnEnable()
        {
            sampleType = serializedObject.FindProperty("CurrentSampleType");
            samplesOverTime = serializedObject.FindProperty("SamplesOverTime");
            samplesOverDistance = serializedObject.FindProperty("SamplesOverDistance");
            lifetime = serializedObject.FindProperty("Lifetime");
            startOnAwake = serializedObject.FindProperty("StartOnAwake");

            enableAlphaOverTime = serializedObject.FindProperty("EnableAlphaOverTime");
            alphaOverTime = serializedObject.FindProperty("AlphaOverTime");

            enableColorOverTime = serializedObject.FindProperty("EnableColorOverTime");
            colorOverTime = serializedObject.FindProperty("ColorOverTime");

            enableSizeOverTime = serializedObject.FindProperty("EnableSizeOverTime");
            sizeOverTime = serializedObject.FindProperty("SizeOverTime");

            poolGhostSprites = serializedObject.FindProperty("PoolGhostSprites");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Sprite Ghosting", EditorStyles.boldLabel);
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(5);

            EditorGUILayout.PropertyField(sampleType, new GUIContent("Sample Type"));

            if(sampleType.enumValueIndex == 0)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(samplesOverTime, new GUIContent("Ghosts Over Time"));
                EditorGUI.indentLevel--;
            }
            else if(sampleType.enumValueIndex == 1)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(samplesOverDistance, new GUIContent("Ghosts Over Distance"));
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.PropertyField(lifetime, new GUIContent("Lifetime"));
            EditorGUILayout.PropertyField(startOnAwake, new GUIContent("Start on Awake"));
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Animate Over Time", EditorStyles.boldLabel);
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(5);

            // Alpha over time
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.PropertyField(enableAlphaOverTime, new GUIContent("Alpha"));

            if(enableAlphaOverTime.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(alphaOverTime, new GUIContent("Alpha Curve"));
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndVertical();

            // Color over time
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.PropertyField(enableColorOverTime, new GUIContent("Color"));

            if(enableColorOverTime.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(colorOverTime, new GUIContent("Color"));
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndVertical();

            // Size over time
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.PropertyField(enableSizeOverTime, new GUIContent("Size"));

            if(enableSizeOverTime.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(sizeOverTime, new GUIContent("Size Curve"));
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndVertical();

            // Advanced settings
            EditorGUILayout.BeginVertical("box");
            EditorGUI.indentLevel++;

            showAdvancedSettings = EditorGUILayout.Foldout(showAdvancedSettings, "Advanced Settings", true);

            if(showAdvancedSettings)
            {
                EditorGUILayout.PropertyField(poolGhostSprites, new GUIContent("Enable Ghost Pooling"));
            }

            EditorGUI.indentLevel--;
            EditorGUILayout.EndVertical();

            serializedObject.ApplyModifiedProperties();
        }
    }
}
