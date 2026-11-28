using UnityEngine;

namespace PurrFanity
{
    // Picks a language from the word lists in the project instead of typing its code
    public sealed class LanguageAttribute : PropertyAttribute
    {
        // Label of the empty choice, null when there's none
        public string None { get; }

        public LanguageAttribute(string none = null)
        {
            None = none;
        }
    }
}
