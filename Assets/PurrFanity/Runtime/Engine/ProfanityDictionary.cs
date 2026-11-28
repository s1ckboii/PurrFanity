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

        // Bit of the language the word came from, 0 for lists without one
        public uint language;
        public string languageCode;

        // Spelled with letters that only some languages use (ász)
        public bool special;

        public bool anchoredStart => kind == MatchKind.Exact || kind == MatchKind.Prefix;
        public bool anchoredEnd => kind == MatchKind.Exact || kind == MatchKind.Suffix;
    }

    // An allowed *piece* of a word, for roots that show up inside ordinary words (szarvas)
    internal struct AllowedPiece
    {
        public string letters;
        public bool start;
        public bool end;
        public uint languages;
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

    // Folded words that can be looked up straight from the text, so a lookup doesn't allocate
    internal sealed class FoldedWords
    {
        private struct Word
        {
            public string text;
            public uint languages;
        }

        private readonly Dictionary<uint, List<Word>> _byHash = new Dictionary<uint, List<Word>>();

        public int Count { get; private set; }

        public void Add(string folded, uint languages)
        {
            uint hash = Hash(folded, 0, folded.Length, false);

            if (!_byHash.TryGetValue(hash, out var words))
            {
                words = new List<Word>(1);
                _byHash.Add(hash, words);
            }

            for (var i = 0; i < words.Count; i++)
            {
                if (words[i].text != folded)
                    continue;

                var word = words[i];
                word.languages |= languages;
                words[i] = word;
                return;
            }

            words.Add(new Word { text = folded, languages = languages });
            Count++;
        }

        // The languages the word is listed for, 0 when it isn't
        public uint Find(string text, int start, int end)
        {
            if (Count == 0 || !_byHash.TryGetValue(Hash(text, start, end, true), out var words))
                return 0;

            for (var i = 0; i < words.Count; i++)
            {
                if (SameWord(words[i].text, text, start, end))
                    return words[i].languages;
            }

            return 0;
        }

        public void Clear()
        {
            _byHash.Clear();
            Count = 0;
        }

        private static uint Hash(string text, int start, int end, bool fold)
        {
            uint hash = 2166136261; // FNV-1a again

            for (var i = start; i < end; i++)
                hash = (hash ^ (fold ? TextNormalizer.Fold(text[i]) : text[i])) * 16777619;

            return hash;
        }

        private static bool SameWord(string word, string text, int start, int end)
        {
            if (word.Length != end - start)
                return false;

            for (var i = 0; i < word.Length; i++)
            {
                if (word[i] != TextNormalizer.Fold(text[start + i]))
                    return false;
            }

            return true;
        }
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
        private readonly FoldedWords _allowed = new FoldedWords();
        private readonly FoldedWords _common = new FoldedWords();
        private readonly Dictionary<char, List<string[]>> _allowedPhrases = new Dictionary<char, List<string[]>>();
        private int _allowedPhraseCount;
        private readonly List<AllowedPiece> _allowedPieces = new List<AllowedPiece>();
        private readonly List<string> _languages = new List<string>();

        // Letters past ASCII and the languages whose words use them (ő, ű: hu)
        private readonly Dictionary<char, uint> _letters = new Dictionary<char, uint>();
        private readonly List<char> _buildLetters = new List<char>();
        private readonly List<int> _buildCounts = new List<int>();
        private readonly List<int> _buildParts = new List<int>();
        private readonly StringBuilder _builder = new StringBuilder();

        // Words allowed or added without a language count for all of them
        internal const uint AnyLanguage = uint.MaxValue;

        // One bit each, the last one would read as AnyLanguage
        private const int MaxLanguages = 31;

        public int Count { get; private set; }
        public int AllowedCount => _allowed.Count + _allowedPhraseCount;

        /// <summary>
        /// Codes of the languages words were added for, like en and hu.
        /// </summary>
        public IReadOnlyList<string> Languages => _languages;

        internal bool HasPhrases => _phrases.Count > 0;
        internal bool HasAllowedPhrases => _allowedPhraseCount > 0;
        internal bool HasCommon => _common.Count > 0;

        // Telling languages apart only matters with two or more
        internal bool HasLanguages => _languages.Count > 1;

        /// <summary>
        /// Adds a word, a word* / *word / *word* wildcard, or a phrase with spaces in it.
        /// </summary>
        public bool Add(string pattern, ProfanityCategory category, params string[] replacements)
        {
            return Add(pattern, category, null, replacements);
        }

        /// <summary>
        /// Adds a word, a word* / *word / *word* wildcard, or a phrase with spaces in it. Language specific.
        /// </summary>
        public bool Add(string pattern, ProfanityCategory category, string language, string[] replacements)
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

            uint bit = LanguageBit(language);
            NoteLetters(body, bit);

            var entry = new ProfanityEntry
            {
                word = body.ToLowerInvariant(),
                category = category,
                replacements = replacements,
                language = bit,
                languageCode = bit != 0 ? _languages[BitIndex(bit)] : null,
                special = HasSpecialLetter(body, 0, body.Length)
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

            Count++;
            return true;
        }

        /// <summary>
        /// Marks a word or phrase as always fine, like "Scunthorpe" or "moby dick".
        /// </summary>
        public void Allow(string word)
        {
            Allow(word, null);
        }

        /// <summary>
        /// Marks a word or phrase as always fine, like "Scunthorpe" or "moby dick". Language specific.
        /// </summary>
        public void Allow(string word, string language)
        {
            if (string.IsNullOrWhiteSpace(word))
                return;

            word = word.Trim();

            // No letters noted, allow lists hold other languages' words too (faggyú in en)
            uint bit = LanguageBit(language);

            if (word.IndexOf('*') >= 0)
            {
                AllowPiece(word, bit);
                return;
            }

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
                _allowed.Add(parts[0], bit != 0 ? bit : AnyLanguage);
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

        // piece*, *piece or *piece*: a word with it in it is fine
        private void AllowPiece(string pattern, uint bit)
        {
            var body = pattern.Trim('*').Trim();

            if (body.Length == 0)
                return;

            for (var i = 0; i < body.Length; i++)
            {
                if (!char.IsLetterOrDigit(TextNormalizer.Fold(body[i])))
                    return;
            }

            _allowedPieces.Add(new AllowedPiece
            {
                letters = FoldToString(body, 0, body.Length),
                start = pattern[0] != '*',
                end = pattern[pattern.Length - 1] != '*',
                languages = bit != 0 ? bit : AnyLanguage
            });
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
            _allowedPieces.Clear();
            _common.Clear();
            _languages.Clear();
            _letters.Clear();
            Count = 0;
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
            AddCommon(word, null);
        }

        /// <summary>
        /// Adds an everyday word. Two words are only read as one when neither is common: "fu ck" but not "pen is". Language specific.
        /// </summary>
        public void AddCommon(string word, string language)
        {
            word = word?.Trim();

            if (string.IsNullOrEmpty(word))
                return;

            uint bit = LanguageBit(language);
            NoteLetters(word, bit);
            _common.Add(FoldToString(word, 0, word.Length), bit != 0 ? bit : AnyLanguage);
        }

        internal bool IsCommon(string text, int start, int end)
        {
            return _common.Find(text, start, end) != 0;
        }

        // Languages the word is allowed in, 0 when it isn't
        internal uint AllowedLanguages(string text, int start, int end)
        {
            uint languages = _allowed.Find(text, start, end);

            for (var i = 0; i < _allowedPieces.Count; i++)
            {
                if (HasPiece(text, start, end, _allowedPieces[i]))
                    languages |= _allowedPieces[i].languages;
            }

            return languages;
        }

        private static bool HasPiece(string text, int start, int end, AllowedPiece piece)
        {
            int length = piece.letters.Length;

            if (length > end - start || (piece.start && piece.end && length != end - start))
                return false;

            int last = piece.start ? start : end - length;

            for (var at = piece.end ? end - length : start; at <= last; at++)
            {
                if (FoldedAt(text, at, piece.letters))
                    return true;
            }

            return false;
        }

        private static bool FoldedAt(string text, int at, string letters)
        {
            for (var i = 0; i < letters.Length; i++)
            {
                if (TextNormalizer.Fold(text[at + i]) != TextNormalizer.Fold(letters[i]))
                    return false;
            }

            return true;
        }

        // False when its the listed word with other accents: szár isn't szar, but szar and sz4r are
        internal bool SameAccents(string text, int start, int end, ProfanityEntry entry)
        {
            if (entry.language == 0)
                return true;

            var word = entry.word;
            int length = word.Length;

            if (length > end - start || (entry.kind == MatchKind.Exact && length != end - start))
                return true;

            bool found = false;
            int last = entry.anchoredStart ? start : end - length;

            for (var at = entry.anchoredEnd ? end - length : start; at <= last; at++)
            {
                if (!FoldedAt(text, at, word))
                    continue;

                found = true;

                if (AccentsAgree(text, at, word, entry.language))
                    return true;
            }

            return !found;
        }

        // Unaccented letters could be anything, an accented one the language uses has to be the listed one
        private bool AccentsAgree(string text, int at, string word, uint language)
        {
            for (var i = 0; i < word.Length; i++)
            {
                var character = text[at + i];

                if (character < 128 || !char.IsLetter(character))
                    continue;

                character = char.ToLowerInvariant(character);

                if (character != word[i] && _letters.TryGetValue(character, out var users) && (users & language) != 0)
                    return false;
            }

            return true;
        }

        // Languages where the word is ordinary
        internal uint WordLanguages(string text, int start, int end)
        {
            return _common.Find(text, start, end) | _allowed.Find(text, start, end);
        }

        // Languages that use every special letter in it, 0 when it has none of them
        internal uint LetterLanguages(string text, int start, int end)
        {
            uint languages = AnyLanguage;
            bool any = false;

            for (var i = start; i < end; i++)
            {
                var character = text[i];

                if (character < 128 || !char.IsLetter(character) || !_letters.TryGetValue(char.ToLowerInvariant(character), out var users))
                    continue;

                languages &= users;
                any = true;
            }

            return any ? languages : 0;
        }

        // 0 when no words were added for it, en-US reads as en
        internal uint FindLanguage(string code)
        {
            if (string.IsNullOrEmpty(code))
                return 0;

            for (var i = 0; i < _languages.Count; i++)
            {
                if (SameCode(_languages[i], code))
                    return 1u << i;
            }

            return 0;
        }

        private uint LanguageBit(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return 0;

            uint bit = FindLanguage(code);

            if (bit != 0 || _languages.Count >= MaxLanguages)
                return bit;

            // Stored without the region, en-US is en
            code = code.Trim();
            int cut = code.IndexOfAny(_regionSeparators);
            _languages.Add((cut > 0 ? code.Substring(0, cut) : code).ToLowerInvariant());
            return 1u << (_languages.Count - 1);
        }

        private static readonly char[] _regionSeparators = { '-', '_', ' ' };

        private static int BitIndex(uint bit)
        {
            int index = 0;

            while (bit > 1)
            {
                bit >>= 1;
                index++;
            }

            return index;
        }

        private static bool SameCode(string code, string other)
        {
            int start = 0;
            int end = other.Length;

            while (start < end && char.IsWhiteSpace(other[start]))
                start++;

            for (var i = start; i < end; i++)
            {
                if (other[i] == '-' || other[i] == '_' || char.IsWhiteSpace(other[i]))
                {
                    end = i;
                    break;
                }
            }

            if (end - start != code.Length)
                return false;

            for (var i = 0; i < code.Length; i++)
            {
                if (char.ToLowerInvariant(other[start + i]) != code[i])
                    return false;
            }

            return true;
        }

        private void NoteLetters(string word, uint bit)
        {
            if (bit == 0)
                return;

            for (var i = 0; i < word.Length; i++)
            {
                var character = word[i];

                if (character < 128 || !char.IsLetter(character))
                    continue;

                character = char.ToLowerInvariant(character);
                _letters.TryGetValue(character, out var users);
                _letters[character] = users | bit;
            }
        }

        internal static bool HasSpecialLetter(string text, int start, int end)
        {
            for (var i = start; i < end; i++)
            {
                if (text[i] >= 128 && char.IsLetter(text[i]))
                    return true;
            }

            return false;
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