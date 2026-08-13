# PLAN — DASH-247

Written 2026-08-13, before implementation. Left as-written on purpose; where the build
diverges from this, the README says so rather than this file being edited to match.

Profiling came first (`analysis/`), because "what is normal" is a data question before
it is a product question. The numbers cited below are measured, not assumed.

---

## 1. What I think the ticket is asking for

The customer question is *"is this number normal **for us**?"* — self-relative, not
relative to an industry benchmark. Nobody asked to be compared to other Relay customers,
and the data has no peer-group framing (20 accounts across 8 industries, 1–15 locations
each — not enough to benchmark against anyway).

Second, from product: *"a customer admin should be able to look at this Monday morning
and act on it."* That is a strong hint about the shape. It means:

- a **weekly** cadence, not daily and not monthly
- **the week that just ended**, not week-to-date — on Monday morning you review what
  just happened
- it has to end in an **action**, which for a multi-location customer means "go look at
  Site D", not "your numbers are down 6%"

Third: *"multi-location customers matter"* appears in the ticket, and the support pain
in the background doc is specifically *"struggle to spot which location needs attention."*
Two independent mentions. A pure account-level roll-up would fail the ticket.

So: **the weekly pulse.** One view. For the last complete Mon–Sun week, show the account
total against its own recent norm, then every location ranked by how far it deviates from
*its own* norm. The answer to "is this normal" is a status, not a number, and the answer
to "what needs attention" is the top of the list.

### Explicitly not building

- **Alerting/notifications, ML/forecasting** — out of scope per the ticket.
- **Peer/industry benchmarking** — "normal for us" is self-relative; see above.
- **Trend charts over time.** Tempting and pretty, but a chart shows *change*, not
  *normality*, and answering "is this normal" from a line chart is exactly the work the
  customer is currently failing to do. Deferred, noted in README.
- **Drill-down into individual events.** The ticket ends at "act on it"; the acting
  happens outside the dashboard.
- **Auth**, per the brief. Account is chosen from a dropdown, standing in for "the
  account this admin belongs to".

---

## 2. What "normal" means, concretely

For a given location (or the account as a whole):

```
current      = total events in the last complete Mon–Sun week, account-local time
baseline     = MEDIAN of the totals of the N complete weeks immediately before it
spread       = MAD (median absolute deviation) of those same N weekly totals
typical band = baseline ± 2 * (MAD / 0.6745)      # robust ~95% band
status       = below | normal | above, by where `current` falls in the band
```

N defaults to 8 and is user-selectable (4 / 8 / 12). That selector doubles as the
required reload-persistent user input.

### Why median and MAD, and not mean and stdev

This is the measured reason, not a stylistic preference. Account 6 (Metro Collision) has
a planted burst: **805 events on Wed 2026-06-03 against a typical 10.9/day — 74×**. Its
Wednesday baseline:

| | mean | median |
|---|---|---|
| all data | 44.6 | 13.0 |
| burst day excluded | 12.4 | 13.0 |

One outlier day moves a mean-based "normal" by 3.6×. It leaves the median untouched. A
customer whose baseline silently absorbed a one-off spike would be told that six normal
weeks in a row were catastrophic. Median + MAD gets this right without special-casing the
burst, without excluding data, and without anything the ticket calls out of scope.

### Why complete weeks, not rolling windows

A complete Mon–Sun week contains exactly one of each weekday, so day-of-week seasonality
cancels out of a week-over-week comparison for free. That matters here: measured weekend
volume is ~22–23 events/day against ~85 on weekdays. Any partial-week or "last 7 days
vs. daily average" framing would have to correct for that explicitly. Comparing whole
weeks avoids the problem rather than solving it, which is the cheaper correct answer.

Cost of this choice: on Monday the freshest data is up to 7 days old. Accepted — it
matches the stated Monday-morning workflow.

### Honesty guards (these are the point, not the polish)

Real accounts in this data break the naive version of the formula:

