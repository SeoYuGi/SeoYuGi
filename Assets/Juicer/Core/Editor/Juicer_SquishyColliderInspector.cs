using UnityEngine;
using UnityEditor;

namespace Juicer.Editors
{
    [CustomEditor(typeof(Juicer_SquishyCollider))]
    [CanEditMultipleObjects]
    public class Juicer_SquishyColliderInspector : Editor
    {
        private SerializedProperty influenceType;
        private SerializedProperty radius;
        private SerializedProperty strength;
        private SerializedProperty strengthCurve;
        private SerializedProperty layerMask;
        private SerializedProperty updateType;
        private SerializedProperty customUpdateRate;

        private bool showAdvancedSettings = false;
        private bool drawRadius = true;

        void OnEnable ()
        {
            influenceType = serializedObject.FindProperty("influenceType");
            radius = serializedObject.FindProperty("Radius");
            strength = serializedObject.FindProperty("Strength");
            strengthCurve = serializedObject.FindProperty("StrengthCurve");
            layerMask = serializedObject.FindProperty("CollisionLayerMask");
            updateType = serializedObject.FindProperty("updateType");
            customUpdateRate = serializedObject.FindProperty("CustomUpdateRate");
        }

        public override void OnInspectorGUI ()
        {
            serializedObject.Update();

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Squishy Collider", EditorStyles.boldLabel);
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(5);

            EditorGUILayout.PropertyField(influenceType, new GUIContent("Influence Type"));
            EditorGUILayout.PropertyField(radius, new GUIContent("Radius"));
            EditorGUILayout.PropertyField(strength, new GUIContent("Strength"));
            EditorGUILayout.PropertyField(strengthCurve, new GUIContent("Strength Curve"));
            EditorGUILayout.PropertyField(layerMask, new GUIContent("Layer Mask"));

            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical("box");
            EditorGUI.indentLevel++;

            showAdvancedSettings = EditorGUILayout.Foldout(showAdvancedSettings, "Advanced Settings", true);

            if(showAdvancedSettings)
            {
                EditorGUILayout.PropertyField(updateType, new GUIContent("Update Type"));

                if(updateType.enumValueIndex == 2)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(customUpdateRate, new GUIContent("Custom Update Rate"));
                    EditorGUI.indentLevel--;
                }

                drawRadius = EditorGUILayout.Toggle("Draw Radius", drawRadius);
            }

            EditorGUI.indentLevel--;
            EditorGUILayout.EndVertical();

            serializedObject.ApplyModifiedProperties();
        }

        void OnSceneGUI ()
        {
            if(!drawRadius)
                return;

            Juicer_SquishyCollider comp = (Juicer_SquishyCollider)target;

            Handles.color = Color.yellow;
            Handles.DrawWireDisc(comp.transform.position, Vector3.forward, comp.Radius / 2, 2.0f);
        }
    }
}