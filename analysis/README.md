# Seed data profiling

Throwaway exploration scripts, committed as evidence rather than as product code.
They run the seed data through SQLite in memory and check it against the anomalies
declared in `seed/generate_seed.py`, so the plan rests on measured facts instead of
on trusting the generator.

```powershell
python -m pip install tzdata     # Windows has no IANA database
python analysis/01_profile_seed.py
python analysis/02_profile_seed_followup.py
```

`01` establishes shape and confirms each planted anomaly. `02` fixes a methodological
bug in `01` (coefficient of variation was computed with the mean over all 177 days but
the variance over only the days that had events) and tests two design hypotheses:
whether a mean-based baseline is poisoned by the burst day, and what the real
last-complete-week comparison looks like per location.

## What was measured

| Fact | Value |
|---|---|
| Accounts / events | 20 / 12,626 |
| Range (UTC) | 2026-02-01 10:57 → 2026-07-27 22:20 (177 days) |
| Last day of data | **Monday 2026-07-27** — last complete Mon–Sun week is 2026-07-20..26 |
| Account with zero events | 20, Quiet Harbor Spa |
| Exact duplicates | 12 groups, 12 extra rows, spread Feb–Jul; none inside 2026-07-20..26 |
| Burst | Account 6, Wed 2026-06-03, **805 events vs a typical 10.9/day — 74×** |
| NULL `duration_seconds` | 4.0% of calls (and 100% of non-calls, by definition) |
| NULL `outcome` | 3.1–3.3% across all three event types |
| Weekend effect | Sat/Sun ≈ 22–23 events/day vs ≈ 85 on weekdays |
| Local-day shift | 67 of 12,626 events (0.53%) fall on a different calendar day in account-local time |
| Daily coefficient of variation | 0.62–0.93 for normal accounts; **4.01 for account 6** |
| Distinct location strings | 15 — `'Site A'` appears in 19 different accounts |
| Integrity | no orphans, no negative durations, no events before `created_at`, no duration on non-calls |

## What this forces into the design

1. **Median, not mean, for the baseline.** Account 6's Wednesday baseline is a mean of
   44.6 with the burst included and 12.4 without it. The median is 13.0 either way. One
   outlier day moves a mean-based "normal" by 3.6×; it does not move the median at all.
2. **Compare like weekdays.** Weekends run at roughly a quarter of weekday volume, so a
   flat trailing average marks every Saturday as a collapse.
3. **Gaps are data.** Old Town Barbers has 66 days with zero events out of 177, Willow
   Creek 60. `GROUP BY date` only returns days that have rows, so any window arithmetic
   has to fill missing days with zero rather than skip them.
4. **Small numbers need a confidence signal.** Daily CV reaches 0.93 on the smallest
   accounts. A per-location weekly count of 5 moving to 9 is "+64%" and is also well
   inside noise. The UI has to show the typical range, not just a percentage.
5. **`location` is only unique within an account.** Every grouping and filter must be
   scoped by `account_id`.
6. **"Now" cannot be `GETDATE()`.** The data ends 2026-07-27; today is later than that.
   Anchoring to the real clock renders an empty dashboard.
