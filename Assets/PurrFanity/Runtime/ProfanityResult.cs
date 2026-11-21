using System;
using System.Collections.Generic;

namespace PurrNet.PurrFanity
{
    /// <summary>
    /// One filtered word or phrase in a checked text.
    /// </summary>
    public readonly struct ProfanityMatch
    {
        /// <summary>
        /// Where the match starts in the original text.
        /// </summary>
        public readonly int index;
        public readonly int length;

        /// <summary>
        /// The word list entry that matched, not what the player typed.
        /// </summary>
        public readonly string word;
        public readonly ProfanityCategory category;

        internal readonly string[] replacements;

        internal ProfanityMatch(int index, int length, ProfanityEntry entry)
        {
            this.index = index;
            this.length = length;
            word = entry.word;
            category = entry.category;
            replacements = entry.replacements;
        }
    }

    public readonly struct ProfanityResult
    {
        private static readonly ProfanityMatch[] _noMatches = Array.Empty<ProfanityMatch>();
        private readonly ProfanityMatch[] _matches;

        public readonly string original;
        public readonly string filtered;
        public readonly bool blocked;
        public readonly ProfanityCategory categories;
        public IReadOnlyList<ProfanityMatch> matches => _matches ?? _noMatches;
        public bool hasProfanity => _matches != null && _matches.Length > 0;

        internal ProfanityResult(string original, string filtered, bool blocked, ProfanityCategory categories, ProfanityMatch[] matches)
        {
            this.original = original;
            this.filtered = filtered;
            this.blocked = blocked;
            this.categories = categories;
            _matches = matches;
        }

        internal static ProfanityResult Clean(string text)
        {
            return new ProfanityResult(text, text, false, ProfanityCategory.None, null);
        }
    }
}