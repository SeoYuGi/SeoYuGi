using UnityEngine;
using UnityEditor;

namespace Juicer.Editors
{
    [CustomEditor(typeof(Juicer_Parallax))]
    [CanEditMultipleObjects]
    public class Juicer_ParallaxInspector : Editor
    {
        private SerializedProperty referenceType;
        private SerializedProperty referenceComponent;

        private SerializedProperty xDistance;
        private SerializedProperty yDistance;

        private SerializedProperty infiniteScrolling;
        private SerializedProperty scrollX;
        private SerializedProperty scrollY;
        private SerializedProperty scrollXThreshold;
        private SerializedProperty scrollYThreshold;

        private SerializedProperty updateMethod;
        private SerializedProperty customUpdateMethodRate;

        private bool showAdvancedSettings = false;

        void OnEnable ()
        {
            referenceType = serializedObject.FindProperty("referenceType");
            referenceComponent = serializedObject.FindProperty("referenceComponent");

            xDistance = serializedObject.FindProperty("xDistance");
            yDistance = serializedObject.FindProperty("yDistance");

            infiniteScrolling = serializedObject.FindProperty("infiniteScrolling");
            scrollX = serializedObject.FindProperty("scrollX");
            scrollY = serializedObject.FindProperty("scrollY");
            scrollXThreshold = serializedObject.FindProperty("scrollXThreshold");
            scrollYThreshold = serializedObject.FindProperty("scrollYThreshold");

            updateMethod = serializedObject.FindProperty("updateMethod");
            customUpdateMethodRate = serializedObject.FindProperty("customUpdateMethodRate");
        }

        public override void OnInspectorGUI ()
        {
            serializedObject.Update();

            EditorGUILayout.BeginVertical("box");

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Parallax", EditorStyles.boldLabel);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(5);

            EditorGUILayout.PropertyField(referenceType, new GUIContent("Reference Type"));

            if(referenceType.enumValueIndex == 1)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(referenceComponent, new GUIContent("Reference Component"));
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.PropertyField(xDistance, new GUIContent("X Distance"));
            EditorGUILayout.PropertyField(yDistance, new GUIContent("Y Distance"));

            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical("box");

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Infinite Scrolling", EditorStyles.boldLabel);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(5);

            EditorGUILayout.PropertyField(infiniteScrolling, new GUIContent("Enabled"));

            if(infiniteScrolling.boolValue)
            {
                EditorGUI.indentLevel++;

                EditorGUILayout.PropertyField(scrollX, new GUIContent("X Axis"));

                if(scrollX.boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(scrollXThreshold, new GUIContent("Threshold"));
                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.PropertyField(scrollY, new GUIContent("Y Axis"));

                if(scrollY.boolValue)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(scrollYThreshold, new GUIContent("Threshold"));
                    EditorGUI.indentLevel--;
                }

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical("box");

            EditorGUI.indentLevel++;

            showAdvancedSettings = EditorGUILayout.Foldout(showAdvancedSettings, "Advanced Settings", true);

            if(showAdvancedSettings)
            {
                EditorGUILayout.PropertyField(updateMethod, new GUIContent("Update Method"));

                if(updateMethod.enumValueIndex == 3)
                {
                    EditorGUILayout.PropertyField(customUpdateMethodRate, new GUIContent("Update Rate"));
                }
            }

            EditorGUI.indentLevel--;

            EditorGUILayout.EndVertical();

            serializedObject.ApplyModifiedProperties();
        }
    }
}
