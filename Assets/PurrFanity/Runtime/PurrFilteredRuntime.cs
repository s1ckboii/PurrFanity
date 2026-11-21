using System.ComponentModel;
using UnityEngine.Scripting;

namespace PurrNet.PurrFanity
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

            var filter = PurrFanity.GetProfile(profile).filter;
            var result = action == ProfanityActionOverride.Profile ? filter.Check(text) : filter.Check(text, (ProfanityAction)action);

            if (!result.hasProfanity)
                return true;

            if (result.blocked)
            {
                PurrFanity.RaiseCallBlocked(new ProfanityBlockedCall(target, method, result));
                return false;
            }

            text = result.filtered;
            return true;
        }
    }
}