| Situation | Measured in seed | Behaviour |
|---|---|---|
| Account with no events at all | Account 20, Quiet Harbor Spa, 0 rows | Empty state: "no activity recorded", no percentages |
| Fewer than 4 prior complete weeks of history | possible near range start | `insufficient_history`, show counts only |
| Baseline median is 0 | sparse locations | No percentage — % change from zero is undefined, not infinite |
| Baseline median < 5 events/week | Old Town Barbers ~6.6/wk account-wide | `low_volume`: show counts and band, suppress the status badge. A location going 5 → 9 is "+64%" and is also pure noise |
| MAD = 0 (identical recent weeks) | likely on small locations | Fall back to a `sqrt(baseline)` spread (count data ≈ Poisson) rather than a zero-width band that flags everything |
| Days with no events | 66 of 177 days for account 16 | Fill gaps with 0. `GROUP BY date` returns only days that have rows — the single easiest way to get this silently wrong |

---

## 3. Data handling decisions

**Duplicates.** 12 exact value-matches with distinct ids (same account, location, type,
timestamp to the second, duration, outcome). Two identical calls at the same location in
the same second is a double-write, not two calls. **Deduplicate** on the full value tuple,
keeping `MIN(id)`, and surface the excluded count in the API response so it is visible
rather than silent. Assumption, flagged below. (None of the 12 fall inside the default
display week, so this is hygiene, not a demo-changer.)

**The burst day stays in the data.** It is real recorded activity; suppressing rows would
be inventing a cleaner dataset. The median baseline is what makes it harmless.

**NULLs.** `outcome` is null on ~3.2% of rows, `duration_seconds` on 4.0% of calls (and
on 100% of non-calls by definition). The pulse counts events, so neither affects the
headline. Relevant only if a later cut slices by outcome — noted, not built.

**Timezones.** `occurred_at` is UTC; each account has an IANA zone. Measured impact:
**67 of 12,626 events (0.53%)** land on a different calendar day in account-local time.
Small, but it is the difference between a correct daily bucket and a wrong one, and the
range spans a DST transition so a fixed offset will not do.

The wrinkle: **SQL Server's `AT TIME ZONE` takes Windows zone ids, not IANA**. Options
were (a) map IANA→Windows in SQL, (b) bucket in C# and give up SQL aggregation, or
(c) compute the local date once at load time and index it. Going with **(c)**: the load
step computes `occurred_local_date` and `local_week_start` in C# via `TimeZoneInfo`
(IANA-aware on .NET 6+), and every aggregate groups on those columns. Aggregation stays
in SQL, the DST edge is handled by a real tz database, and the query stays sargable.
Trade-off: denormalized, and would need a backfill if an account's timezone changed. For
this dataset, correct and cheap.

**"Now" is not the system clock.** Data ends 2026-07-27; today is later. The default week
is derived from `MAX(occurred_at)`, not `GETUTCDATE()`. Anchoring to the real clock would
render an empty dashboard — the kind of thing that looks like a broken build.

**Seeding must be idempotent.** The provided `seed.sql` is ~12.6k bare `INSERT`s with
explicit ids; running it twice violates the primary key. It gets wrapped so re-running
migrations is safe (guard on a marker row / `NOT EXISTS` per batch). The raw file stays
unmodified in the repo — the brief says treat it as production data.

---

## 4. Shape of the build

**DB — SQL Server 2022 in Docker.** It is what the team runs. `docker compose up` plus a
connection string; no manual setup steps in the README beyond that.

**Backend — .NET 8 minimal API.**
- **EF Core for migrations and the accounts read** — standard tooling, and the brief asks
  for the stack's normal migration story.
- **Dapper for the analytics query** — the pulse is a windowed aggregate with gap filling.
  Expressing that through LINQ would be fighting the ORM to produce SQL I would rather
  write directly and be able to read. Documented in the README as a deliberate split.

One endpoint does the real work:

```
GET /api/accounts/{accountId}/weekly-pulse
      ?weekStart=2026-07-20        # default: last complete week in the data
      &baselineWeeks=8             # 4 | 8 | 12
      &eventType=all               # all | call_received | lead_created | appointment_set
```

Returns the account roll-up, the per-location rows sorted by absolute deviation, the
resolved window boundaries, and a `dataQuality` block (duplicates excluded, whether the
account has any data at all). Supporting: `GET /api/accounts` for the selector.

