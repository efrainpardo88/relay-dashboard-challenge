"""
Profile the Relay seed data against the anomalies declared in seed/generate_seed.py.

Throwaway analysis script (scratchpad, not committed). Loads schema.sql + seed.sql
into an in-memory SQLite DB and verifies each claim against the actual rows, so the
plan rests on measured facts rather than on reading the generator.
"""
import sqlite3
from collections import defaultdict
from datetime import datetime, timezone
from zoneinfo import ZoneInfo

REPO = r"c:\repos\personal\qualitara\home-challenge"

con = sqlite3.connect(":memory:")
con.executescript(open(f"{REPO}/schema.sql", encoding="utf-8").read())
con.executescript(open(f"{REPO}/seed.sql", encoding="utf-8").read())
q = lambda sql, *a: con.execute(sql, a).fetchall()


def h(title):
    print(f"\n{'=' * 78}\n{title}\n{'=' * 78}")


h("1. SHAPE")
print("accounts        :", q("SELECT COUNT(*) FROM accounts")[0][0])
print("activity_events :", q("SELECT COUNT(*) FROM activity_events")[0][0])
lo, hi = q("SELECT MIN(occurred_at), MAX(occurred_at) FROM activity_events")[0]
print("occurred_at UTC :", lo, "->", hi)
print("distinct days   :", q("SELECT COUNT(DISTINCT date(occurred_at)) FROM activity_events")[0][0])
print("id gaps         :", q("SELECT MIN(id), MAX(id), COUNT(*) FROM activity_events")[0])

h("2. ACCOUNTS WITH NO EVENTS  (generator claims: id 20, rate 0.0)")
for r in q("""SELECT a.id, a.name, a.timezone, COUNT(e.id) AS n
              FROM accounts a LEFT JOIN activity_events e ON e.account_id = a.id
              GROUP BY a.id HAVING n = 0"""):
    print("  ", r)

h("3. EXACT DUPLICATES  (generator claims: 12, same values / new id)")
dups = q("""SELECT account_id, location, event_type, occurred_at,
                   COALESCE(duration_seconds,-1), COALESCE(outcome,'~'), COUNT(*) AS n
            FROM activity_events
            GROUP BY 1,2,3,4,5,6 HAVING n > 1 ORDER BY n DESC""")
print("  duplicate groups :", len(dups))
print("  extra rows       :", sum(n - 1 for *_, n in dups))
for d in dups[:5]:
    print("   ", d)

h("4. BURST DAY  (generator claims: account 6, 2026-06-03, +800 events)")
for r in q("""SELECT account_id, date(occurred_at) d, COUNT(*) n FROM activity_events
              GROUP BY 1,2 ORDER BY n DESC LIMIT 5"""):
    print("   acct %-3s %s  n=%s" % r)
b = q("""SELECT COUNT(*) FROM activity_events
         WHERE account_id=6 AND date(occurred_at)='2026-06-03'""")[0][0]
typ = q("""SELECT AVG(n) FROM (SELECT COUNT(*) n FROM activity_events
           WHERE account_id=6 AND date(occurred_at)<>'2026-06-03'
           GROUP BY date(occurred_at))""")[0][0]
print(f"   acct 6 burst day = {b}   |  acct 6 typical day = {typ:.1f}   |  ratio = {b/typ:.1f}x")

h("5. NULLS  (generator claims: 4% duration on calls, 3% outcome)")
for r in q("""SELECT event_type, COUNT(*) n,
                     SUM(CASE WHEN duration_seconds IS NULL THEN 1 ELSE 0 END) null_dur,
                     SUM(CASE WHEN outcome IS NULL THEN 1 ELSE 0 END) null_out
              FROM activity_events GROUP BY 1"""):
    et, n, nd, no = r
    print(f"   {et:<16} n={n:<6} null_duration={nd:<5} ({nd/n:5.1%})  null_outcome={no:<4} ({no/n:5.1%})")
print("   duration range  :", q("SELECT MIN(duration_seconds), MAX(duration_seconds) FROM activity_events")[0])
print("   outcomes by type:")
for r in q("SELECT event_type, outcome, COUNT(*) FROM activity_events GROUP BY 1,2 ORDER BY 1,3 DESC"):
    print("     ", r)

