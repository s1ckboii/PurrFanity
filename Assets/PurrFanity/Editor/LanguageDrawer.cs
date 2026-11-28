using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace PurrFanity.Editor
{
    [CustomPropertyDrawer(typeof(LanguageAttribute))]
    internal sealed class LanguageDrawer : PropertyDrawer
    {
        private const string ListsFolder = "/Resources/PurrFanity/WordLists/";

        private static List<string> _codes;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            var none = ((LanguageAttribute)attribute).None;
            var codes = new List<string>(Codes());

            if (none != null)
                codes.Insert(0, string.Empty);

            // A code without word lists in this project still shows, so it isnt lost
            var current = property.stringValue ?? string.Empty;

            if (!codes.Contains(current))
                codes.Add(current);

            var names = new GUIContent[codes.Count];

            for (var i = 0; i < codes.Count; i++)
                names[i] = new GUIContent(codes[i].Length == 0 ? none ?? "None" : Name(codes[i]));

            EditorGUI.BeginProperty(position, label, property);
            int picked = EditorGUI.Popup(position, label, codes.IndexOf(current), names);

            if (picked >= 0 && codes[picked] != current)
                property.stringValue = codes[picked];

            EditorGUI.EndProperty();
        }

        private static List<string> Codes()
        {
            if (_codes != null)
                return _codes;

            _codes = FindCodes();
            EditorApplication.projectChanged -= Forget;
            EditorApplication.projectChanged += Forget;
            return _codes;
        }

        // Every folder of word lists, built-in and your own
        private static List<string> FindCodes()
        {
            var codes = new SortedSet<string>();
            var guids = AssetDatabase.FindAssets("t:TextAsset");

            for (var i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                int at = path.IndexOf(ListsFolder, StringComparison.Ordinal);

                if (at < 0)
                    continue;

                int start = at + ListsFolder.Length;
                int slash = path.IndexOf('/', start);

                if (slash > start)
                    codes.Add(path.Substring(start, slash - start));
            }

            return new List<string>(codes);
        }

        private static void Forget()
        {
            _codes = null;
        }

        // English (en)
        internal static string Name(string code)
        {
            if (string.IsNullOrEmpty(code))
                return "(empty)";

            try
            {
                var culture = CultureInfo.GetCultureInfo(code);

                if (!string.IsNullOrEmpty(culture.EnglishName) && culture.EnglishName != code)
                    return $"{culture.EnglishName} ({code})";
            }
            catch (CultureNotFoundException)
            {
                
            }

            return code;
        }
    }
}