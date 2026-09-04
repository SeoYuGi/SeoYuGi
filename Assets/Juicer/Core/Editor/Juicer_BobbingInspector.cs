using UnityEngine;
using UnityEditor;

namespace Juicer.Editors
{
    [CustomEditor(typeof(Juicer_Bobbing))]
    [CanEditMultipleObjects]
    public class Juicer_BobbingInspector : Editor
    {
        private SerializedProperty bobSpeed;
        private SerializedProperty bobTargetOffset;

        private SerializedProperty timeScale;
        private SerializedProperty bobType;

        private SerializedProperty customBobCurve;

        private SerializedProperty fakeRotation;
        private SerializedProperty fakeRotationSpeed;
        private SerializedProperty fakeRotationType;

        private bool showAdvancedSettings = false;

        void OnEnable ()
        {
            bobSpeed = serializedObject.FindProperty("BobSpeed");
            bobTargetOffset = serializedObject.FindProperty("BobTargetOffset");

            timeScale = serializedObject.FindProperty("CurrentTimeScale");
            bobType = serializedObject.FindProperty("CurrentBobType");

            customBobCurve = serializedObject.FindProperty("CustomBobCurve");

            fakeRotation = serializedObject.FindProperty("EnableFakeRotation");
            fakeRotationSpeed = serializedObject.FindProperty("FakeRotationSpeed");
            fakeRotationType = serializedObject.FindProperty("CurrentFakeRotationType");
        }

        public override void OnInspectorGUI ()
        {
            serializedObject.Update();

            EditorGUILayout.BeginVertical("box");

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Bobbing", EditorStyles.boldLabel);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(5);

            EditorGUILayout.PropertyField(bobSpeed, new GUIContent("Speed"));
            EditorGUILayout.PropertyField(bobTargetOffset, new GUIContent("Target Offset"));
            EditorGUILayout.PropertyField(bobType, new GUIContent("Type"));

            if(bobType.enumValueIndex == 1)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(customBobCurve, new GUIContent("Custom Bob Curve"));
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();


            EditorGUILayout.BeginVertical("box");

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Fake Rotation", EditorStyles.boldLabel);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(5);

            EditorGUILayout.PropertyField(fakeRotation, new GUIContent("Enabled"));

            if(fakeRotation.boolValue == true)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(fakeRotationSpeed, new GUIContent("Speed"));
                EditorGUILayout.PropertyField(fakeRotationType, new GUIContent("Type"));
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();


            EditorGUILayout.BeginVertical("box");

            EditorGUI.indentLevel++;

            showAdvancedSettings = EditorGUILayout.Foldout(showAdvancedSettings, "Advanced Settings", true);

            if(showAdvancedSettings)
            {
                EditorGUILayout.PropertyField(timeScale, new GUIContent("Time Scale"));
            }

            EditorGUI.indentLevel--;

            EditorGUILayout.EndVertical();

            serializedObject.ApplyModifiedProperties();
        }
    }
}