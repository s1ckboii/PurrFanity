using System;
using System.Collections.Generic;
using System.Text;

namespace PurrFanity
{
    /// <summary>
    /// Finds and replaces listed words in a text. Safe to call from any thread.
    /// </summary>
    public sealed class ProfanityFilter
    {
        internal struct Token
        {
            // The whole word, with symbols on its ends ($hit, a$$)
            public int start;
            public int end;

            // Without those symbols (shit!!! -> shit)
            public int coreStart;
            public int coreEnd;

            // Letter runs, for phrases
            public int runStart;
            public int runCount;

            public bool hasLetter;
            
            // Only punctuation before it: s.h.i.t
            public bool glued;
            public bool single;
        }

        private sealed class Scratch
        {
            public Glyph[] glyphs = new Glyph[128];
            public int glyphCount;
            public readonly List<char> letters = new List<char>(128);
            public readonly List<int> counts = new List<int>(128);
            public readonly List<Token> tokens = new List<Token>(32);
            public readonly List<bool> allowed = new List<bool>(32);
            public readonly List<ProfanityMatch> matches = new List<ProfanityMatch>(8);
            public readonly StringBuilder builder = new StringBuilder(128);
        }

        [ThreadStatic] private static Scratch _scratch;

        private readonly ProfanityDictionary _dictionary;
        private readonly ProfanityFilterSettings _settings;

        public ProfanityDictionary dictionary => _dictionary;
        public ProfanityFilterSettings settings => _settings;

        public ProfanityFilter(ProfanityDictionary dictionary, ProfanityFilterSettings settings = null)
        {
            _dictionary = dictionary ?? throw new ArgumentNullException(nameof(dictionary));
            _settings = settings ?? new ProfanityFilterSettings();
        }

        public bool Contains(string text)
        {
            if (!_settings.enabled || string.IsNullOrEmpty(text))
                return false;

            var scratch = _scratch ??= new Scratch();
            FindMatches(text, scratch, true);
            return scratch.matches.Count > 0;
        }

        /// <summary>
        /// Returns the filtered text, or an empty string when it's blocked.
        /// </summary>
        public string Filter(string text)
        {
            TryFilter(text, out var filtered);
            return filtered;
        }

        /// <summary>
        /// Like Check, but skips the list of matches. Returns false when the text is blocked.
        /// </summary>
        public bool TryFilter(string text, out string filtered, ProfanityAction? forcedAction = null)
        {
            filtered = text;

            if (!_settings.enabled || string.IsNullOrEmpty(text))
                return true;

            var scratch = _scratch ??= new Scratch();
            FindMatches(text, scratch, false);

            if (scratch.matches.Count == 0)
                return true;

            if (IsBlocked(scratch.matches, forcedAction, out _))
            {
                filtered = string.Empty;
                return false;
            }

            filtered = Replace(text, scratch, forcedAction);
            return true;
        }

        public ProfanityResult Check(string text)
        {
            return Check(text, null);
        }

        /// <summary>
        /// Like Check(string text), but uses forcedAction for every match.
        /// </summary>
        public ProfanityResult Check(string text, ProfanityAction? forcedAction)
        {
            if (!_settings.enabled || string.IsNullOrEmpty(text))
                return ProfanityResult.Clean(text);

            var scratch = _scratch ??= new Scratch();
            FindMatches(text, scratch, false);

            if (scratch.matches.Count == 0)
                return ProfanityResult.Clean(text);

            var matches = scratch.matches.ToArray();

            if (IsBlocked(scratch.matches, forcedAction, out var categories))
                return new ProfanityResult(text, string.Empty, true, categories, matches);

            return new ProfanityResult(text, Replace(text, scratch, forcedAction), false, categories, matches);
        }

        private bool IsBlocked(List<ProfanityMatch> matches, ProfanityAction? forcedAction, out ProfanityCategory categories)
        {
            categories = ProfanityCategory.None;
            bool blocked = false;

            for (var i = 0; i < matches.Count; i++)
            {
                categories |= matches[i].category;

                if ((forcedAction ?? _settings.GetAction(matches[i].category)) == ProfanityAction.Block)
                    blocked = true;
            }

            return blocked;
        }

