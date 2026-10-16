---
name: english-teacher
description: Rewrite or review UI copy (titles, descriptions, labels, placeholders, empty states, toasts, errors) into terse technical English with domain terms, scannable at a glance. Audits for inconsistent terms, grammar errors, redundant text/labels, and mismatched verbs, and applies ASD-STE100 (Simplified Technical English) to full sentences. Use when writing or editing any user-facing text in web/, or when the user says "english-teacher", "tighten the copy", "too wordy", "simplify the text", "check the copy", "inconsistent wording".
---

# English teacher

Readers are print-farm operators. They know the domain and they skim. Write for someone with ADHD: every word must earn its place.

## Rules

### Style

1. **Fragments over sentences.** "No printers found", not "We couldn't find any printers yet."
2. **Domain terms, not lay explanations.** LAN, subnet, SN, access code, nozzle, AMS, plate, profile. Never "on the same network as OpenFarm".
3. **One idea per line.** Split or cut; never join with "and/so/while".
4. **Cut reassurance and restating the obvious.** Drop "This list updates automatically", "Make sure…", "Please", "You can…", "It may have been…".
5. **Errors = what failed + what to check.** "Connection failed. Check IP, access code." No apology, no "Could not … this printer's …".
6. **Lists over prose.** "Check: powered on, same subnet, LAN mode on."
7. **Shorthand is fine when unambiguous.** "Blank = keep current", "Add by IP", "3 new".
8. **No description if the UI already says it.** Delete page descriptions that repeat the title or the visible list.
9. Sentence case. No ALL CAPS, no trailing "→", no exclamation marks.

### Consistency

10. **One term per concept, everywhere.** Pick one, grep for the rest, fix all. Never alternate synonyms for the same thing (title, column header, toast, empty state, aria-label, test).
11. **One verb per action, across the app.** Button, pending state, toast and confirm dialog share it: "Add printer" → "Adding…" → "Added X."
    - Create a record: **Add** (not New/Create) — except routed `/new` page titles, which may say "New X".
    - Soft-removal from the farm: **Remove**. **Delete** only for irreversible destruction (files, credentials). Never mix on the same entity.
    - Discard edits: **Discard changes**. Not "Discard unsaved", "Cancel changes", "Revert".
    - Dismiss a dialog without acting: **Cancel**. Leave an edit page: **Cancel** (with unsaved-changes guard).
    - Persist edits: **Save**. Persist a new record: **Add X** or **Save** — match the page's title verb.
12. **Same casing and plurality for the same noun.** Singular on single-item actions ("Remove part"), plural for counts ("3 parts"). Always pluralize the count noun correctly ("1 spool", "2 spools").
13. **Same shape for parallel items.** Sibling tabs, columns, menu items, and empty states use one grammatical form (all nouns, or all verbs) and one punctuation style (periods on all full-stop messages or on none).
14. **Same units and number format.** "0.4 mm", "250 g", "215 °C": space before unit, same abbreviation everywhere. Same date/time format.
15. **Domain glossary is canonical.** Use the term the code/spec uses (Part, Assembly, Job, Spool, Filament, Printer, Queue, Feed unit). Don't invent UI-only synonyms (item, product, machine, roll, task) for these. Drift to flag: "machine" vs "printer", "roll" vs "spool", "task" vs "job".

### Grammar & mechanics

16. **Correct grammar still applies to fragments.** Check: subject–verb agreement, article misuse ("a printer" / "an AMS"), plural/singular mismatch, wrong tense in status text, wrong preposition ("on the queue" → "in queue"), typos, doubled words.
17. **Tense by state.** Pending: present participle ("Saving…"). Success: past participle ("Saved."). Failure: noun + "failed" ("Save failed."). Never mix ("Saving failed", "Failed to save").
18. **Punctuation.** Ellipsis is the single character `…`, not `...`. Straight vs curly quotes consistent. No double spaces. No trailing period on labels, buttons, headings, or placeholders. Fragments that stand alone as messages end with a period (toasts, errors) — match the file's existing choice.
19. **Spelling variant.** American English ("color", "canceled" vs "cancelled": pick the one already in the codebase and keep it).

### Redundancy

20. **Cut repeated words in context.** If the card is titled "Filament", rows aren't "Filament name" → "Name". If the dialog title says "Remove part", the button is "Remove", not "Remove part".
21. **Cut duplicate text.** Same string in title + description + empty state + tooltip: keep one. `aria-label`/`title` identical to visible text → delete the attribute.
22. **Cut labels that restate the control.** "Search" + placeholder "Search parts…" → keep one. "Enter name" placeholder under a "Name" label → delete the placeholder or make it an example ("e.g. Bracket").
23. **Cut filler words.** "Currently", "Successfully", "Selected" (on a selection bar), "Item(s)", "Details" suffix, "Information", "Your", "the" before UI nouns.
24. **Don't label the obvious empty state twice.** Heading "No parts" + body "There are no parts" → heading only, plus the CTA.

### ASD-STE100 (Simplified Technical English)

Applies to every full sentence: descriptions, errors with a sentence body, confirm dialogs, help text, tooltips. Labels, buttons, column headers, tabs and status fragments are exempt from the article and tense rules (25, 29), but not from the vocabulary rules (26–28). If STE and rules 1–9 conflict on a full sentence, keep the sentence STE-compliant and as short as possible. Cut it to a fragment if the UI allows.

