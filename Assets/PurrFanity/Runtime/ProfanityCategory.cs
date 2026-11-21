using System;

namespace PurrNet.PurrFanity
{
    [Flags]
    public enum ProfanityCategory
    {
        None = 0,

        /// <summary>
        /// Light swearing, like damn and hell.
        /// </summary>
        Mild = 1 << 0,
        Strong = 1 << 1,
        Sexual = 1 << 2,
        Insults = 1 << 3,

        /// <summary>
        /// Racial, ethnic and religious slurs.
        /// </summary>
        Racism = 1 << 4,

        /// <summary>
        /// Homophobic and transphobic slurs.
        /// </summary>
        Phobic = 1 << 5,
        Ableism = 1 << 6,

        /// <summary>
        /// Default for your own word lists.
        /// </summary>
        Custom = 1 << 7,
        All = ~0
    }
}