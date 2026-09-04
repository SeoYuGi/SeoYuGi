using UnityEngine;
using UnityEditor;

namespace Juicer.Editors
{
    [CustomEditor(typeof(Juicer_ScreenShake))]
    [CanEditMultipleObjects]
    public class Juicer_ScreenShakeInspector : Editor
    {
        private SerializedProperty affectX;
        private SerializedProperty affectY;
        private SerializedProperty shakeType;
        private SerializedProperty smoothShakeRate;

        void OnEnable ()
        {
            affectX = serializedObject.FindProperty("affectX");
            affectY = serializedObject.FindProperty("affectY");
            shakeType = serializedObject.FindProperty("shakeType");
            smoothShakeRate = serializedObject.FindProperty("smoothShakeRate");
        }

        public override void OnInspectorGUI ()
        {
            serializedObject.Update();

            EditorGUILayout.BeginVertical("box");

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Screen Shake", EditorStyles.boldLabel);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(5);

            EditorGUILayout.PropertyField(affectX, new GUIContent("Affect X"));
            EditorGUILayout.PropertyField(affectY, new GUIContent("Affect Y"));
            EditorGUILayout.PropertyField(shakeType, new GUIContent("Shake Type"));

            if(shakeType.enumValueIndex == 1)
            {
                EditorGUILayout.PropertyField(smoothShakeRate, new GUIContent("Smooth Shake Rate"));
            }

            EditorGUILayout.EndVertical();

            serializedObject.ApplyModifiedProperties();
        }
    }
}