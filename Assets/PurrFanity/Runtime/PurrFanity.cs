using System;
using System.Collections.Generic;
using UnityEngine;

namespace PurrFanity
{
    /// <summary>
    /// A [PurrFiltered] call that was skipped because its text was blocked.
    /// </summary>
    public readonly struct ProfanityBlockedCall
    {
        /// <summary>
        /// The object the method was called on, null for static methods.
        /// </summary>
        public readonly object target;

        /// <summary>
        /// "Type.Method" of the skipped method.
        /// </summary>
        public readonly string method;
        public readonly ProfanityResult result;
        
        public ProfanityBlockedCall(object target, string method, ProfanityResult result)
        {
            this.target = target;
            this.method = method;
            this.result = result;
        }
    }

    /// <summary>
    /// The profanity filter. Works out of the box with the built-in English lists.
    /// </summary>
    public static class PurrFanity
    {
        public const string ResourcesFolder = "PurrFanity";

        public const string DefaultProfileName = "Default";

        private static readonly Dictionary<string, ProfanityProfile> _profiles = new Dictionary<string, ProfanityProfile>();

        private static ProfanityProfile _defaultProfile;

        /// <summary>
        /// Raised when a [PurrFiltered] method is skipped because its text was blocked.
        /// </summary>
        public static event Action<ProfanityBlockedCall> onCallBlocked;

        /// <summary>
        /// Used whenever a call doesn't pass a profile.
        /// </summary>
        public static ProfanityProfile defaultProfile
        {
            get
            {
                if (_defaultProfile)
                    return _defaultProfile;

                _defaultProfile = Resources.Load<ProfanityProfile>($"{ResourcesFolder}/{DefaultProfileName}");

                if (!_defaultProfile)
                {
                    // A profile that isn't named Default is still better than none lol
                    var found = Resources.LoadAll<ProfanityProfile>(ResourcesFolder);

                    if (found.Length > 0)
                    {
                        _defaultProfile = found[0];
                        Debug.LogWarning($"[PurrFanity] No profile named '{DefaultProfileName}' in Resources/{ResourcesFolder}, " +  $"using '{found[0].name}'. Rename it to {DefaultProfileName} to make this the default.");
                    }
                }

                if (!_defaultProfile)
                    _defaultProfile = ProfanityProfile.CreateDefault();

                return _defaultProfile;
            }
            set => _defaultProfile = value;
        }

        /// <summary>
        /// Makes a profile available by name, for [PurrFiltered(profile: "name")].
        /// </summary>
        public static void RegisterProfile(string name, ProfanityProfile profile)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("A profile needs a name to be registered.", nameof(name));

            _profiles[name] = profile;
        }

        public static bool UnregisterProfile(string name)
        {
            return !string.IsNullOrEmpty(name) && _profiles.Remove(name);
        }

        /// <summary>
        /// Finds a profile by name, or falls back to the default with a warning.
        /// </summary>
        public static ProfanityProfile GetProfile(string name)
        {
            if (string.IsNullOrEmpty(name))
                return defaultProfile;

            if (_profiles.TryGetValue(name, out var profile) && profile)
                return profile;

            profile = Resources.Load<ProfanityProfile>($"{ResourcesFolder}/{name}");

            if (!profile)
            {
                Debug.LogWarning($"[PurrFanity] No profile named '{name}' is registered or found in " + $"Resources/{ResourcesFolder}. Using the default profile instead.");
                profile = defaultProfile;
            }

            _profiles[name] = profile;
            return profile;
        }

        /// <summary>
        /// Returns the filtered text, or an empty string when the message is blocked.
        /// </summary>
        public static string Filter(string text, ProfanityProfile profile = null)
        {
            return Resolve(profile).filter.Filter(text);
        }

        /// <summary>
        /// Returns false when the message is blocked.
        /// </summary>
        public static bool TryFilter(string text, out string filtered, ProfanityProfile profile = null)
        {
            return Resolve(profile).filter.TryFilter(text, out filtered);
        }

        /// <summary>
        /// True when the text has anything the profile filters.
        /// </summary>
        public static bool Contains(string text, ProfanityProfile profile = null)
        {
            return Resolve(profile).filter.Contains(text);
        }

        public static bool Contains(string text, out ProfanityCategory categories, ProfanityProfile profile = null)
        {
            var result = Resolve(profile).filter.Check(text);
            categories = result.categories;
            return result.hasProfanity;
        }

        /// <summary>
        /// The filtered text, whether it's blocked, and every match.
        /// </summary>
        public static ProfanityResult Check(string text, ProfanityProfile profile = null)
        {
            return Resolve(profile).filter.Check(text);
        }

        private static ProfanityProfile Resolve(ProfanityProfile profile)
        {
            return profile ? profile : defaultProfile;
        }

        internal static void RaiseCallBlocked(ProfanityBlockedCall call)
        {
            onCallBlocked?.Invoke(call);
        }

        // Statics survive entering play mode when domain reload is off
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _profiles.Clear();
            _defaultProfile = null;
            onCallBlocked = null;
        }
    }
}