        private string Replace(string text, Scratch scratch, ProfanityAction? forcedAction)
        {
            var matches = scratch.matches;
            var builder = scratch.builder;
            builder.Clear();

            int position = 0;

            for (var i = 0; i < matches.Count; i++)
            {
                var match = matches[i];
                builder.Append(text, position, match.index - position);
                AppendReplacement(builder, text, match, forcedAction ?? _settings.GetAction(match.category));
                position = match.index + match.length;
            }

            builder.Append(text, position, text.Length - position);
            return builder.ToString();
        }

        private void AppendReplacement(StringBuilder builder, string text, ProfanityMatch match, ProfanityAction action)
        {
            string[] pool = null;

            if (action == ProfanityAction.ReplaceSimilar)
                pool = match.replacements;

            // No similar word listed, a silly one is better than **** - might change later (watch this stay like this for a decade)
            if (action == ProfanityAction.ReplaceSilly || (action == ProfanityAction.ReplaceSimilar && IsEmpty(pool)))
                pool = IsEmpty(_settings.sillyWords) ? ProfanityFilterSettings.defaultSillyWords : _settings.sillyWords;

            if (IsEmpty(pool))
            {
                AppendMask(builder, text, match, _settings.maskStyle);
                return;
            }

            // Same message, same pick
            uint hash = 2166136261; // magic number of doom (FNV-1a hash)
            int upper = 0;
            int lower = 0;
            bool startsUpper = false;
            bool seenLetter = false;

            for (var i = 0; i < match.length; i++)
            {
                var character = text[match.index + i];
                hash = (hash ^ char.ToLowerInvariant(character)) * 16777619; // magic number of doom part 2 (FNV-1a hash)
                // might move these out as private consts uint HashSeed and HashPrime later but im lazy but now that i think about it i wouldve already done it if im not just yapping what is wrong with me... okayBye
                if (!char.IsLetter(character))
                    continue;

                bool isUpper = char.IsUpper(character);

                if (!seenLetter)
                {
                    seenLetter = true;
                    startsUpper = isUpper;
                }

                if (isUpper)
                    upper++;
                else
                    lower++;
            }

            hash = (hash ^ (uint)match.index) * 16777619;

            var replacement = pool[(int)(hash % (uint)pool.Length)];

            if (string.IsNullOrEmpty(replacement))
                return;

            if (upper > 1 && lower == 0)
            {
                // Char by char, ToUpperInvariant on the string would allocate
                for (var i = 0; i < replacement.Length; i++)
                    builder.Append(char.ToUpperInvariant(replacement[i]));
            }
            else if (startsUpper)
            {
                builder.Append(char.ToUpperInvariant(replacement[0]));
                builder.Append(replacement, 1, replacement.Length - 1);
            }
            else
                builder.Append(replacement);
        }

        // Keeps a phrase's spaces whatever the style
        private void AppendMask(StringBuilder builder, string text, ProfanityMatch match, MaskStyle style)
        {
            int start = builder.Length;
            int firstLetter = -1;
            int lastLetter = -1;

            for (var i = 0; i < match.length; i++)
            {
                if (!char.IsLetterOrDigit(text[match.index + i]))
                    continue;

                if (firstLetter < 0)
                    firstLetter = i;

                lastLetter = i;
            }

            var symbols = string.IsNullOrEmpty(_settings.maskSymbols) ? ProfanityFilterSettings.defaultMaskSymbols : _settings.maskSymbols;
            int masked = 0;

            for (var i = 0; i < match.length; i++)
            {
                var character = text[match.index + i];

                if (char.IsWhiteSpace(character) || Keeps(style, character, i == firstLetter, i == lastLetter))
                {
                    builder.Append(character);
                    continue;
                }

                builder.Append(style == MaskStyle.Symbols ? symbols[(match.index + masked) % symbols.Length] : _settings.maskCharacter);
                masked++;
            }

            if (masked > 0 || style == MaskStyle.All)
                return;

            // Nothing got hidden (fvck has no vowels, a two letter word in KeepEnds), try the next stricter style
            builder.Length = start;
            AppendMask(builder, text, match, style == MaskStyle.Vowels ? MaskStyle.KeepEnds : style == MaskStyle.KeepEnds ? MaskStyle.KeepFirst : MaskStyle.All);
        }

