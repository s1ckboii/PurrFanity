using UnityEditor;
using UnityEngine;

namespace PurrNet.PurrFanity.Editor
{
    // First time in a project, makes a Default profile and a word list in Resources/PurrFanity
    internal static class PurrFanitySetup
    {
        private const string Folder = "Assets/Resources/PurrFanity";
        private static string doneKey => "PurrFanity.SetupDone:" + Application.dataPath;

        [InitializeOnLoadMethod]
        private static void Schedule()
        {
            if (Application.isBatchMode || EditorPrefs.GetBool(doneKey))
                return;

            EditorApplication.delayCall += Run;
        }

        private static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            EditorPrefs.SetBool(doneKey, true);

            if (HasProfile())
                return;

            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");

            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets/Resources", "PurrFanity");

            var words = ScriptableObject.CreateInstance<ProfanityWordList>();
            AssetDatabase.CreateAsset(words, Folder + "/Custom Words.asset");

            var profile = ScriptableObject.CreateInstance<ProfanityProfile>();
            profile.AddWordList(words);
            AssetDatabase.CreateAsset(profile, Folder + "/Default.asset");
            AssetDatabase.SaveAssets();

            bool show = EditorUtility.DisplayDialog("PurrFanity", "Created a Default profile and a Custom Words list in " + Folder + ".\n\n" +
                "The Default profile is used whenever no other profile is given. Pick what gets filtered " +
                "and how in its inspector, and add your own words to Custom Words.",
                "Show me", "OK");

            if (!show)
                return;

            Selection.activeObject = profile;
            EditorGUIUtility.PingObject(profile);
        }

        private static bool HasProfile()
        {
            var guids = AssetDatabase.FindAssets("t:" + nameof(ProfanityProfile));

            for (var i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);

                if (path.Contains("/Resources/" + PurrFanity.ResourcesFolder + "/"))
                    return true;
            }

            return false;
        }
    }
}