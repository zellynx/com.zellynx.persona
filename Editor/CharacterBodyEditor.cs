using UnityEditor;
using UnityEngine;

namespace Character.Mobility.Editor
{
    [CustomEditor(typeof(CharacterBody))] [CanEditMultipleObjects]
    internal sealed class CharacterBodyEditor : UnityEditor.Editor
    {
        private SerializedProperty _geometryType;
        private SerializedProperty _capsuleGeometrySettings;
        private SerializedProperty _sphereGeometrySettings;
        private SerializedProperty _boxGeometrySettings;
        private SerializedProperty _settings;

        private void OnEnable() {
            _geometryType = serializedObject.FindProperty("_geometryType");
            _capsuleGeometrySettings = serializedObject.FindProperty("_capsuleGeometrySettings");
            _sphereGeometrySettings = serializedObject.FindProperty("_sphereGeometrySettings");
            _boxGeometrySettings = serializedObject.FindProperty("_boxGeometrySettings");

            _settings = serializedObject.FindProperty("_settings");
        }

        public override void OnInspectorGUI() {
            serializedObject.Update();

            using (new EditorGUI.DisabledScope(true)) {
                var script = MonoScript.FromMonoBehaviour((CharacterBody)target);
                EditorGUILayout.ObjectField("Script", script, typeof(MonoScript), false);
            }

            EditorGUILayout.PropertyField(_geometryType);

            
            EditorGUILayout.Space();

            var geometryType = (GeometryTypes)_geometryType.enumValueIndex;
            switch (geometryType) {
                case GeometryTypes.Capsule:
                    EditorGUILayout.PropertyField(_capsuleGeometrySettings, new GUIContent("Capsule Shape"), true);
                    break;
                case GeometryTypes.Sphere:
                    EditorGUILayout.PropertyField(_sphereGeometrySettings, new GUIContent("Sphere Shape"), true);
                    break;
                case GeometryTypes.Box:
                    EditorGUILayout.PropertyField(_boxGeometrySettings, new GUIContent("Box Shape"), true);
                    break;
            }

            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(_settings, new GUIContent("Settings"), true);

            if (geometryType == GeometryTypes.Capsule) {
                EditorGUILayout.HelpBox("CharacterBody uses an upright Y-axis capsule for Character locomotion.", MessageType.Info);
            }
            else {
                EditorGUILayout.HelpBox("CharacterBody manages the active collider component for the selected shape. " +
                    "A box always remains upright relative to the character's basis Up; it may only rotate around that axis.", MessageType.Info);
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}