        private static bool Keeps(MaskStyle style, char character, bool first, bool last)
        {
            return style switch
            {
                // Vowels and anything standing in for a letter go, separators like s.h.i.t stay
                MaskStyle.Vowels => (bool)(char.IsLetter(character) ? "aeiou".IndexOf(TextNormalizer.Fold(character)) < 0 : !char.IsDigit(character) && !TextNormalizer.IsLeetSymbol(character)),
                MaskStyle.KeepFirst => first,
                MaskStyle.KeepEnds => first || last,
                _ => false,
            };
        }

        private static bool IsEmpty(string[] pool)
        {
            return pool == null || pool.Length == 0;
        }

        private const int MaxSpelled = 16;

        private void FindMatches(string text, Scratch scratch, bool firstOnly)
        {
            var tokens = scratch.tokens;
            var matches = scratch.matches;

            scratch.letters.Clear();
            scratch.counts.Clear();
            tokens.Clear();
            matches.Clear();

            bool evasion = _settings.detectEvasion;
            Tokenize(text, scratch, evasion);

            var allowed = scratch.allowed;
            bool hasAllowedPhrases = _dictionary.hasAllowedPhrases;

            if (hasAllowedPhrases)
            {
                allowed.Clear();

                for (var t = 0; t < tokens.Count; t++)
                    allowed.Add(false);

                for (var t = 0; t < tokens.Count; t++)
                {
                    int covered = _dictionary.MatchAllowedPhrase(text, tokens, t);

                    for (var j = 0; j < covered; j++)
                        allowed[t + j] = true;
                }
            }

            bool hasPhrases = _dictionary.hasPhrases;
            int index = 0;

            while (index < tokens.Count)
            {
                // Spelled out: s h i t
                if (evasion && tokens[index].single)
                {
                    int spelled = MatchSpelled(text, scratch, index, hasAllowedPhrases);

                    if (spelled > 0)
                    {
                        if (firstOnly)
                            return;

                        index += spelled;
                        continue;
                    }
                }

                // Split up by spaces: fu ck, f u ck, f uck
                int pieces = evasion ? MatchPieces(text, scratch, index, hasAllowedPhrases) : 0;

                if (pieces > 0)
                {
                    if (firstOnly)
                        return;

                    index += pieces;
                    continue;
                }

                int groupEnd = index + 1;

                while (evasion && groupEnd < tokens.Count && tokens[groupEnd].glued)
                    groupEnd++;

                int before = matches.Count;
                int next = groupEnd;
                bool groupAllowed = false;

                for (var t = index; t < groupEnd; t++)
                {
                    var token = tokens[t];

                    if (hasAllowedPhrases && allowed[t])
                    {
                        groupAllowed = true;
                        continue;
                    }

                    if (hasPhrases)
                    {
                        var phrase = _dictionary.MatchPhrase(scratch.letters, scratch.counts, tokens, t, evasion,
                            out var lastToken);

                        if (phrase != null)
                        {
                            matches.Add(new ProfanityMatch(token.coreStart, tokens[lastToken].coreEnd - token.coreStart,
                                phrase));

                            if (firstOnly)
                                return;

                            t = lastToken;
                            next = Math.Max(next, lastToken + 1);
                            continue;
                        }
                    }

                    var entry = MatchToken(text, token, scratch, out var matchStart, out var matchEnd);

                    if (entry == null)
                        continue;

                    if (_dictionary.IsAllowed(text, token.coreStart, token.coreEnd))
                    {
                        groupAllowed = true;
                        continue;
                    }

                    matches.Add(new ProfanityMatch(matchStart, matchEnd - matchStart, entry));

                    if (firstOnly)
                        return;
                }

                // Recognizing 5_/TuP1___D as stupid
                if (matches.Count == before && groupEnd - index > 1 && !groupAllowed)
                {
                    var entry = MatchJoined(text, scratch, index, groupEnd);

                    if (entry != null)
                    {
                        int start = tokens[index].start;
                        matches.Add(new ProfanityMatch(start, tokens[groupEnd - 1].end - start, entry));

                        if (firstOnly)
                            return;
                    }
                }

                index = next;
            }
        }

