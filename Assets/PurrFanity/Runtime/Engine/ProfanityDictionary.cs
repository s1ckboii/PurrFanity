using System.Collections.Generic;
using System.Text;

namespace PurrFanity
{
    internal enum MatchKind
    {
        Exact,
        Prefix,
        Suffix,
        Contains
    }

    internal sealed class ProfanityEntry
    {
        public string word;
        public ProfanityCategory category;
        public string[] replacements;
        public MatchKind kind;

        public bool anchoredStart => kind == MatchKind.Exact || kind == MatchKind.Prefix;
        public bool anchoredEnd => kind == MatchKind.Exact || kind == MatchKind.Suffix;
    }

    // One character of checked text, with the letters it could stand for
    internal struct Glyph
    {
        public char letter;

        // Special characters or numbers can be used to evade filters, like "b4stard" or "sh1t"
        public string alternates;

        // Runs of two or more count as stretched letters
        public int run;
    }

    // Phrases are stored as letter runs, so "fuuuccck" still matches "fuck"
    internal sealed class ProfanityPhrase
    {
        public ProfanityEntry entry;
        // Hi Xu, remember my string[][] in lhud? ..Am sorry for the pain I caused you, I WILL do it over and over.
        public char[][] letters;
        public int[][] counts;
    }

    /// <summary>
    /// Compiled words a filter looks for.
    /// </summary>
    public sealed class ProfanityDictionary
    {
        private sealed class Node
        {
            public Dictionary<char, Node> children;
            public List<ProfanityEntry> entries;
            public char letter;
        }

        // Cap on steps/match, so no text can make it slow
        private const int MaxSteps = 1024;

        // Left out vowels (btch) only count on longer words with enough letters typed
        private const int SkipMinWord = 5;
        private const int SkipMinTyped = 4;
        private const string Vowels = "aeiou";

        private struct Search
        {
            public Glyph[] glyphs;
            public int count;
            public bool tolerant;
            public int budget;
        }

        private struct Step
        {
            public int position;
            public int depth;
            public int skipped;
            public bool justSkipped;
            public bool typedVowel;
        }

        private readonly Node _anchored = new Node();
        private readonly Node _floating = new Node();
        private readonly Dictionary<char, List<ProfanityPhrase>> _phrases = new Dictionary<char, List<ProfanityPhrase>>();
        private readonly HashSet<string> _allowed = new HashSet<string>();
        private readonly HashSet<string> _common = new HashSet<string>();
        private readonly Dictionary<char, List<string[]>> _allowedPhrases = new Dictionary<char, List<string[]>>();
        private int _allowedPhraseCount;
        private readonly List<char> _buildLetters = new List<char>();
        private readonly List<int> _buildCounts = new List<int>();
        private readonly List<int> _buildParts = new List<int>();
        private readonly StringBuilder _builder = new StringBuilder();

        public int count { get; private set; }
        public int allowedCount => _allowed.Count + _allowedPhraseCount;

        internal bool hasPhrases => _phrases.Count > 0;
        internal bool hasAllowedPhrases => _allowedPhraseCount > 0;
        internal bool hasCommon => _common.Count > 0;

        /// <summary>
        /// Adds a word, a word* / *word / *word* wildcard, or a phrase with spaces in it.
        /// </summary>
        public bool Add(string pattern, ProfanityCategory category, params string[] replacements)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                return false;

            pattern = pattern.Trim();

            bool leading = pattern[0] == '*';
            bool trailing = pattern.Length > 1 && pattern[pattern.Length - 1] == '*';
            var body = pattern.Trim('*').Trim();

            _buildLetters.Clear();
            _buildParts.Clear();

            bool inPart = false;
            for (var i = 0; i < body.Length; i++)
            {
                var letter = TextNormalizer.Fold(body[i]);

                if (!char.IsLetterOrDigit(letter))
                {
                    inPart = false;
                    continue;
                }

                if (!inPart)
                {
                    _buildParts.Add(_buildLetters.Count);
                    inPart = true;
                }

                _buildLetters.Add(letter);
            }

            if (_buildParts.Count == 0)
                return false;

            if (replacements != null && replacements.Length == 0)
                replacements = null;

            var entry = new ProfanityEntry
            {
                word = body.ToLowerInvariant(),
                category = category,
                replacements = replacements
            };

            if (_buildParts.Count == 1)
            {
                entry.kind = leading ? (trailing ? MatchKind.Contains : MatchKind.Suffix) : (trailing ? MatchKind.Prefix : MatchKind.Exact);
                Insert(entry.anchoredStart ? _anchored : _floating, entry);
            }
            else
            {
                entry.kind = MatchKind.Exact;

                var phrase = new ProfanityPhrase
                {
                    entry = entry,
                    letters = new char[_buildParts.Count][],
                    counts = new int[_buildParts.Count][]
                };

                for (var part = 0; part < _buildParts.Count; part++)
                {
                    int from = _buildParts[part];
                    int to = part + 1 < _buildParts.Count ? _buildParts[part + 1] : _buildLetters.Count;
                    ToRuns(from, to, out phrase.letters[part], out phrase.counts[part]);
                }

                var key = phrase.letters[0][0];
                if (!_phrases.TryGetValue(key, out var list))
                {
                    list = new List<ProfanityPhrase>();
                    _phrases.Add(key, list);
                }

                list.Add(phrase);
            }

