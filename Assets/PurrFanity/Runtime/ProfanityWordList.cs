using UnityEngine;

namespace PurrNet.PurrFanity
{
    /// <summary>
    /// Your own words, on top of the built-in lists.
    /// </summary>
    [CreateAssetMenu(menuName = "PurrNet/PurrFanity/Word List", fileName = "PurrFanity Word List", order = -199)]
    public class ProfanityWordList : ScriptableObject
    {
        [Tooltip("Category these words count as.")]
        [SerializeField] private ProfanityCategory _category = ProfanityCategory.Custom;

        [Tooltip("One entry per line, same format as the built-in lists:\n" + "word\nword => replacement, replacement\nword*   *word   *word*\ntwo words")]
        [TextArea(6, 30)]
        [SerializeField] private string _words;

        [Tooltip("Text files in the same format, for long lists.")]
        [SerializeField] private TextAsset[] _files;

        // Goes up whenever any list changes, so profiles know to rebuild
        internal static int changeCount;

        public ProfanityCategory category => _category;

        public string words => _words;

        public int AddTo(ProfanityDictionary dictionary)
        {
            int added = WordListParser.Parse(_words, _category, dictionary);

            if (_files == null)
                return added;

            for (var i = 0; i < _files.Length; i++)
            {
                if (_files[i])
                    added += WordListParser.Parse(_files[i].text, _category, dictionary);
            }

            return added;
        }

        /// <summary>
        /// Fills the list from code, for example with words downloaded at runtime.
        /// </summary>
        public void SetWords(string text, ProfanityCategory listCategory = ProfanityCategory.Custom)
        {
            _words = text;
            _category = listCategory;
            changeCount++;
        }

        private void OnValidate()
        {
            changeCount++;
        }
    }
}