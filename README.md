# PurrFanity

A free, easy to use profanity filter for Unity, made for [PurrNet](https://purrnet.dev/) and usable in any project.

```csharp
using PurrFanity;

string clean = PurrFanity.Filter("what the hell");   // "what the ****"
```

- **Pick what gets filtered.** Words are split into categories (mild swearing, strong swearing, sexual, insults, racism, phobic, ableism). A game for a younger audience ticks everything, another only ticks the slurs.
- **Pick what happens.** Mask with asterisks, swap for a similar clean word, swap for a silly word, or block the whole message. Each category can have its own action.
- **One attribute.** Put `[PurrFiltered]` on a string parameter and it's filtered before the method runs. On PurrNet RPCs that's on the receiving side.
- **Hard to dodge.** Sees through `sh1t`, `$hit`, `fvck`, `shìt`, `shiit`, `btch`, `s h i t`, `fu ck` and `5_h1__t`, without flagging `class`, `assassin` or `Scunthorpe`.
- **Your own words.** Add word list assets, or allow words the built-in lists filter.
- **Works anywhere.** Made for PurrNet, but the filter needs no networking library at all. Use it for chat, player names, lobby titles, in any Unity project.
- **No garbage on clean text.** Most messages are clean, and checking them allocates nothing.

English ships built in. Other languages are a folder of text files away, see [Adding a language](#adding-a-language).

## Installation

### Unity Package Manager (Git URL)

**Latest release:**
```
https://github.com/s1ckboii/PurrFanity.git?path=Assets/PurrFanity#release
```

**Latest development:**
```
https://github.com/s1ckboii/PurrFanity.git?path=Assets/PurrFanity#dev
```

Requires Unity `2022.3` or newer.

## Filtering text

```csharp
// filtered text, empty if the message is blocked
string clean = PurrFanity.Filter(message);

// false if the message is blocked
if (!PurrFanity.TryFilter(message, out string filtered))
    ShowWarning("Please keep it friendly.");

// just checking, for example a player name field
bool bad = PurrFanity.Contains(playerName);
bool bad2 = PurrFanity.Contains(playerName, out ProfanityCategory categories);

// everything: filtered text, blocked or not, and each match with its position and category
ProfanityResult result = PurrFanity.Check(message);
```

Every call takes an optional profile as its last argument. Without one, the default profile is used.

## Profiles

A profile decides what is filtered and how. The first time PurrFanity is in a project it creates `Assets/Resources/PurrFanity` with a `Default` profile and a `Custom Words` list. More can be made with **Assets > Create > PurrNet > PurrFanity > Profile**.

| Setting | What it does |
|---|---|
| Languages | Which built-in languages to load (`en`) |
| Categories | Which kinds of words are filtered |
| Action | What happens to a filtered word |
| Category Actions | A different action for some categories, for example mask swearing but block slurs |
| Mask Character | The character used by Mask |
| Silly Words | The pool Replace Silly picks from |
| Detect Evasion | See through digits, symbols and look-alike letters (`fvck`, `azz`), letters typed three or more times, left out vowels (`btch`), and letters split up by spaces or punctuation |
| Custom Lists | Your own word lists |
| Allowed Words | Words that are always fine, and phrases that are fine together |

The inspector has a **Try it** box at the bottom to test the profile on any text.

To make a profile the default, do one of these:

- Name it `Default` and put it in a `Resources/PurrFanity` folder. Any other profile in that folder is used when there is no `Default`.
- Assign it from code: `PurrFanity.defaultProfile = myProfile;`

With no profile at all, PurrFanity filters every English category with asterisks.

### Actions

| Action | `what the hell` becomes |
|---|---|
| `Mask` | `what the ****` |
| `ReplaceSimilar` | `what the heck` |
| `ReplaceSilly` | `what the meow` |
| `Block` | the message is rejected |

`ReplaceSimilar` uses the replacements written in the word list and falls back to a silly word for words that have none. If you'd rather mask those, give their categories their own action. Replacements keep the casing of what the player typed (`DAMN` becomes `DARN`).

## The [PurrFiltered] attribute

Put `[PurrFiltered]` on a string parameter and it's filtered before the method body runs:

```csharp
[ServerRpc]
private void SendChat([PurrFiltered] string message)
{
    // message is already filtered here
    ReceiveChat(message);
}
```

It works on any method, networked or not. On a method instead of a parameter it filters every string parameter:

```csharp
[PurrFiltered]
public void SetProfile(string name, string bio) { ... }
```

Options, on either:

```csharp
// force an action, whatever the profile says
[PurrFiltered(action: ProfanityActionOverride.Block)]

// use a profile by name: registered with PurrFanity.RegisterProfile,
// or an asset with that name in a Resources/PurrFanity folder
[PurrFiltered(profile: "Strict")]
```

A parameter's own `[PurrFiltered]` wins over the method's.

When the text is blocked the body does not run, and `PurrFanity.onCallBlocked` is raised so you can tell the player:

```csharp
PurrFanity.onCallBlocked += call =>
{
    Debug.Log($"{call.method} was blocked: {call.result.categories}");
};
```

A blocked method that returns a value returns `default`. A blocked `Task` or `Task<T>` returns an already completed task.

### RPCs

With PurrNet the filter runs where the RPC is received, so on the server for a `ServerRpc`, and a modified client can't skip it.

Other networking libraries move RPC bodies around in their own way, so there the filter may run on the sending side instead. Filter inside the RPC to be sure it runs where it's received:

```csharp
void CmdSendChat(string message)
{
    if (!PurrFanity.TryFilter(message, out message))
        return; // blocked
}
```

## Your own words

Create a list with **Assets > Create > PurrNet > PurrFanity > Word List**, type one entry per line, and add it to a profile's Custom Lists. Long lists can live in text files assigned to the same asset.

```
# comments start with a hash
noob
noob => newcomer, beginner
smurf*
*hax
*cheat*
git gud
```

| Entry | Matches |
|---|---|
| `word` | that whole word only |
| `word => a, b` | same, with replacements for Replace Similar |
| `word*` | words starting with it |
| `*word` | words ending with it |
| `*word*` | words containing it |
| `two words` | the words in sequence |

Write each word once, in plain lowercase. Case, accents, `1337` spelling and stretched letters are handled by the filter.

## Words that depend on context

Some listed words are fine in the right company. An allowed entry with several words only lets them through together, in that order:

```
moby dick
homo sapiens
tit for tat
```

`I read Moby Dick` is left alone, `you dick` is still filtered. The built-in English list ships with a set of these in `allow.txt`, and a profile's Allowed Words takes them too:

```csharp
profile.AllowWord("hell yeah");
```

Allowed phrases are matched as written (case, accents and punctuation between the words are ignored), so `moby d1ck` is still filtered.

## Adding a language

The built-in lists live in `Assets/PurrFanity/Runtime/Resources/PurrFanity/WordLists/<language>/`. To add a language, copy the `en` folder, rename it to the language code, replace the words and open a pull request against `dev`. The list format and files are described in [Documentation~/WordLists.md](Assets/PurrFanity/Documentation~/WordLists.md).

A profile loads a language once its code is added to the profile's Languages.

## Performance

- Clean text allocates nothing: buffers are reused and the original string is returned as is. That includes `[PurrFiltered]` calls.
- Text with a match allocates the filtered string and the list of matches, around 240 bytes for a short message with one bad word.
- A short chat message takes around 20 µs in the Editor, about 50,000 messages a second. Builds are faster.
- A profile loads its word lists once, the first time it's used, on the main thread. After that, filtering is safe from any thread.
- `[PurrFiltered]` is added at compile time, so there's no reflection while the game runs.

## Good to know

- Matching is on whole words by default, which is what keeps `class` and `assassin` clean. Use wildcards sparingly, and only for stems that do not appear inside ordinary words.
- A word split in two by a space (`fu ck`) is only caught when neither half is filtered on its own or is a common word, so `pen is` stays clean. Splits into three or more pieces (`f u ck`) are not caught.
- Real words with double letters that would read as a stretched bad word (`Shiite`, `assess`) are in `allow.txt`.
- Left out vowels only count for words of five letters or more, with at least four letters typed and no vowels: `btch` is caught, `cnt` is not (it's short for count).
- The built-in lists are a starting point, not a guarantee. Some entries are ordinary words in other contexts, use Allowed Words to let them through, alone or as part of a phrase.
- The filter does not judge intent: it cannot tell an insult aimed at someone from the same word used innocently, beyond the allowed phrases.

## License

MIT, see [LICENSE](LICENSE).