            count++;
            return true;
        }

        /// <summary>
        /// Marks a word or phrase as always fine, like "Scunthorpe" or "moby dick".
        /// </summary>
        public void Allow(string word)
        {
            if (string.IsNullOrWhiteSpace(word))
                return;

            word = word.Trim();

            List<string> parts = null;
            int partStart = -1;

            for (var i = 0; i <= word.Length; i++)
            {
                bool inWord = i < word.Length && char.IsLetterOrDigit(TextNormalizer.Fold(word[i]));

                if (inWord)
                {
                    if (partStart < 0)
                        partStart = i;
                    continue;
                }

                if (partStart < 0)
                    continue;

                parts ??= new List<string>();
                parts.Add(FoldToString(word, partStart, i));
                partStart = -1;
            }

            if (parts == null)
                return;

            if (parts.Count == 1)
            {
                _allowed.Add(parts[0]);
                return;
            }

            var key = parts[0][0];
            if (!_allowedPhrases.TryGetValue(key, out var list))
            {
                list = new List<string[]>();
                _allowedPhrases.Add(key, list);
            }

            list.Add(parts.ToArray());
            _allowedPhraseCount++;
        }

        public void Clear()
        {
            _anchored.children = null;
            _anchored.entries = null;
            _floating.children = null;
            _floating.entries = null;
            _phrases.Clear();
            _allowed.Clear();
            _allowedPhrases.Clear();
            _allowedPhraseCount = 0;
            _common.Clear();
            count = 0;
        }

        private void Insert(Node node, ProfanityEntry entry)
        {
            for (var i = 0; i < _buildLetters.Count; i++)
            {
                var letter = _buildLetters[i];
                node.children ??= new Dictionary<char, Node>();

                if (!node.children.TryGetValue(letter, out var next))
                {
                    next = new Node { letter = letter };
                    node.children.Add(letter, next);
                }

                node = next;
            }

            node.entries ??= new List<ProfanityEntry>();
            node.entries.Add(entry);
        }

        private void ToRuns(int from, int to, out char[] letters, out int[] counts)
        {
            _buildCounts.Clear();
            _builder.Clear();

            for (var i = from; i < to; i++)
            {
                var letter = _buildLetters[i];

                if (_builder.Length > 0 && _builder[_builder.Length - 1] == letter)
                    _buildCounts[_buildCounts.Count - 1]++;
                else
                {
                    _builder.Append(letter);
                    _buildCounts.Add(1);
                }
            }

            letters = _builder.ToString().ToCharArray();
            counts = _buildCounts.ToArray();
        }

        /// <summary>
        /// Adds an everyday word. Two words are only read as one when neither is common: "fu ck" but not "pen is".
        /// </summary>
        public void AddCommon(string word)
        {
            word = word?.Trim();

            if (!string.IsNullOrEmpty(word))
                _common.Add(FoldToString(word, 0, word.Length));
        }

        internal bool IsCommon(string text, int start, int end)
        {
            return _common.Contains(FoldToString(text, start, end));
        }

        internal bool IsAllowed(string text, int start, int end)
        {
            if (_allowed.Count == 0)
                return false;

            return _allowed.Contains(FoldToString(text, start, end));
        }

        private string FoldToString(string text, int start, int end)
        {
            lock (_builder)
            {
                _builder.Clear();
                for (var i = start; i < end; i++)
                    _builder.Append(TextNormalizer.Fold(text[i]));
                return _builder.ToString();
            }
        }

        internal ProfanityEntry Match(Glyph[] glyphs, int count, bool tolerant, bool whole)
        {
            if (count == 0)
                return null;

            var search = new Search { glyphs = glyphs, count = count, tolerant = tolerant, budget = MaxSteps };
            var entry = Walk(_anchored, default, ref search);

            if (entry != null || whole || _floating.children == null)
                return entry;

            for (var start = 0; start < count; start++)
            {
                search.budget = MaxSteps;
                entry = Walk(_floating, new Step { position = start }, ref search);

                if (entry != null)
                    return entry;
            }

            return null;
        }

