using System.Collections.Generic;

namespace PurrFanity
{
    public sealed class ProfanityFilterSettings
    {
        public static readonly string[] defaultSillyWords =
        {
            "meow", "purr", "pebbles", "hiss", "nya", "floof", "whiskers", "paws", "maomao"
        };

        private readonly Dictionary<ProfanityCategory, ProfanityAction> _categoryActions = new Dictionary<ProfanityCategory, ProfanityAction>();

        /// <summary>
        /// Used for categories without an action of their own.
        /// </summary>
        public ProfanityAction action { get; set; } = ProfanityAction.Mask;

        /// <summary>
        /// Off lets all text through unchanged, for things like a streamer mode toggle.
        /// </summary>
        public bool enabled { get; set; } = true;
        public char maskCharacter { get; set; } = '*';
        public MaskStyle maskStyle { get; set; } = MaskStyle.All;

        /// <summary>
        /// The characters Symbols mask style cycles through.
        /// </summary>
        public string maskSymbols { get; set; } = defaultMaskSymbols;
        public const string defaultMaskSymbols = "@#$%&!";
        public string[] sillyWords { get; set; } = defaultSillyWords;

        /// <summary>
        /// Sees through tricks like sh1t, shiiit and s h i t.
        /// </summary>
        public bool detectEvasion { get; set; } = true;

        public void SetCategoryAction(ProfanityCategory category, ProfanityAction categoryAction)
        {
            _categoryActions[category] = categoryAction;
        }

        public void ClearCategoryActions()
        {
            _categoryActions.Clear();
        }

        public ProfanityAction GetAction(ProfanityCategory category)
        {
            return _categoryActions.TryGetValue(category, out var categoryAction) ? categoryAction : action;
        }
    }
}