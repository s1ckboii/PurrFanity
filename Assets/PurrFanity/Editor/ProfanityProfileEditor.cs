using UnityEditor;

namespace PurrFanity.Editor
{
    // Adds a Try it box to the profile inspector
    [CustomEditor(typeof(ProfanityProfile))]
    public class ProfanityProfileEditor : UnityEditor.Editor
    {
        private string _testText = string.Empty;
        private string _testLanguage = string.Empty;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var profile = (ProfanityProfile)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Try it", EditorStyles.boldLabel);

            _testText = EditorGUILayout.TextField("Text", _testText);

            if (profile.Languages != null && profile.Languages.Length > 1)
                _testLanguage = PickLanguage(profile.Languages, _testLanguage);

            if (!profile.Enabled)
            {
                EditorGUILayout.HelpBox("Turned off, text passes through unchanged.", MessageType.None);
                return;
            }

            var result = profile.Filter.Check(_testText, null, _testLanguage);

            if (result.blocked)
                EditorGUILayout.HelpBox($"Blocked. Matched: {result.categories}", MessageType.Warning);
            else if (result.hasProfanity)
                EditorGUILayout.HelpBox($"{result.filtered}\n\nMatched: {result.categories}", MessageType.Info);
            else
                EditorGUILayout.HelpBox("Nothing to filter.", MessageType.None);

            EditorGUILayout.LabelField("Words loaded", profile.Filter.dictionary.Count.ToString());
        }

        // The profile's languages, or none to see what the Fallback Language does
        private static string PickLanguage(string[] languages, string current)
        {
            var names = new string[languages.Length + 1];
            names[0] = "None (uses Fallback Language)";
            int selected = 0;

            for (var i = 0; i < languages.Length; i++)
            {
                names[i + 1] = LanguageDrawer.Name(languages[i]);

                if (languages[i] == current)
                    selected = i + 1;
            }

            int picked = EditorGUILayout.Popup("Sender Language", selected, names);
            return picked == 0 ? string.Empty : languages[picked - 1];
        }
    }
}