        private static void Tokenize(string text, Scratch scratch, bool evasion)
        {
            var tokens = scratch.tokens;
            int length = text.Length;
            int previousEnd = -1;
            int i = 0;

            while (i < length)
            {
                if (!TextNormalizer.IsWordChar(text[i], evasion))
                {
                    i++;
                    continue;
                }

                var token = new Token { start = i };

                while (i < length && TextNormalizer.IsWordChar(text[i], evasion))
                {
                    if (char.IsLetter(text[i]))
                        token.hasLetter = true;

                    i++;
                }

                token.end = i;
                token.coreStart = token.start;
                token.coreEnd = token.end;

                if (evasion)
                {
                    while (token.coreStart < token.coreEnd && !char.IsLetterOrDigit(text[token.coreStart]))
                        token.coreStart++;

                    while (token.coreEnd > token.coreStart && !char.IsLetterOrDigit(text[token.coreEnd - 1]))
                        token.coreEnd--;

                    token.glued = previousEnd >= 0 && !HasWhiteSpace(text, previousEnd, token.start);
                    token.single = VisibleLength(text, token.coreStart, token.coreEnd) == 1;
                }

                previousEnd = token.end;

                token.runStart = scratch.letters.Count;
                AppendRuns(text, token.coreStart, token.coreEnd, evasion, scratch.letters, scratch.counts);
                token.runCount = scratch.letters.Count - token.runStart;
                tokens.Add(token);
            }
        }

        private ProfanityEntry MatchToken(string text, Token token, Scratch scratch, out int matchStart, out int matchEnd)
        {
            matchStart = token.coreStart;
            matchEnd = token.coreEnd;

            // Plain numbers aren't words, otherwise "455" reads as "ass" (sure it could be like that like boobies as 800815 but theres not much i can do about that)
            if (!token.hasLetter)
                return null;

            if (token.start != token.coreStart || token.end != token.coreEnd)
            {
                scratch.glyphCount = 0;
                AddGlyphs(text, token.start, token.end, scratch);

                var entry = Match(scratch, false);

                if (entry != null)
                {
                    matchStart = token.start;
                    matchEnd = token.end;
                    return entry;
                }
            }

            scratch.glyphCount = 0;
            AddGlyphs(text, token.coreStart, token.coreEnd, scratch);
            return Match(scratch, false);
        }

        private ProfanityEntry MatchJoined(string text, Scratch scratch, int first, int end)
        {
            var tokens = scratch.tokens;
            bool hasLetter = false;
            scratch.glyphCount = 0;

            for (var t = first; t < end; t++)
            {
                hasLetter |= tokens[t].hasLetter;
                AddGlyphs(text, tokens[t].start, tokens[t].end, scratch);
            }

            return hasLetter ? Match(scratch, true) : null;
        }

        private int MatchSpelled(string text, Scratch scratch, int first, bool hasAllowedPhrases)
        {
            var tokens = scratch.tokens;
            int last = first;

            while (last < tokens.Count && last - first < MaxSpelled && tokens[last].single &&
                   !(hasAllowedPhrases && scratch.allowed[last]))
                last++;

            // Longest first
            for (var count = last - first; count >= 3; count--)
            {
                bool hasLetter = false;
                scratch.glyphCount = 0;

                for (var t = first; t < first + count; t++)
                {
                    hasLetter |= tokens[t].hasLetter;
                    AddGlyphs(text, tokens[t].coreStart, tokens[t].coreEnd, scratch);
                }

                var entry = hasLetter ? Match(scratch, true) : null;

                if (entry == null)
                    continue;

                int start = tokens[first].coreStart;
                scratch.matches.Add(new ProfanityMatch(start, tokens[first + count - 1].coreEnd - start, entry));
                return count;
            }

            return 0;
        }

        // Longest number of words read as one
        private const int MaxPieces = 4;

