"""
Which guard branches does the real seed data actually exercise, per (account_id, location)?

The earlier profiling measured history and zero-days per ACCOUNT, but PLAN.md defines
insufficient_history / low_volume / baseline-zero / MAD-zero at LOCATION level. Any branch
no real location triggers has to be tested with synthetic fixtures instead, and it is worth
knowing that before writing the tests.

Applies the plan's rules as specified: exact-duplicate rows removed, occurred_at bucketed
into the ACCOUNT-LOCAL calendar date, weeks are Mon-Sun local.
"""
import sqlite3, statistics
from collections import defaultdict
from datetime import date, datetime, timedelta, timezone
from zoneinfo import ZoneInfo

REPO = r"c:\repos\personal\qualitara\home-challenge"
BASELINE_WEEKS = 8          # PLAN.md default
MIN_HISTORY_WEEKS = 4       # below this -> insufficient_history
LOW_VOLUME_THRESHOLD = 5    # baseline median < this -> low_volume

con = sqlite3.connect(":memory:")
con.executescript(open(f"{REPO}/schema.sql", encoding="utf-8").read())
con.executescript(open(f"{REPO}/seed.sql", encoding="utf-8").read())
q = lambda sql, *a: con.execute(sql, a).fetchall()

tzs = dict(q("SELECT id, timezone FROM accounts"))
names = dict(q("SELECT id, name FROM accounts"))


def h(t): print(f"\n{'=' * 82}\n{t}\n{'=' * 82}")


# --- dedup exactly as PLAN.md specifies: keep MIN(id) per full value tuple -------------
rows = q("""SELECT MIN(id), account_id, location, occurred_at
            FROM activity_events
            GROUP BY account_id, location, event_type, occurred_at,
                     COALESCE(duration_seconds, -1), COALESCE(outcome, '~')""")
print(f"rows after dedup: {len(rows)}  (removed {q('SELECT COUNT(*) FROM activity_events')[0][0] - len(rows)})")

# --- bucket into account-local Mon-Sun weeks ------------------------------------------
weekly = defaultdict(lambda: defaultdict(int))   # (acct, loc) -> week_start -> count
first_week = {}
for _id, acct, loc, ts in rows:
    dt = datetime.strptime(ts, "%Y-%m-%d %H:%M:%S").replace(tzinfo=timezone.utc)
    d = dt.astimezone(ZoneInfo(tzs[acct])).date()
    wk = d - timedelta(days=d.weekday())
    key = (acct, loc)
    weekly[key][wk] += 1
    if key not in first_week or wk < first_week[key]:
        first_week[key] = wk

# every (account, location) that exists in accounts but has no events at all
all_keys = sorted(weekly.keys())
accounts_with_no_events = [a for a in names if not any(k[0] == a for k in all_keys)]

CURRENT_WEEK = date(2026, 7, 20)     # last complete Mon-Sun week in the data
BASE_START = CURRENT_WEEK - timedelta(weeks=BASELINE_WEEKS)


def mad(xs, med):
    return statistics.median([abs(x - med) for x in xs])


def evaluate(key, current_week):
    """Apply the plan's guards for one location at one current-week anchor."""
    series = weekly[key]
    base_weeks = [current_week - timedelta(weeks=n) for n in range(BASELINE_WEEKS, 0, -1)]
    totals = [series.get(w, 0) for w in base_weeks]          # gap-filled with zero
    history = (current_week - first_week[key]).days // 7      # complete weeks before current
    med = statistics.median(totals)
    return {
        "history_weeks": history,
        "totals": totals,
        "median": med,
        "mad": mad(totals, med),
        "current": series.get(current_week, 0),
        "insufficient_history": history < MIN_HISTORY_WEEKS,
        "baseline_zero": med == 0,
        "low_volume": med < LOW_VOLUME_THRESHOLD,
        "mad_zero": mad(totals, med) == 0,
    }


