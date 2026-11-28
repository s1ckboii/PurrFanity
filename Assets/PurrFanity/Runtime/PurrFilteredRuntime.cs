using System.ComponentModel;
using UnityEngine.Scripting;

namespace PurrFanity
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static class PurrFilteredRuntime
    {
        // False-skips the method body
        [Preserve]
        public static bool FilterArgument(ref string text, ProfanityActionOverride action, string profile, object target, string method)
        {
            if (string.IsNullOrEmpty(text))
                return true;

            var filter = PurrFanity.GetProfile(profile).Filter;
            ProfanityAction? forced = action == ProfanityActionOverride.Profile ? (ProfanityAction?)null : (ProfanityAction)action;

            if (filter.TryFilter(text, out var filtered, forced))
            {
                text = filtered;
                return true;
            }

            // Blocked is rare, so only then the full result gets built for the event
            PurrFanity.RaiseCallBlocked(new ProfanityBlockedCall(target, method, filter.Check(text, forced)));
            return false;
        }
    }
}