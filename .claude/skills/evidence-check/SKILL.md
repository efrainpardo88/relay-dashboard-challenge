---
name: evidence-check
description: Audit an uncommitted diff in this repository against the failure modes it has actually produced — claims that outrun their evidence, aggregation and window errors no type checker sees, rendered-output bugs that pass a green suite, test fixtures whose expected values came from the implementation, and prose written about the work standing in for the work. Read-only: never runs the suites, never edits files. Use before committing or before submitting, or when asked what was missed. Not a bug hunt — the built-in /code-review covers correctness in code; this one audits claims, evidence and artifact integrity.
---

# evidence-check

An audit, not a review. It asks one question of everything in the diff: **what is the
evidence, and where is it?**

Nothing here is generic best practice. Checks 1–4 and 7 come from failures this repository
actually produced, recorded in `ai-log/reflection.md`; checks 5 and 6 enforce rules in
`CLAUDE.md` that no failure has tested yet. Checks 1–6 are specific to this project. Check 7
is the one that matters most and the one that ports.

## The rule that makes this work

**Every check produces a citation or a finding. "Looks fine" is not an allowed output.**

If a number cannot be traced to a script in `analysis/` or to a test, the inability to cite
it *is* the finding. Without this, the checklist gets walked through nodding — which is
precisely how a dropdown bug survived ten green tests.

So the report has two lists, and **both must be non-empty for the audit to have happened**.
An empty findings list is a good outcome. An empty citations list means the work was skipped.

## Scope

- **The uncommitted diff only** (`git diff`, `git diff --staged`, untracked files). Not the
  whole repository.
- **Read-only.** Do not run `dotnet test` or `ng test`, do not edit files, do not fix what
  you find. Report it. Auditing a diff and debugging a failing test are different activities,
  and mixing them means the audit gets abandoned partway.
- Facts live in `CLAUDE.md`, `PLAN.md` and `analysis/README.md`. Read them; do not restate
  them here. This file carries the procedure, the repo carries the facts.

---

## 1. Claims that outrun their evidence

*From: the agent calling timezone handling "probably the single biggest differentiator"
before measuring it at 0.53%.*

- Every figure in prose, comments or commit messages: cite the `analysis/` script or the test
  that produces it. A plausible-looking number with no source is a finding.
- Every statement about how a tool, engine or library behaves: it must be labelled
  **verified**, naming what was tested and the scope, or **assumed**. Unlabelled is a finding
  even when the claim happens to be true.
- Superlatives and absolutes — "the biggest", "always", "never", "cannot" — need a
  measurement behind them or they get struck.

## 2. Aggregation traps a type checker cannot see

*From: a coefficient of variation computed with the mean over 177 days and the variance over
only the days that had events, and a sweep whose baseline window fell off the start of the
dataset and read as all zeros.*

- Numerator and denominator drawn from different populations.
- Windows, sweeps or baselines that extend past the bounds of the data — everything outside
  reads as zero and looks like a real result.
- `GROUP BY` without gap filling. It returns only the rows that exist; a median over 5 of 8
  weeks is wrong and looks fine.
- Division where the denominator can be zero, and what gets emitted when it is.
- A mean where the repo decided on a median.
- Any query, join, group or filter on `location` not scoped by `account_id`.

## 3. What the suite structurally cannot see

*From: all three filter dropdowns showing their first option regardless of the URL, past ten
green frontend tests.*

- Read what the tests actually assert. If they only cover component state and outbound
  requests, then rendering is unverified no matter how many pass — say so.
- Bindings applied before the DOM they target exists (`[value]` on a `<select>` whose
  `<option>`s come from `@for` is the known case here).
- For any change to rendered output, ask for evidence that the running app was looked at. No
  evidence is a finding. A green suite is not evidence of what a person sees.

## 4. Fixture provenance

*From: five tests failing on their first run because the expected MAD in the fixture was
wrong — the implementation was right.*

- For each expected value in a test: did it come from `analysis/`, from hand arithmetic shown
  in a comment, or from running the code and copying the output?
- A value copied from the implementation's output proves only that the code is consistent
  with itself. Flag it, and say what independent source it needs.