25. **Keep articles in full sentences.** "Remove the part from the queue", not "Remove part from queue". Fragments are exempt (rule 1).
26. **One word, one meaning, one part of speech.** Use the approved word, not a synonym: use / not "utilize"; start / not "initiate", "commence"; stop / not "terminate"; make sure / not "ensure", "verify" (as filler); remove / not "eliminate"; show / not "display" (as a verb); set / not "configure" (as a verb); need / not "require". Never use a word as both noun and verb in nearby copy ("a check" and "check the IP").
27. **No phrasal verbs or idioms.** "Turn on the printer" is fine (STE-approved). "Set up", "hook up", "figure out", "bring up", "kick off" are not. Use "install", "connect", "find", "show", "start".
28. **Noun clusters: 3 words maximum.** Rewrite longer ones with "of", "for", "in". "Printer filament feed unit status" → "Status of the feed unit".
29. **Approved verb forms only in sentences.** Allowed: infinitive, imperative, simple present, simple past, simple future, past participle (as adjective, passive only in descriptions). Not allowed: present participle as a verb, perfect ("has failed"), progressive ("is connecting"), conditional ("would", "could"), subjunctive. Pending UI states ("Saving…") are exempt as fragments.
30. **Instructions: imperative, one instruction per sentence.** "Turn on LAN mode. Enter the access code." Not "Turn on LAN mode and enter the access code".
31. **Maximum sentence length: 20 words for instructions, 25 for descriptions.** One topic per sentence; up to 6 sentences per paragraph.
32. **Active voice in instructions.** "The printer rejected the access code", not "The access code was rejected". Passive only in descriptions when the agent is unknown.
33. **No contractions, no "-ing" nouns where a plain noun exists, no vague pronouns.** "Cannot", "Do not", not "Can't", "Don't". Repeat the noun instead of "it/this/that" when the referent is not in the same sentence. "Do not" not "Never" in a warning.
34. **Warnings and cautions: use the command first.** "Do not remove the part while a job is running." Put the consequence in the next sentence. Keep "Warning" for injury or data loss, "Caution" for equipment damage, "Note" for extra information.
35. **Use words consistently with the glossary, units, and numerals.** Write numerals ("3 parts"), not words. SI units with a space. Same word for the same part every time (rule 10 and 15 apply).

## Examples

| Before | After | Rule |
|---|---|---|
| Looking for printers on your network… | Scanning LAN… | 2 |
| Make sure the printer is powered on, on the same network as OpenFarm, and has LAN mode enabled. | Check: powered on, same subnet, LAN mode on. | 4, 6 |
| Couldn't connect to this printer. Check the IP address, serial number and LAN access code. | Connection failed. Check IP, serial, access code. | 5 |
| While OpenFarm is connected, this printer is disconnected from Bambu Cloud and the official app. | LAN mode disconnects Bambu Cloud and Bambu Handy. | 1, 3 |
| Leave blank to keep the current connection | Blank = keep current | 7 |
| Printer-reported nozzle: 0.4 mm | Nozzle: 0.4 mm (reported) | 7 |
| It may have been removed, or the link is out of date. | Removed or invalid link. | 4 |
| Could not update printer. | Update failed. | 5, 17 |
| Delete part (menu) / Remove part (dialog) | Remove part / Remove part | 11 |
| Discard unsaved / Discard changes | Discard changes / Discard changes | 11 |
| Create one (empty state) / Add part (button) | Add part / Add part | 11 |
| Saving failed / Failed to save | Save failed. | 17 |
| Loading... | Loading… | 18 |
| 3 spool / 1 spools | 3 spools / 1 spool | 12 |
| Filament name (column in Filament card) | Name | 20 |
| Search parts (label) + "Search parts…" (placeholder) | placeholder only | 22 |
| 2 selected items | 2 selected | 23 |
| Machine offline | Printer offline | 15 |
| Print job 1 of queue | Job 1 in queue | 16 |
| Please utilize the access code to initiate the connection. | Use the access code to start the connection. | 26 |
| Set up the printer and then hook it up to LAN. | Add the printer. Connect it to the LAN. | 27, 30 |
| Printer filament feed unit status | Status of the feed unit | 28 |
| The connection hasn't been established. | The connection failed. | 29, 33 |
| Turn on LAN mode and enter the code. | Turn on LAN mode. Enter the code. | 30 |
| The access code was rejected. | The printer rejected the access code. | 32 |
| Remove part from queue? (dialog body) | Remove the part from the queue? | 25 |
| Never remove a part while it prints! | Do not remove the part while a job is running. | 34, 9 |

## Process

1. **Collect** every user-facing string in scope: JSX text, `title`/`description`/`placeholder`/`aria-label`/`alt` props, `toast.*`, zod messages, column headers, empty states, dialog titles/confirm buttons.
2. **Build a term map** before editing: group strings by concept (entity noun, action verb, state). Use grep across `web/src` for each variant (e.g. `Remove|Delete`, `Add|Create|New`, `Discard`) so cross-file drift is visible, not just drift inside the files in scope.
3. **Audit** against the rules and tag each finding with its rule number and category: `style`, `inconsistent`, `grammar`, `redundant`, `ste`. Classify each string first as fragment (label, button, header, status) or full sentence; STE rules 25 and 29 apply to sentences only.
4. **Show** a Now → New table (with rule # and category) before editing unless the user asked to just apply. Group inconsistent-term findings by concept with every location, and state which variant wins and why (most-used, or matches glossary).
5. **Keep** field labels and validation messages that are already terse and consistent.
6. **Apply** terminology fixes app-wide for the chosen variant, not only in the files in scope; otherwise you create new drift.
7. **Update tests** that query by the changed text (`getByText`, `getByLabelText`, `getByRole({ name })`), then run `bunx vitest run <paths>`, `bunx tsc -b`, `bun run lint` in `web/`.
