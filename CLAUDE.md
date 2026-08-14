# CLAUDE.md — Relay / DASH-247

**Before touching aggregation, seeding or the baseline logic, read `PLAN.md` (including the
Amendments at the end) and `analysis/README.md`.** The rules below are the conclusions; those
two files carry the measurements they rest on. If a rule looks wrong, the evidence is there —
check it before overriding.

## Data
- `seed.sql` is production data. Never edit it. Idempotency comes from wrapping the load, not
  from rewriting the file.
- Deduplicate on the full value tuple keeping `MIN(id)`; report the excluded count in the API
  response rather than dropping rows silently. There are 12.
- The 2026-06-03 burst (805 events, account 6) **stays in the data**. It is real activity and
  the median absorbs it. Never filter it out as dirty data.
- `location` is unique only *within* an account — `'Site A'` exists in 19 accounts. Every
  query, join, group and filter is scoped by `account_id`.

## Aggregation
- SQL aggregates as far as weekly totals per `(account_id, location, week_start)` and stops.
  Median, MAD, band and status are computed in C#. See PLAN Amendment 2.
- Missing weeks and days materialise as `0`. `GROUP BY` returns only rows that exist; a
  baseline averaged over 5 of 8 weeks is wrong and looks fine.
- Baseline median of 0 → no percentage at all. Never emit `Infinity`, `NaN` or `-100%`.
  `low_volume` (median < 5/week) shows counts and band but suppresses the status badge.

## Time
- Weeks are Monday–Sunday in the **account's local time**, bucketed on `occurred_local_date`.
- `occurred_local_date` is precomputed at load time in C# (`TimeZoneInfo`, IANA-aware) and
  indexed — it keeps the day bucket sargable and the conversion unit-testable without a
  database. Do not move the conversion into the query: `AT TIME ZONE` on this engine rejects
  IANA ids and takes Windows ones, verified in PLAN Amendment 3.
- "Now" is `MAX(occurred_at)`, never `GETUTCDATE()` or `DateTime.Now`. The data ends
  2026-07-27; the system clock renders an empty dashboard.

## Tests
- Golden numbers come from `analysis/`, never from intuition. Needing a new expected value
  means measuring it there first.
- `insufficient_history` and `baseline_zero` have no real data behind them in this seed
  (minimum baseline median is 3.5, minimum history 24 weeks). Test them with synthetic
  fixtures — do not invent seed rows to reach them.

## Stack
- EF Core owns migrations and the `accounts` read. The pulse aggregate is Dapper. Do not
  "unify" it back into LINQ.
- All UI state — account, week, baseline window, event type — lives in URL query params. Not
  a service field, not `localStorage`. It must survive reload and be shareable by link.

## Out of scope
- Auth, alerting/notifications, ML/forecasting, trend charts, per-event drill-down. Do not add
  them, and do not scaffold them "for later".