The aggregation is a single query: dedup → local-date bucket → weekly totals per location
across the current + N baseline weeks → gap-fill → median/MAD per location. Median and MAD
in T-SQL via `PERCENTILE_CONT`.

**Frontend — Angular 18 standalone + signals.** One route, one view: account selector,
week selector, baseline-window selector, event-type filter. **All four live in the URL
query string**, so state survives reload and the view is shareable — a customer admin
pasting a link to their account manager is the actual use case, which `localStorage` would
not serve. No NgRx; four params and one HTTP call do not need a store, and I would rather
spend the budget on the aggregate being right.

Presentation is deliberately plain: a status line for the account, a table for locations
with current / typical band / delta / status. Function over form, per the brief.

---

## 5. Tests

Aiming at the parts that can be wrong in ways that matter, using the profiled numbers as
golden values.

**Unit — the baseline calculator** (no DB):
- burst immunity: a series with one 74× day yields the same median baseline as without it
- gap filling: missing days count as 0, not as absent
- `baseline = 0` → no percentage, status is not "down 100%"
- fewer than 4 prior weeks → `insufficient_history`
- `MAD = 0` → falls back to the sqrt spread, does not divide by zero
- low volume → status suppressed
- band arithmetic at the boundary (current exactly on the edge)

**Integration — against the seeded DB**, asserting numbers verified in `analysis/`:
- total rows 12,626; accounts 20
- account 6 on 2026-06-03 = 805 events
- account 20 returns an empty-state payload, not a 500 and not a NaN
- dedup removes exactly 12 rows
- default week resolves to 2026-07-20..26
- a known location's weekly total matches a hand-computed value

**Frontend**: one test that query params round-trip through a reload.

Not chasing coverage. If something has to give, the integration aggregates stay.

---

## 6. Open questions

Not sending these to the recruiter — the assumptions are defensible and waiting would
cost more than being wrong here would. Recording both halves per the brief.

1. **Is "normal" self-relative or peer-relative?** → Assuming self-relative. The customer
   phrasing is "normal *for us*", and 20 accounts is not a benchmark set.
2. **Last complete week, or week-to-date?** → Last complete week. Matches the
   Monday-morning framing; week-to-date needs a partial-week correction that adds error
   for no clear gain.
3. **Are exact duplicate events real, or ingestion artifacts?** → Assuming artifacts and
   deduplicating, but reporting the count so the assumption is visible in the response.
4. **Should a spike like account 6's be excluded from history, or is it real?** → Leaving
   it in and letting the robust statistic absorb it. Deleting real activity to make a
   metric behave is the worse failure.
5. **Is "activity" all three event types together?** → Yes by default, with a filter.
   Calls, leads and appointments are different funnel stages and a customer may well want
   them separately, but summing them answers "is this normal" at a glance.
6. **What does a brand-new account see?** → `insufficient_history` until 4 complete weeks
   exist. Not exercised by this dataset (all accounts predate the range) but cheap to get
   right.

---

## 7. Budget

Target 4–6 hours. Already spent ~1h15 on brief, profiling and this plan.

| | |
|---|---|
| SQL Server up, EF Core migrations, idempotent seed load with local-date computation | ~1h15 |
| Aggregation query + endpoint | ~1h15 |
| Angular view with URL-persisted state | ~1h |
| Tests | ~45m |
| README | ~30m |

**If I run out of time**, the order I drop things: event-type filter first, then the
baseline-window selector (hardcode 8), then the account-level roll-up (locations are the
differentiated half). The aggregate being correct and the tests proving it are the last
things to go, not the first.

**Risks.** SQL Server container startup on Windows eating more than its slot — mitigation
is a `docker compose` healthcheck and, failing that, swapping the provider (EF Core makes
this cheap, and the README would say so). Median in T-SQL via `PERCENTILE_CONT` needs
care to avoid a per-row window scan; if it fights back, computing median in C# over an
already-aggregated weekly series is a fine fallback, since that set is small (locations ×
weeks, at most ~200 rows).