h(f"A. DEFAULT WINDOW — current week {CURRENT_WEEK}, baseline {BASE_START}..{CURRENT_WEEK - timedelta(1)}")
res = {k: evaluate(k, CURRENT_WEEK) for k in all_keys}
print(f"   (account, location) pairs with any events : {len(all_keys)}")
print(f"   accounts with zero events entirely        : {accounts_with_no_events} "
      f"-> {[names[a] for a in accounts_with_no_events]}")

branches = ["insufficient_history", "baseline_zero", "low_volume", "mad_zero"]
print(f"\n   {'branch':<24} {'locations firing':<18} share")
for b in branches:
    n = sum(1 for r in res.values() if r[b])
    print(f"   {b:<24} {n:<18} {n / len(all_keys):.0%}")

h("B. WHICH LOCATIONS, CONCRETELY")
for b in branches:
    hits = [(k, r) for k, r in res.items() if r[b]]
    print(f"\n   -- {b}  ({len(hits)}) --")
    if not hits:
        print("      NONE — no real location in the seed triggers this branch.")
        continue
    for k, r in sorted(hits, key=lambda x: x[1]["median"])[:12]:
        print(f"      acct {k[0]:<3} {k[1]:<8} median={r['median']:<5.1f} mad={r['mad']:<5.1f} "
              f"current={r['current']:<4} history={r['history_weeks']}w  baseline={r['totals']}")
    if len(hits) > 12:
        print(f"      ... and {len(hits) - 12} more")

h("C. DOES ANY BRANCH EVER FIRE, AT ANY *VALID* ANCHOR WEEK?")
# An anchor is only valid if its whole 8-week baseline sits inside the data range.
# Sweeping earlier anchors makes every branch fire trivially, because the baseline
# window falls off the start of the dataset and every total reads zero. That is an
# artifact of the sweep, not a property of the data.
DATA_FIRST_WEEK = min(first_week.values())
FIRST_VALID_ANCHOR = DATA_FIRST_WEEK + timedelta(weeks=BASELINE_WEEKS)
anchors = [w for w in (FIRST_VALID_ANCHOR + timedelta(weeks=n) for n in range(60))
           if w <= CURRENT_WEEK]
print(f"   data starts week {DATA_FIRST_WEEK}; first anchor with a fully in-range "
      f"baseline is {FIRST_VALID_ANCHOR}")
print(f"   swept {len(anchors)} valid anchor weeks x {len(all_keys)} locations "
      f"= {len(anchors) * len(all_keys)} evaluations\n")
ever = {b: set() for b in branches}
for wk in anchors:
    for k in all_keys:
        r = evaluate(k, wk)
        for b in branches:
            if r[b]:
                ever[b].add(k)
for b in branches:
    n = len(ever[b])
    verdict = "real data covers it" if n else "*** NEVER FIRES -> synthetic fixture required ***"
    print(f"   {b:<24} {n:>3} of {len(all_keys)} locations   {verdict}")

h("D. DISTRIBUTION OF BASELINE MEDIANS (default window) — how close to the thresholds?")
meds = sorted(r["median"] for r in res.values())
print(f"   min={meds[0]}  p25={meds[len(meds)//4]}  median={statistics.median(meds)}  "
      f"p75={meds[3*len(meds)//4]}  max={meds[-1]}")
buckets = defaultdict(int)
for m in meds:
    buckets["0" if m == 0 else "1-4 (low_volume)" if m < 5 else "5-9" if m < 10 else "10+"] += 1
for b in ["0", "1-4 (low_volume)", "5-9", "10+"]:
    print(f"   baseline median {b:<18} {buckets[b]:>3} locations")

h("E. HISTORY AVAILABLE PER LOCATION (default window)")
hist = sorted(r["history_weeks"] for r in res.values())
print(f"   min={hist[0]}w  max={hist[-1]}w   -> every location has at least {hist[0]} complete "
      f"weeks before {CURRENT_WEEK}")
print(f"   locations with < {MIN_HISTORY_WEEKS}w history: "
      f"{sum(1 for x in hist if x < MIN_HISTORY_WEEKS)}")