        private static ProfanityEntry Walk(Node node, Step step, ref Search search)
        {
            if (--search.budget < 0)
                return null;

            var entries = node.entries;

            if (entries != null && !step.justSkipped && AcceptsSkips(step))
            {
                for (var e = 0; e < entries.Count; e++)
                {
                    if (!entries[e].anchoredEnd || step.position == search.count)
                        return entries[e];
                }
            }

            var children = node.children;
            ProfanityEntry found;

            // Left out vowels, never the first letter or half of a double vowel (coolie isn't cole)
            if (children != null && search.tolerant && step.depth > 0)
            {
                for (var v = 0; v < Vowels.Length; v++)
                {
                    var vowel = Vowels[v];

                    if (vowel == node.letter || !children.TryGetValue(vowel, out var skipped))
                        continue;

                    if (skipped.children != null && skipped.children.ContainsKey(vowel))
                        continue;

                    var skip = step;
                    skip.depth++;
                    skip.skipped++;
                    skip.justSkipped = true;

                    found = Walk(skipped, skip, ref search);
                    if (found != null)
                        return found;
                }
            }

            if (step.position == search.count)
                return null;

            var glyph = search.glyphs[step.position];

            if (children != null)
            {
                if (children.TryGetValue(glyph.letter, out var next))
                {
                    found = Walk(next, Advance(step, glyph.letter, glyph, search.count), ref search);
                    if (found != null)
                        return found;
                }

                var alternates = glyph.alternates;

                if (alternates != null)
                {
                    for (var i = 0; i < alternates.Length; i++)
                    {
                        if (!children.TryGetValue(alternates[i], out next))
                            continue;

                        found = Walk(next, Advance(step, alternates[i], glyph, search.count), ref search);
                        if (found != null)
                            return found;
                    }
                }
            }

            if (search.tolerant && glyph.run >= 2 && glyph.letter == node.letter)
            {
                var stay = step;
                stay.position++;
                stay.justSkipped = false;
                return Walk(node, stay, ref search);
            }

            return null;
        }

        // A typed vowel means a real word (batch, morn), except a last one (ngga)...
        private static bool AcceptsSkips(Step step)
        {
            return step.skipped == 0 || (!step.typedVowel && step.depth >= SkipMinWord && step.depth - step.skipped >= SkipMinTyped);
        }

        private static Step Advance(Step step, char reading, Glyph glyph, int count)
        {
            // A digit standing in for a vowel is still evasion: b4strd
            if (step.position + 1 < count && char.IsLetter(glyph.letter) && Vowels.IndexOf(reading) >= 0)
                step.typedVowel = true;

            step.position++;
            step.depth++;
            step.justSkipped = false;
            return step;
        }

        // How many words from here are covered by an allowed phrase
        internal int MatchAllowedPhrase(string text, List<ProfanityFilter.Token> tokens, int tokenIndex)
        {
            var first = tokens[tokenIndex];

            if (!_allowedPhrases.TryGetValue(TextNormalizer.Fold(text[first.coreStart]), out var list))
                return 0;

            int longest = 0;

            for (var phraseIndex = 0; phraseIndex < list.Count; phraseIndex++)
            {
                var parts = list[phraseIndex];

                if (parts.Length <= longest || tokenIndex + parts.Length > tokens.Count)
                    continue;

                bool matched = true;

                for (var part = 0; part < parts.Length && matched; part++)
                {
                    var token = tokens[tokenIndex + part];
                    var word = parts[part];

                    if (token.coreEnd - token.coreStart != word.Length)
                    {
                        matched = false;
                        break;
                    }

                    for (var charIndex = 0; charIndex < word.Length; charIndex++)
                    {
                        if (TextNormalizer.Fold(text[token.coreStart + charIndex]) != word[charIndex])
                        {
                            matched = false;
                            break;
                        }
                    }
                }

                if (matched)
                    longest = parts.Length;
            }

            return longest;
        }

        internal ProfanityEntry MatchPhrase(List<char> letters, List<int> counts, List<ProfanityFilter.Token> tokens, int tokenIndex, bool tolerant, out int lastToken)
        {
            lastToken = tokenIndex;

            var first = tokens[tokenIndex];
            if (first.runCount == 0 || !_phrases.TryGetValue(letters[first.runStart], out var list))
                return null;

            for (var part = 0; part < list.Count; part++)
            {
                var phrase = list[part];
                int parts = phrase.letters.Length;

                if (tokenIndex + parts > tokens.Count)
                    continue;

                bool matched = true;

                for (var part2 = 0; part2 < parts && matched; part2++)
                {
                    var token = tokens[tokenIndex + part2];
                    var partLetters = phrase.letters[part2];
                    var partCounts = phrase.counts[part2];

                    if (token.runCount != partLetters.Length)
                    {
                        matched = false;
                        break;
                    }

                    for (var k = 0; k < partLetters.Length; k++)
                    {
                        int actual = counts[token.runStart + k];

                        if (letters[token.runStart + k] != partLetters[k] ||
                            (tolerant ? actual < partCounts[k] : actual != partCounts[k]))
                        {
                            matched = false;
                            break;
                        }
                    }
                }

                if (!matched)
                    continue;

                lastToken = tokenIndex + parts - 1;
                return phrase.entry;
            }

            return null;
        }
    }
}