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
| `common.txt` | the language's most common everyday words, also used to tell languages apart |

Any file can be left out.

`allow.txt` holds real words a list entry would otherwise catch, like `scunthorpe` or `niggle`. A line with several words only lets them through together, so `moby dick` allows that phrase while `dick` on its own is still filtered.

A line with a wildcard allows every word with that piece in it: `*szarv*` lets `szarvas` and `rénszarvas` through past `*szar*`. That's for roots that hide inside ordinary words, keep the pieces long enough that they don't cover the bad word itself.

A listed word typed with other accents is a different word. In Hungarian `szár` isn't `szar`, so `száraz` and `császár` pass on their own. Only the accents the language itself uses count, so `shìt` is still `shit`.

`queer` is left out of `phobic.txt` on purpose, it's widely used to describe oneself.

`common.txt` keeps two words from being read as one: `fu ck` is caught, but `pen is` isn't, since `is` is common. Without it, words split by a space aren't joined at all.

Built in: `en` (English) and `hu` (Hungarian).

In `hu`, `cigány` and `zsidó` are filtered as racism, since they're mostly used as slurs, so neutral mentions are masked too. `mongol` is left out, it's also the word for Mongolian.

Hungarian makes up insults by gluing words together (`lószar`, `kutyafasza`, `seggarc`, `cigánygyerek`), so its hurtful roots are listed as `*root*` and caught inside any word. The ordinary words they hide in (`szarvas`, `faszén`, `baromfi`) are allowed as pieces in `allow.txt`, typed with or without accents. Ordinary words used as insults in a sentence (`te ló`) are listed as phrases.

## Several languages

Every loaded language is checked on every message, so mixed messages need nothing special: in `ez egy fucking jó game` the English word is filtered, the Hungarian ones are left alone. A word that's only filtered in one language is filtered whatever language the rest of the message is in.

What needs deciding is a look-alike, a word one language filters and another uses every day: `asz` is Hungarian for ace (`ász`) and an English evasion of `ass`. Each look-alike is settled on its own, by the first of these that tells:

1. **Its letters.** `ász` is spelled with a letter English doesn't use, so it's Hungarian whatever is around it. The letters each language uses are read from its own lists.
2. **Accents typed elsewhere.** When a listed word has accents and someone who types accents elsewhere in the message leaves them out, they meant the other language's word.
3. **The words around it.** Up to four on each side vote, through `common.txt`, `allow.txt` and the other filtered words: `nice asz lol` is English, `van egy asz a kezemben` is Hungarian. Words common in both (`a`, `is`) don't vote.
4. **The sender's language**, when the call passes one.
5. **The profile's Fallback Language.**
6. Otherwise it's filtered. With the Fallback Language set to None, a lone `asz` is filtered.

### Where a look-alike goes

- A real word that a list catches by accident, and nobody would type to mean the bad word (`Kafka` read as `cafka`, `geekier` as `geci`): the `allow.txt` of the language whose list catches it. It's then always fine.
- A word that's really both (`asz` and `ass`): the `common.txt` or `allow.txt` of the language it's ordinary in. The rules above then decide.

## Adding a language

Copy the `en` folder, rename it to the language code (`de`, `fr`, ...) and replace the words. A profile loads it once the code is added to its Languages.

Give it a good `common.txt`, a few hundred everyday words with their usual chat forms. It's what tells a message in that language from one in another. Then try its everyday words with the other languages loaded, in the profile's Try it box, to find look-alikes.
