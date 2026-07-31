# The question bank

Two directories per locale: `BM1/Text/<LANG>/` for strings and
`BM1/Rounds/<LANG>/` for the question records.

## Strings — `.str`

Plain **UTF-8**, one string per line, no header. Dutch content uses
`À Ä È É Ë Ï Ö Ü` plus typographic quotes and ellipsis, so decoding as Latin-1
produces mojibake on roughly 366 bytes of `quid.str`.

| File | Lines | Holds |
|---|---|---|
| `quid.str` | 11185 | question text and answer options |
| `default.str` | 659 | UI strings, menus, warnings |

References from the round data are **1-based**; id 0 means "no string". This is
the single most important detail in the format — read as 0-based, every question
resolves to its own first answer, which looks plausible enough to pass a casual
glance.

Long UI strings in `default.str` embed a literal backslash-n for line breaks
rather than a real newline, since a real one would end the record.

## Named lookups — `.ndx`

Text, one `hash id` pair per line, 659 of them, matching `default.str`
line-for-line. The hash is a signed 32-bit value over the string's name, so UI
code fetches by name rather than by index. The exact hash function has not been
identified yet — nothing so far needs it, because the id column alone is enough
to read the table.

## Questions — `.rnd`

Fixed **16-byte records**, eight little-endian `uint16` fields:

| Field | Meaning |
|---|---|
| 0 | global question id (1-based) |
| 1 | song id, 0…999 |
| 2 | question text (id into `quid.str`) |
| 3 | mostly 0; only 5 distinct values across the whole bank, purpose unknown |
| 4 | **correct answer** |
| 5–7 | the three distractors |

`quid.str` stores each question followed by its options, so a record's fields
4–7 are usually consecutive ids — but not always, because identical answer
strings are shared rather than duplicated.

### Which option is correct

Field 4. This is inference, not something the format states:

- Across the 1000 questions shared between `qall` and `qtitle`, both the option
  *set* and its *order* are identical — so ordering is meaningful and stable.
- There is no correct-index field: field 3 is zero in 4367 of 8374 records and
  has only 5 distinct values overall.
- The engine exposes `GetRandomisedIndex` alongside `GetCorrectAnswerIndex`,
  which is what you need if the data is stored correct-first and shuffled for
  display.

### Pools

`qall.rnd` is the master bank; every other file is a **subset reusing the same
global question ids**. A port should load `qall` as the bank and treat the rest
as round-type selections.

| Pool | Questions | Id range | Distinct question texts |
|---|---:|---|---:|
| `qall` | 8374 | 1..8374 | 1560 |
| `qicfire` | 4367 | 1..4367 | 1559 |
| `qassoc` | 4007 | 4368..8374 | 1 |
| `qhotseat` | 2745 | 1..4367 | 711 |
| `qtriger` | 2442 | 1..4367 | 408 |
| `qdive` | 2303 | 1..3854 | 305 |
| `qartitle` | 2000 | 1..2000 | 2 |
| `qpasply` | 1622 | 2001..4164 | 849 |
| `qtrivia` | 1388 | 2001..4164 | 848 |
| `qsabot` | 1234 | 1001..2343 | 2 |
| `qartist` | 1000 | 1001..2000 | 1 |
| `qtitle` | 1000 | 1..1000 | 1 |
| `qbluff` | 234 | 2110..2343 | 1 |

`qartitle` is exactly `qtitle` ∪ `qartist`. Pools with a single distinct
question text are the fixed-prompt rounds — every question asks the same thing
about a different song. `qyearevent.rnd` exists but is empty.

`qqi.dat` is 33496 bytes = one `uint32` per `qall` record, almost certainly the
song clip reference; `rri.dat` (12008 bytes) is not yet identified. Neither is
needed to read questions.

## Tools

```bash
obz quiz stats                  # per-pool counts, id ranges, and validation
obz quiz dump --pool qall       # resolved questions -> extracted/questions-NET.txt
```

`stats` validates that every string reference resolves and that every pool is a
subset of `qall`; both hold for the Dutch bank. `dump` writes to a file rather
than stdout because the output is game content and belongs with the other
extracted data.
