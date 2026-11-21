namespace PurrFanity
{
    public enum ProfanityAction
    {
        Mask,
        ReplaceSimilar,
        ReplaceSilly,
        Block
    }

    public enum MaskStyle
    {
        // ****
        All,
        // f*ck
        Vowels,
        // f***
        KeepFirst,
        // f**k
        KeepEnds,
        // @#$%
        Symbols
    }

    public enum ProfanityActionOverride
    {
        Profile = -1,
        Mask = ProfanityAction.Mask,
        ReplaceSimilar = ProfanityAction.ReplaceSimilar,
        ReplaceSilly = ProfanityAction.ReplaceSilly,
        Block = ProfanityAction.Block
    }
}