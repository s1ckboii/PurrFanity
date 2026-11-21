using UnityEditor;

namespace PurrNet.PurrFanity.Editor
{
    // Adds a Try it box to the profile inspector
    [CustomEditor(typeof(ProfanityProfile))]
    public class ProfanityProfileEditor : UnityEditor.Editor
    {
        private string _testText = string.Empty;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var profile = (ProfanityProfile)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Try it", EditorStyles.boldLabel);

            _testText = EditorGUILayout.TextField("Text", _testText);

            var result = profile.filter.Check(_testText);

            if (result.blocked)
                EditorGUILayout.HelpBox($"Blocked. Matched: {result.categories}", MessageType.Warning);
            else if (result.hasProfanity)
                EditorGUILayout.HelpBox($"{result.filtered}\n\nMatched: {result.categories}", MessageType.Info);
            else
                EditorGUILayout.HelpBox("Nothing to filter.", MessageType.None);

            EditorGUILayout.LabelField("Words loaded", profile.filter.dictionary.count.ToString());
        }
    }
}