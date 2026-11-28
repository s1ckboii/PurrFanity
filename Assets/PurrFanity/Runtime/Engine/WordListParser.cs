using System;

namespace PurrFanity
{
    /// <summary>
    /// Reads the word list text format, one entry per line.
    /// </summary>
    public static class WordListParser
    {
        private static readonly char[] _newLines = { '\n', '\r' };
        private static readonly char[] _commas = { ',' };

        /// <summary>
        /// Returns how many entries were added.
        /// </summary>
        public static int Parse(string text, ProfanityCategory category, ProfanityDictionary dictionary)
        {
            return Parse(text, category, dictionary, null);
        }

        /// <summary>
        /// Returns how many entries were added based on the specified language.
        /// </summary>
        public static int Parse(string text, ProfanityCategory category, ProfanityDictionary dictionary, string language)
        {
            if (dictionary == null)
                throw new ArgumentNullException(nameof(dictionary));

            if (string.IsNullOrEmpty(text))
                return 0;

            int added = 0;
            var lines = text.Split(_newLines, StringSplitOptions.RemoveEmptyEntries);

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();

                if (line.Length == 0 || line[0] == '#')
                    continue;

                string[] replacements = null;
                int arrow = line.IndexOf("=>", StringComparison.Ordinal);

                if (arrow >= 0)
                {
                    replacements = line.Substring(arrow + 2).Split(_commas, StringSplitOptions.RemoveEmptyEntries);

                    int kept = 0;
                    for (var r = 0; r < replacements.Length; r++)
                    {
                        var replacement = replacements[r].Trim();
                        if (replacement.Length > 0)
                            replacements[kept++] = replacement;
                    }

                    if (kept != replacements.Length)
                        Array.Resize(ref replacements, kept);

                    line = line.Substring(0, arrow);
                }

                if (dictionary.Add(line, category, language, replacements))
                    added++;
            }

            return added;
        }

        public static int ParseCommon(string text, ProfanityDictionary dictionary, string language = null)
        {
            if (dictionary == null)
                throw new ArgumentNullException(nameof(dictionary));

            if (string.IsNullOrEmpty(text))
                return 0;

            int added = 0;
            var lines = text.Split(_newLines, StringSplitOptions.RemoveEmptyEntries);

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();

                if (line.Length == 0 || line[0] == '#')
                    continue;

                dictionary.AddCommon(line, language);
                added++;
            }

            return added;
        }

        public static int ParseAllowed(string text, ProfanityDictionary dictionary, string language = null)
        {
            if (dictionary == null)
                throw new ArgumentNullException(nameof(dictionary));

            if (string.IsNullOrEmpty(text))
                return 0;

            int added = 0;
            var lines = text.Split(_newLines, StringSplitOptions.RemoveEmptyEntries);

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();

                if (line.Length == 0 || line[0] == '#')
                    continue;

                dictionary.Allow(line, language);
                added++;
            }

            return added;
        }
    }
}