        // Standalone words read as one word, longest first. Returns how many were used, 0 when none.
        private int MatchPieces(string text, Scratch scratch, int first, bool hasAllowedPhrases)
        {
            if (!_dictionary.hasCommon)
                return 0;

            var tokens = scratch.tokens;

            for (var count = MaxPieces; count >= 2; count--)
            {
                if (!CanJoin(text, scratch, first, count, hasAllowedPhrases))
                    continue;

                scratch.glyphCount = 0;

                for (var piece = first; piece < first + count; piece++)
                    AddGlyphs(text, tokens[piece].coreStart, tokens[piece].coreEnd, scratch);

                var entry = Match(scratch, true);

                if (entry == null || !PiecesAreFragments(text, scratch, first, count))
                    continue;

                int start = tokens[first].coreStart;
                scratch.matches.Add(new ProfanityMatch(start, tokens[first + count - 1].coreEnd - start, entry));
                return count;
            }

            return 0;
        }

        // Pieces need to stand alone, and more than two need a short one among them (f u ck),
        // so three real words never join
        private bool CanJoin(string text, Scratch scratch, int first, int count, bool hasAllowedPhrases)
        {
            var tokens = scratch.tokens;
            int end = first + count;

            if (end > tokens.Count || tokens[first].glued || (end < tokens.Count && tokens[end].glued))
                return false;

            bool hasLetter = false;
            bool hasShort = false;

            for (var piece = first; piece < end; piece++)
            {
                var token = tokens[piece];

                if ((piece > first && token.glued) || (hasAllowedPhrases && scratch.allowed[piece]))
                    return false;

                int length = VisibleLength(text, token.coreStart, token.coreEnd);

                if (length == 0)
                    return false;

                hasLetter |= token.hasLetter;
                hasShort |= length <= 2;
            }

            return hasLetter && (count == 2 || hasShort);
        }

        // No piece is filtered on its own, or a common word (pen is)
        private bool PiecesAreFragments(string text, Scratch scratch, int first, int count)
        {
            var tokens = scratch.tokens;

            for (var piece = first; piece < first + count; piece++)
            {
                var token = tokens[piece];

                if (MatchToken(text, token, scratch, out _, out _) != null)
                    return false;

                if (VisibleLength(text, token.coreStart, token.coreEnd) >= 2 &&
                    _dictionary.IsCommon(text, token.coreStart, token.coreEnd))
                    return false;
            }

            return true;
        }

        private ProfanityEntry Match(Scratch scratch, bool whole)
        {
            var glyphs = scratch.glyphs;
            int count = scratch.glyphCount;

            for (var i = 0; i < count;)
            {
                int end = i + 1;

                while (end < count && glyphs[end].letter == glyphs[i].letter)
                    end++;

                for (var k = i; k < end; k++)
                    glyphs[k].run = end - i;

                i = end;
            }

            return _dictionary.Match(glyphs, count, _settings.detectEvasion, whole);
        }

        private void AddGlyphs(string text, int start, int end, Scratch scratch)
        {
            bool evasion = _settings.detectEvasion;

            for (var i = start; i < end; i++)
            {
                var c = text[i];

                if (TextNormalizer.IsInvisible(c) || !TextNormalizer.IsWordChar(c, evasion))
                    continue;

                if (scratch.glyphCount == scratch.glyphs.Length)
                    Array.Resize(ref scratch.glyphs, scratch.glyphs.Length * 2);

                scratch.glyphs[scratch.glyphCount++] = new Glyph
                {
                    letter = TextNormalizer.Fold(c),
                    alternates = evasion ? TextNormalizer.Alternates(text, i) : null
                };
            }
        }

        private static bool HasWhiteSpace(string text, int start, int end)
        {
            for (var i = start; i < end; i++)
            {
                if (char.IsWhiteSpace(text[i]))
                    return true;
            }

            return false;
        }

        private static int VisibleLength(string text, int start, int end)
        {
            int visible = 0;

            for (var i = start; i < end; i++)
            {
                if (!TextNormalizer.IsInvisible(text[i]))
                    visible++;
            }

            return visible;
        }

        private static void AppendRuns(string text, int start, int end, bool leet, List<char> letters,
            List<int> counts)
        {
            int first = letters.Count;

            for (var i = start; i < end; i++)
            {
                if (TextNormalizer.IsInvisible(text[i]))
                    continue;

                var character = TextNormalizer.Fold(text[i]);

                if (leet)
                    character = TextNormalizer.Leet(character);

                int last = letters.Count - 1;

                if (last >= first && letters[last] == character)
                    counts[last]++;
                else
                {
                    letters.Add(character);
                    counts.Add(1);
                }
            }
        }
    }
}