using System;
using UnityEngine.Scripting;

namespace PurrNet.PurrFanity
{
    /// <summary>
    /// Filters a string parameter before the method runs. On a method, it filters all of them.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Parameter, Inherited = false)]
    public sealed class PurrFilteredAttribute : PreserveAttribute
    {
        public PurrFilteredAttribute(ProfanityActionOverride action = ProfanityActionOverride.Profile, string profile = null) { }
    }
}