h("6. DAY-OF-WEEK SEASONALITY  (generator claims: weekends x0.35)")
for r in q("""SELECT strftime('%w', occurred_at) dow, COUNT(*) n,
                     COUNT(*)*1.0/COUNT(DISTINCT date(occurred_at)) per_day
              FROM activity_events GROUP BY 1 ORDER BY 1"""):
    names = "Sun Mon Tue Wed Thu Fri Sat".split()
    print(f"   {names[int(r[0])]}  total={r[1]:<6} avg/day={r[2]:.1f}")

h("7. TIMEZONE IMPACT — events whose CALENDAR DAY changes under account-local time")
tzs = dict(q("SELECT id, timezone FROM accounts"))
rows = q("SELECT account_id, occurred_at FROM activity_events")
shifted = defaultdict(int)
total = defaultdict(int)
for acct, ts in rows:
    dt = datetime.strptime(ts, "%Y-%m-%d %H:%M:%S").replace(tzinfo=timezone.utc)
    local = dt.astimezone(ZoneInfo(tzs[acct]))
    total[tzs[acct]] += 1
    if local.date() != dt.date():
        shifted[tzs[acct]] += 1
for tz in sorted(total, key=lambda t: -shifted[t] / total[t]):
    print(f"   {tz:<22} {shifted[tz]:>5} / {total[tz]:<6} shift day  ({shifted[tz]/total[tz]:6.2%})")
print(f"   TOTAL                  {sum(shifted.values())} / {sum(total.values())} "
      f"({sum(shifted.values())/sum(total.values()):.2%}) events land on a different local day")

h("8. PER-ACCOUNT PROFILE  (volume, locations, noise)")
print(f"   {'id':<3} {'name':<26} {'tz':<20} {'loc':<4} {'events':<7} {'/day':<7} {'cv':<6} zero-days")
for aid, name, tz in q("SELECT id, name, timezone FROM accounts ORDER BY id"):
    daily = [n for (n,) in q("""SELECT COUNT(*) FROM activity_events
                                WHERE account_id=? GROUP BY date(occurred_at)""", aid)]
    nloc = q("SELECT COUNT(DISTINCT location) FROM activity_events WHERE account_id=?", aid)[0][0]
    n = sum(daily)
    if not daily:
        print(f"   {aid:<3} {name:<26} {tz:<20} {nloc:<4} {'0':<7} {'-':<7} {'-':<6} -")
        continue
    span = 177  # 2026-02-01 .. 2026-07-27 inclusive
    mean = n / span
    var = sum((d - mean) ** 2 for d in daily) / len(daily)
    cv = (var ** 0.5) / mean if mean else 0
    print(f"   {aid:<3} {name:<26} {tz:<20} {nloc:<4} {n:<7} {mean:<7.2f} {cv:<6.2f} {span-len(daily)}")

h("9. LOCATION NAMES — are they unique per account?")
print("   distinct location strings overall:", q("SELECT COUNT(DISTINCT location) FROM activity_events")[0][0])
for r in q("""SELECT location, COUNT(DISTINCT account_id) na FROM activity_events
              GROUP BY 1 ORDER BY na DESC LIMIT 3"""):
    print(f"   '{r[0]}' appears in {r[1]} different accounts")

h("10. INTEGRITY ODDITIES")
print("   events before account created_at :",
      q("""SELECT COUNT(*) FROM activity_events e JOIN accounts a ON a.id=e.account_id
           WHERE e.occurred_at < a.created_at""")[0][0])
print("   orphan account_id                :",
      q("""SELECT COUNT(*) FROM activity_events e
           LEFT JOIN accounts a ON a.id=e.account_id WHERE a.id IS NULL""")[0][0])
print("   non-null duration on non-calls   :",
      q("""SELECT COUNT(*) FROM activity_events
           WHERE event_type<>'call_received' AND duration_seconds IS NOT NULL""")[0][0])
print("   negative/zero durations          :",
      q("SELECT COUNT(*) FROM activity_events WHERE duration_seconds <= 0")[0][0])
