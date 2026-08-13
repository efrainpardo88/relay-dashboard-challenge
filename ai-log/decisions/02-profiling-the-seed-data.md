# 02 — Profiling the seed data before planning

**When:** 2026-08-13, still before `PLAN.md`
**Agent:** Claude Code (Claude Opus 5, 1M context)
**Artifacts:** `analysis/01_profile_seed.py`, `analysis/02_profile_seed_followup.py`, `analysis/README.md`

## Why this came before the plan

The ticket asks what "normal" means. That is a data question before it is a product
question — the answer depends on how noisy this data actually is. Planning first and
profiling second would have meant designing a baseline against an imagined dataset.

## What I directed the agent to do

The starter ships `seed/generate_seed.py`, the deterministic generator. Reading it
gives the planted anomalies for free: a zero-event account, a burst day, 12 exact
duplicates, planted NULL rates, weekend suppression. I had the agent read it.

Then I made it verify every claim against the actual rows rather than reporting the
generator's intent as fact. The generator says what was *intended*; the evaluation is
against `seed.sql`. All six claims held, and the measured magnitudes turned out to
matter more than the claims themselves — 805 events on the burst day against a typical
10.9, a 74× spike, is a very different design input than "there is a burst."

## Where I overrode the agent

- **It overstated the timezone finding.** It called account-local day handling
  "probably the single biggest differentiator in the exercise" before measuring it.
  Measured: 67 of 12,626 events (0.53%) land on a different local day. Real, worth
  getting right, not worth reprioritising the plan around. I had it correct the claim
  and I am not budgeting the day around timezone work.
- **Its first profiling script had a statistics bug.** It computed each account's
  coefficient of variation using a mean over all 177 days but a variance over only the
  days that had events — inconsistent denominators, and worst exactly on the sparse
  accounts the metric was meant to describe. The agent caught this itself on review and
  flagged it; the corrected numbers are in `02_profile_seed_followup.py`. Recording it
  because the first version looked completely plausible and I would have quoted those
  CVs in the plan.

## The finding that changed the design

Account 6's Wednesday baseline:

| | mean | median |
|---|---|---|
| all data | 44.6 | 13.0 |
| burst day excluded | 12.4 | 13.0 |

A single planted outlier moves a mean-based "normal" by 3.6×. It does not move the
median at all. That is the whole argument for a median baseline, it is measurable, and
it is explainable to a customer admin without the word "model". This is now the
load-bearing decision in `PLAN.md`.

## Second finding: the dataset is aligned to the ticket

Data ends **Monday 2026-07-27**. The ticket says a customer admin "should be able to
look at this Monday morning and act on it". The last complete Mon–Sun week is
2026-07-20..26. The dataset was built for that exact scenario, so the default view
anchors there rather than to the system clock.

## Cost

~45 minutes. Bought the two decisions above plus a written list of the edge cases the
tests have to cover, which is most of the value of the profiling.
