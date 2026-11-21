using System.Globalization;

namespace PurrNet.PurrFanity
{
    // Word lists and messages go through the same folding, so they always meet in the same form
    internal static class TextNormalizer
    {
        // U+00C0..U+00FF, '.' means no base letter
        private const string Latin1 =
            "aaaaaaaceeeeiiiidnooooo.ouuuuy.s" +
            "aaaaaaaceeeeiiiidnooooo.ouuuuy.y";

        // U+0100..U+017F
        private const string LatinExtendedA =
            "aaaaaaccccccccddddeeeeeeeeeegggggggghhhhiiiiiiiiiiiijjkkkllllllllll" +
            "nnnnnnnnnoooooooorrrrrrssssssssttttttuuuuuuuuuuuuwwyyyzzzzzzs";

        public static bool IsLeetSymbol(char character)
        {
            return character switch
            {
                '@' or '$' or '!' or '|' or '+' or '€' or '¢' or '£' => true,
                _ => false,
            };

        }

        // Zero width characters and combining marks, skipped inside a word
        public static bool IsInvisible(char c)
        {
            if (c < '\u0300')
                return false;

            var category = char.GetUnicodeCategory(c);
            return category == UnicodeCategory.NonSpacingMark || category == UnicodeCategory.EnclosingMark || category == UnicodeCategory.Format;
        }

        public static bool IsWordChar(char c, bool detectEvasion)
        {
            return char.IsLetterOrDigit(c) || (detectEvasion && IsLeetSymbol(c)) || IsInvisible(c);
        }

        // Lowercase, no accents, look-alikes from other scripts mapped to latin
        public static char Fold(char character)
        {
            if (character < 128)
            {
                if (character >= 'A' && character <= 'Z')
                    return (char)(character + 32);
                return character;
            }

            if (character >= 'À' && character <= 'ÿ')
            {
                var mapped = Latin1[character - 'À'];
                return mapped == '.' ? char.ToLowerInvariant(character) : mapped;
            }

            if (character >= 'Ā' && character <= 'ſ')
            {
                var mapped = LatinExtendedA[character - 'Ā'];
                return mapped == '.' ? char.ToLowerInvariant(character) : mapped;
            }

            if (character >= 'Ａ' && character <= 'Ｚ')
                return (char)(character - 'Ａ' + 'a');
            if (character >= 'ａ' && character <= 'ｚ')
                return (char)(character - 'ａ' + 'a');
            if (character >= '０' && character <= '９')
                return (char)(character - '０' + '0');

            return Unconfuse(char.ToLowerInvariant(character));
        }

        private static char Unconfuse(char character)
        {
            return character switch
            {
                // Cyrillic
                'а' => 'a',
                'е' => 'e',
                'о' => 'o',
                'р' => 'p',
                'с' => 'c',
                'у' => 'y',
                'х' => 'x',
                'і' => 'i',
                'ј' => 'j',
                'ѕ' => 's',
                'һ' => 'h',
                // Greek
                'α' => 'a',
                'ε' => 'e',
                'ι' => 'i',
                'κ' => 'k',
                'ο' => 'o',
                'ρ' => 'p',
                'τ' => 't',
                'υ' => 'u',
                'ν' => 'v',
                _ => character,
            };

        }

        // One reading per character, for phrases
        public static char Leet(char character)
        {
            if (char.IsLetter(character))
                return character;

            var readings = Alternates(character);
            return readings != null ? readings[0] : character;
        }

        // What else a character could be, null when nothing
        public static string Alternates(string text, int index)
        {
            var character = text[index];

            // In caps an I is just an I (MIST, BLIND)
            if (character == 'I')
                return NextToLowercase(text, index) ? "l" : null;

            return Alternates(character);
        }

        private static string Alternates(char character)
        {
            return character switch
            {
                '0' => "o",
                '1' => "il",
                '2' => "z",
                '3' => "e",
                '4' => "a",
                '5' => "s",
                '6' => "gb",
                '7' => "t",
                '8' => "b",
                '9' => "g",
                '@' => "a",
                '$' => "s",
                '!' => "il",
                '|' => "il",
                '+' => "t",
                '€' => "e",
                '¢' => "c",
                '£' => "l",
                'l' => "i",
                'v' or 'V' => "u",
                'q' or 'Q' => "g",
                'z' or 'Z' => "s",
                'k' or 'K' => "c",
                _ => null,
            };

        }

        private static bool NextToLowercase(string text, int index)
        {
            for (var i = index - 1; i <= index + 1; i += 2)
            {
                if (i >= 0 && i < text.Length && char.IsLower(text[i]))
                    return true;
            }

            return false;
        }
    }
}