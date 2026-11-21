using System;
using System.Collections.Generic;
using UnityEngine;

namespace PurrFanity
{
    /// <summary>
    /// What gets filtered and how.
    /// </summary>
    [CreateAssetMenu(menuName = "PurrNet/PurrFanity/Profile", fileName = "PurrFanity Profile", order = -200)]
    public class ProfanityProfile : ScriptableObject
    {
        [Serializable]
        public struct CategoryAction
        {
            public ProfanityCategory category;
            public ProfanityAction action;
        }

        private static readonly ProfanityCategory[] _builtInCategories =
        {
            ProfanityCategory.Mild, ProfanityCategory.Strong, ProfanityCategory.Sexual, ProfanityCategory.Insults,
            ProfanityCategory.Racism, ProfanityCategory.Phobic, ProfanityCategory.Ableism
        };

        private static readonly string[] _builtInFiles =
        {
            "mild", "strong", "sexual", "insults", "racism", "phobic", "ableism"
        };

        private const string AllowFile = "allow";
        private const string CommonFile = "common";

        [Tooltip("Built-in languages to load, like en. Anyone is welcome to contribute more word lists!")]
        [SerializeField] private string[] _languages = { "en" };

        [Tooltip("Kinds of words that get filtered.")]
        [SerializeField] private ProfanityCategory _categories = ProfanityCategory.All;

        [Tooltip("What happens to a filtered word.")]
        [SerializeField] private ProfanityAction _action = ProfanityAction.Mask;

        [Tooltip("Categories with their own action, like blocking slurs.")]
        [SerializeField] private CategoryAction[] _categoryActions = Array.Empty<CategoryAction>();
        [SerializeField] private char _maskCharacter = '*';

        [Tooltip("The words Replace Silly picks from.")]
        [SerializeField] private string[] _sillyWords = (string[])ProfanityFilterSettings.defaultSillyWords.Clone();

        [Tooltip("Sees through tricks like sh1t, shiiit and s h i t.")]
        [SerializeField] private bool _detectEvasion = true;

        [Tooltip("Your own word lists.")]
        [SerializeField] private List<ProfanityWordList> _customLists = new List<ProfanityWordList>();

        [Tooltip("Words and phrases that are always fine, like homo sapiens.")]
        [SerializeField] private List<string> _allowedWords = new List<string>();

        [NonSerialized] private ProfanityFilter _filter;
        [NonSerialized] private int _builtVersion;

        /// <summary>
        /// Built on first use, and again after every change.
        /// </summary>
        public ProfanityFilter filter
        {
            get
            {
                if (_filter == null || _builtVersion != ProfanityWordList.changeCount)
                    Rebuild();
                return _filter;
            }
        }

        public ProfanityCategory categories
        {
            get => _categories;
            set
            {
                _categories = value;
                Invalidate();
            }
        }

        public ProfanityAction action
        {
            get => _action;
            set
            {
                _action = value;
                Invalidate();
            }
        }

        public char maskCharacter
        {
            get => _maskCharacter;
            set
            {
                _maskCharacter = value;
                Invalidate();
            }
        }

        public bool detectEvasion
        {
            get => _detectEvasion;
            set
            {
                _detectEvasion = value;
                Invalidate();
            }
        }

        public string[] languages
        {
            get => _languages;
            set
            {
                _languages = value;
                Invalidate();
            }
        }

        public string[] sillyWords
        {
            get => _sillyWords;
            set
            {
                _sillyWords = value;
                Invalidate();
            }
        }

        public IReadOnlyList<ProfanityWordList> customLists => _customLists;
        public IReadOnlyList<string> allowedWords => _allowedWords;

        /// <summary>
        /// Filters every built-in English category with asterisks.
        /// </summary>
        public static ProfanityProfile CreateDefault()
        {
            var profile = CreateInstance<ProfanityProfile>();
            profile.name = "Default (built-in)";
            profile.hideFlags = HideFlags.DontSave;
            return profile;
        }

        public void SetCategoryAction(ProfanityCategory category, ProfanityAction categoryAction)
        {
            for (var i = 0; i < _categoryActions.Length; i++)
            {
                if (_categoryActions[i].category != category)
                    continue;

                _categoryActions[i].action = categoryAction;
                Invalidate();
                return;
            }

            Array.Resize(ref _categoryActions, _categoryActions.Length + 1);
            _categoryActions[_categoryActions.Length - 1] = new CategoryAction
            {
                category = category,
                action = categoryAction
            };
            Invalidate();
        }

        public void AddWordList(ProfanityWordList list)
        {
            if (!list || _customLists.Contains(list))
                return;

            _customLists.Add(list);
            Invalidate();
        }

        public bool RemoveWordList(ProfanityWordList list)
        {
            if (!_customLists.Remove(list))
                return false;

            Invalidate();
            return true;
        }

        public void AllowWord(string word)
        {
            if (string.IsNullOrWhiteSpace(word) || _allowedWords.Contains(word))
                return;

            _allowedWords.Add(word);
            Invalidate();
        }

        public void Invalidate()
        {
            _filter = null;
        }

        /// <summary>
        /// Loads the word lists and builds the filter now instead of on first use.
        /// </summary>
        public void Rebuild()
        {
            var dictionary = new ProfanityDictionary();

            if (_languages != null)
            {
                for (var l = 0; l < _languages.Length; l++)
                    LoadLanguage(_languages[l], dictionary);
            }

            for (var i = 0; i < _customLists.Count; i++)
            {
                var list = _customLists[i];

                if (list && (_categories & list.category) != 0)
                    list.AddTo(dictionary);
            }

            for (var i = 0; i < _allowedWords.Count; i++)
                dictionary.Allow(_allowedWords[i]);

            var settings = new ProfanityFilterSettings
            {
                action = _action,
                maskCharacter = _maskCharacter,
                detectEvasion = _detectEvasion,
                sillyWords = _sillyWords
            };

            for (var i = 0; i < _categoryActions.Length; i++)
            {
                var entry = _categoryActions[i];

                for (var bit = 0; bit < 31; bit++)
                {
                    var single = (ProfanityCategory)(1 << bit);

                    if ((entry.category & single) != 0)
                        settings.SetCategoryAction(single, entry.action);
                }
            }

            _filter = new ProfanityFilter(dictionary, settings);
            _builtVersion = ProfanityWordList.changeCount;
        }

        private void LoadLanguage(string language, ProfanityDictionary dictionary)
        {
            if (string.IsNullOrWhiteSpace(language))
                return;

            var folder = $"{PurrFanity.ResourcesFolder}/WordLists/{language.Trim()}/";
            bool foundAny = false;

            for (var i = 0; i < _builtInCategories.Length; i++)
            {
                var asset = Resources.Load<TextAsset>(folder + _builtInFiles[i]);

                if (!asset)
                    continue;

                foundAny = true;

                if ((_categories & _builtInCategories[i]) != 0)
                    WordListParser.Parse(asset.text, _builtInCategories[i], dictionary);
            }

            var allow = Resources.Load<TextAsset>(folder + AllowFile);

            if (allow)
                WordListParser.ParseAllowed(allow.text, dictionary);

            var common = Resources.Load<TextAsset>(folder + CommonFile);

            if (common)
                WordListParser.ParseCommon(common.text, dictionary);

            if (!foundAny)
            {
                Debug.LogWarning($"[PurrFanity] No word lists found for language '{language}' (looked in Resources/{folder}).", this);
            }
        }

        private void OnValidate()
        {
            Invalidate();
        }
    }
}