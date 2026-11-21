# Word lists

The built-in lists live in `Runtime/Resources/PurrFanity/WordLists/<language>/`, one text file per category. Your own Custom Words lists use the same format.

## Format

One entry per line, in plain lowercase. Case, accents, `sh1t`, `shiiit` and `s h i t` are handled by the filter, so each word only needs listing once.

| Entry | Matches |
|---|---|
| `word` | that whole word only |
| `word => clean, cleaner` | same, with replacements for Replace Similar |
| `word*` | words starting with it |
| `*word` | words ending with it |
| `*word*` | words containing it |
| `two words` | the words in that order |
| `# text` | a comment, ignored |

Different spellings still need their own line, the filter can't work out `fuking` from `fucking`.

Keep wildcards to stems that don't show up inside ordinary words. `*ass*` would catch `class`. If a wildcard catches a real word anyway, add that word to `allow.txt`.

## Files

| File | Category |
|---|---|
| `mild.txt` | light swearing, like damn and hell |
| `strong.txt` | strong swearing |
| `sexual.txt` | sexually explicit terms |
| `insults.txt` | insults and harassment aimed at a person |
| `racism.txt` | racial, ethnic and religious slurs |
| `phobic.txt` | homophobic and transphobic slurs |
| `ableism.txt` | slurs aimed at disabilities |
| `allow.txt` | words that are always fine |
| `common.txt` | the language's most common everyday words |

Any file can be left out.

`allow.txt` holds real words a list entry would otherwise catch, like `scunthorpe` or `niggle`. A line with several words only lets them through together, so `moby dick` allows that phrase while `dick` on its own is still filtered.

`queer` is left out of `phobic.txt` on purpose, it's widely used to describe oneself.

`common.txt` keeps two words from being read as one: `fu ck` is caught, but `pen is` isn't, since `is` is common. Without it, words split by a space aren't joined at all.

## Adding a language

Copy the `en` folder, rename it to the language code (`de`, `fr`, ...) and replace the words. A profile loads it once the code is added to its Languages.
