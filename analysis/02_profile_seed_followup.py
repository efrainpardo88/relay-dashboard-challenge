"""Follow-up profiling: fixes the CV bug and tests two design hypotheses."""
import sqlite3, statistics
from datetime import date, timedelta

REPO = r"c:\repos\personal\qualitara\home-challenge"
con = sqlite3.connect(":memory:")
con.executescript(open(f"{REPO}/schema.sql", encoding="utf-8").read())
con.executescript(open(f"{REPO}/seed.sql", encoding="utf-8").read())
q = lambda sql, *a: con.execute(sql, a).fetchall()

DAY0, DAYN = date(2026, 2, 1), date(2026, 7, 27)
ALL_DAYS = [DAY0 + timedelta(d) for d in range((DAYN - DAY0).days + 1)]
DOW = "Mon Tue Wed Thu Fri Sat Sun".split()


def h(t): print(f"\n{'='*78}\n{t}\n{'='*78}")


h("A. CALENDAR ANCHORS  (does the dataset end on a Monday?)")
print(f"   first day  {DAY0}  = {DOW[DAY0.weekday()]}")
print(f"   last  day  {DAYN}  = {DOW[DAYN.weekday()]}")
print(f"   burst day  2026-06-03 = {DOW[date(2026,6,3).weekday()]}")
last_mon = DAYN - timedelta(days=DAYN.weekday())
print(f"   -> last COMPLETE Mon-Sun week: {last_mon - timedelta(7)} .. {last_mon - timedelta(1)}")

h("B. CV, COMPUTED HONESTLY  (zero-days included in BOTH mean and variance)")
print(f"   {'id':<3} {'name':<26} {'n':<6} {'mean':<6} {'median':<7} {'stdev':<7} {'CV':<6} {'p95':<5} zero-days")
for aid, name in q("SELECT id, name FROM accounts ORDER BY id"):
    counts = dict(q("""SELECT date(occurred_at), COUNT(*) FROM activity_events
                       WHERE account_id=? GROUP BY 1""", aid))
    series = [counts.get(d.isoformat(), 0) for d in ALL_DAYS]   # gaps filled with 0
    n = sum(series)
    if n == 0:
        print(f"   {aid:<3} {name:<26} {'0':<6} {'-':<6} {'-':<7} {'-':<7} {'-':<6} {'-':<5} {len(series)}")
        continue
    mean, med, sd = statistics.mean(series), statistics.median(series), statistics.pstdev(series)
    p95 = sorted(series)[int(len(series) * 0.95)]
    print(f"   {aid:<3} {name:<26} {n:<6} {mean:<6.2f} {med:<7.1f} {sd:<7.2f} "
          f"{sd/mean:<6.2f} {p95:<5} {series.count(0)}")

h("C. HOW BADLY DOES THE BURST POISON A MEAN-BASED BASELINE?")
for label, where in [("all data", ""), ("burst day excluded", "AND date(occurred_at)<>'2026-06-03'")]:
    counts = dict(q(f"""SELECT date(occurred_at), COUNT(*) FROM activity_events
                        WHERE account_id=6 {where} GROUP BY 1"""))
    wed = [counts.get(d.isoformat(), 0) for d in ALL_DAYS if d.weekday() == 2]
    print(f"   acct 6 Wednesdays, {label:<20} mean={statistics.mean(wed):7.1f}  "
          f"median={statistics.median(wed):6.1f}  n_weeks={len(wed)}")
print("   -> the median is unmoved by the burst; the mean is not.")

h("D. THE PRODUCT SCENARIO: last complete week vs a trailing baseline, per location")
wk_start, wk_end = last_mon - timedelta(7), last_mon - timedelta(1)
base_start = wk_start - timedelta(28)          # 4 prior weeks
print(f"   current week  : {wk_start} .. {wk_end}")
print(f"   baseline      : {base_start} .. {wk_start - timedelta(1)}  (4 prior weeks, same weekdays)")
print(f"\n   {'acct':<5} {'location':<9} {'this wk':<8} {'baseline med':<13} {'delta':<9} note")
for aid in (6, 16, 20):
    locs = [l for (l,) in q("SELECT DISTINCT location FROM activity_events WHERE account_id=? ORDER BY 1", aid)]
    if not locs:
        print(f"   {aid:<5} {'(none)':<9} {'-':<8} {'-':<13} {'-':<9} account has zero events at all")
        continue
    for loc in locs[:4]:
        counts = dict(q("""SELECT date(occurred_at), COUNT(*) FROM activity_events
                           WHERE account_id=? AND location=? GROUP BY 1""", aid, loc))
        cur = sum(counts.get((wk_start + timedelta(i)).isoformat(), 0) for i in range(7))
        weeks = [sum(counts.get((base_start + timedelta(w * 7 + i)).isoformat(), 0) for i in range(7))
                 for w in range(4)]
        med = statistics.median(weeks)
        delta = "n/a" if med == 0 else f"{(cur - med) / med:+.0%}"
        note = "baseline is 0 -> % is undefined" if med == 0 else ""
        print(f"   {aid:<5} {loc:<9} {cur:<8} {med:<13.1f} {delta:<9} {note}")

h("E. DUPLICATES: do they land inside the window that matters?")
print("   dup rows by month:")
for r in q("""SELECT strftime('%Y-%m', occurred_at) m, COUNT(*)-COUNT(DISTINCT id) FROM (
                SELECT id, occurred_at, account_id, location, event_type,
                       COALESCE(duration_seconds,-1) d, COALESCE(outcome,'~') o
                FROM activity_events) GROUP BY m"""):
    pass  # placeholder, real calc below
dups = q("""SELECT account_id, location, event_type, occurred_at, COUNT(*) n
            FROM activity_events GROUP BY 1,2,3,4,
                 COALESCE(duration_seconds,-1), COALESCE(outcome,'~')
            HAVING n>1 ORDER BY occurred_at""")
for d in dups:
    print(f"     acct {d[0]:<3} {d[3]}  {d[2]}")
print(f"   total duplicate groups: {len(dups)}  (all are exact value-matches with distinct ids)")