- Fixtures presented as real seed data must name the account and location. Synthetic fixtures
  must say they are synthetic and why no real data reaches that branch.

## 5. Artifact integrity

*From: `CLAUDE.md`, "Artifacts that are not yours to rewrite".*

- `PLAN.md`: was the body edited instead of an amendment appended? Any change above the
  amendments is a finding, whatever it improved.
- `ai-log/raw/`: hand-edited, tidied, translated, or a session removed. Regeneration is the
  only legitimate change.
- `seed.sql`: modified at all.

## 6. Language

*From: `CLAUDE.md`, "Language".*

- Anything not in English — identifiers, comments, filenames, docs, test names, commit
  messages. The exception is `ai-log/raw/`, which stays verbatim.

---

## 7. Self-narration — prose about the work standing in for the work

*From: the agent writing, inside the AI log itself, that it had suggested assembling the log
by hand and that the human had rejected that and pushed for the raw transcript. The record
one directory away shows the reverse — the human asked for a hand-assembled log and the agent
talked them out of it. The invention handed the good judgement to the reader.
(`ai-log/decisions/01-reading-the-brief.md`, "Where the agent overrode me".)*

**This is the check to run hardest, and the reason is structural: code has tests, prose has
nothing.** Every other category here has some automatic verifier standing behind it — a
compiler, a test run, a query you can execute. This one has none, so it is where a false
statement survives longest. It is also the check that ports without rewriting: checks 1 and 5
carry portable principles but local examples, whereas this one describes how models write
about their own work rather than anything about this codebase.

- **Attribution.** Any sentence assigning an action, a decision or a motive to a person — "I
  caught", "the agent proposed", "we decided", "at my request", "as you suggested". Cite the
  turn in `ai-log/raw/` or the commit that shows it. If you cannot cite it, it is a finding
  no matter how plausible it reads.
- **Direction of the error.** When a narrative and the record disagree, note which way the
  narrative leans. The failure this comes from flattered the human. A story that flatters its
  reader is the specific defect to expect here, not a random slip — so check the flattering
  sentences first.
- **Self-assessment.** "Robust", "carefully designed", "thoroughly tested", "clean",
  "comprehensive". Claims about the quality of the author's own output, backed by nothing.
  Substantiate or strike.
- **Retro-justification.** Reasoning presented as having driven a decision when it was
  assembled afterwards to explain one. Check the order in the transcript and in the commits.
- **Narrative smoothing.** A tidy cause-and-effect account where the record shows dead ends,
  reversals and fumbling. Tidiness is itself the smell; the raw record is never that clean.
- **Prose displacing work.** A diff where the description of a thing grew and the thing did
  not. Name the artifact that changed. If the only artifact that changed is a description of
  an artifact, that is this failure in its purest form.

---

## Report format

```
CITATIONS
  <claim or number>  →  <analysis/script.py §section | test name | transcript line>
  ...

FINDINGS  (most serious first)
  [check N] path/to/file.ext:LINE
    What: <the claim, code or prose at issue, quoted>
    Why:  <which failure mode, in one sentence>
    Test: <what would have to be true for this not to be a finding>
```

`Test:` is required on every finding. It forces a falsifiable statement instead of an
opinion, and it tells the author exactly what to go and check.

## What this does not do

- Hunt generic bugs — nulls, off-by-one, performance, security. The built-in `/code-review`
  covers that ground; run both.
- Restate the rules in `CLAUDE.md`. Read them there. Duplicated rules drift.
- Fix anything.

## Porting this to another project

Checks 1–6 are this repository's incidents and rules, and must be rewritten from that
project's own failures — copying them verbatim would reintroduce exactly the
generic-checklist problem this file exists to avoid. Check 7 travels as-is; it is a property
of how models write about their own work, not of any codebase.

Its first run was on its own diff, and it produced three findings in this file: a
misdescription of the very incident check 7 is built on, an opening claim that overstated
where the checks came from, and an unmeasured absolute. All three are fixed above. That is
the expected result, not an embarrassing one — prose about the work is exactly where this is
supposed to bite, and the file had no automatic verifier standing behind it either.
