#!/usr/bin/env python3
"""
Deterministic seed data generator for the Relay take-home dataset.

Running this script always produces byte-identical output (seed.sql).
Candidates should load seed.sql — they do not need to run this script.

Usage:  python3 generate_seed.py > ../seed.sql
"""

import random
from datetime import datetime, timedelta

random.seed(247)  # deterministic — do not change

RANGE_START = datetime(2026, 2, 1, 0, 0, 0)   # UTC, inclusive
RANGE_END = datetime(2026, 7, 28, 0, 0, 0)    # UTC, exclusive

EVENT_TYPES = ["call_received", "lead_created", "appointment_set"]
EVENT_WEIGHTS = [0.62, 0.24, 0.14]
CALL_OUTCOMES = ["connected", "missed", "voicemail"]
LEAD_OUTCOMES = ["converted", "open"]
APPT_OUTCOMES = ["completed", "no_show"]

ACCOUNTS = [
    # (id, name, industry, timezone, n_locations, daily_event_rate)
    (1,  "Summit Auto Group",        "Automotive Services", "America/Chicago",     6,  9.0),
    (2,  "Harbor Dental Partners",   "Dental",              "America/New_York",    3,  5.5),
    (3,  "Bluebird HVAC",            "Home Services",       "America/Denver",      2,  4.0),
    (4,  "Cornerstone Vet Clinics",  "Veterinary",          "America/Chicago",     4,  6.0),
    (5,  "Pacific Smiles",           "Dental",              "America/Los_Angeles", 5,  7.0),
    (6,  "Metro Collision Centers",  "Automotive Services", "America/New_York",   15, 14.0),
    (7,  "Desert Springs Plumbing",  "Home Services",       "America/Phoenix",     2,  3.5),
    (8,  "Lakeside Physio",          "Healthcare",          "America/Chicago",     1,  2.5),
    (9,  "Ironwood Fitness",         "Fitness",             "America/Denver",      3,  4.5),
    (10, "Gulf Coast Roofing",       "Home Services",       "America/Chicago",     2,  3.0),
    (11, "Northgate Optical",        "Healthcare",          "America/New_York",    2,  3.0),
    (12, "Redline Tire & Service",   "Automotive Services", "America/Los_Angeles", 7, 10.0),
    (13, "Willow Creek Counseling",  "Healthcare",          "America/Chicago",     1,  2.0),
    (14, "Beacon Home Security",     "Home Services",       "America/New_York",    4,  5.0),
    (15, "Sierra Pest Solutions",    "Home Services",       "America/Phoenix",     3,  4.0),
    (16, "Old Town Barbers",         "Personal Care",       "America/Chicago",     1,  1.8),
    (17, "Evergreen Landscaping",    "Home Services",       "America/Los_Angeles", 2,  2.8),
    (18, "Capital City Storage",     "Storage",             "UTC",                 5,  4.5),
    (19, "Riverbend Chiropractic",   "Healthcare",          "America/New_York",    1,  2.2),
    (20, "Quiet Harbor Spa",         "Personal Care",       "America/Los_Angeles", 1,  0.0),  # zero events
]

BURST_ACCOUNT_ID = 6                     # Metro Collision Centers
BURST_DAY = datetime(2026, 6, 3)         # single huge day
BURST_EVENTS = 800

NULL_DURATION_RATE = 0.04                # planted NULLs on call durations
NULL_OUTCOME_RATE = 0.03                 # planted NULLs on outcomes
N_DUPLICATES = 12                        # planted exact duplicates (new id, same values)


def sql_str(s):
    return "'" + s.replace("'", "''") + "'"


def sql_ts(dt):
    return "'" + dt.strftime("%Y-%m-%d %H:%M:%S") + "'"


def pick_outcome(event_type):
    if random.random() < NULL_OUTCOME_RATE:
        return None
    if event_type == "call_received":
        return random.choices(CALL_OUTCOMES, weights=[0.62, 0.26, 0.12])[0]
    if event_type == "lead_created":
        return random.choices(LEAD_OUTCOMES, weights=[0.35, 0.65])[0]
    return random.choices(APPT_OUTCOMES, weights=[0.82, 0.18])[0]


def pick_duration(event_type):
    if event_type != "call_received":
        return None
    if random.random() < NULL_DURATION_RATE:
        return None
    return random.randint(20, 1500)


def business_hour_dt(day):
    # Cluster events into local-ish business hours expressed in UTC,
    # with a tail into evenings so day boundaries are not trivially clean.
    hour = min(23, max(0, int(random.gauss(16, 4))))
    return day.replace(hour=hour, minute=random.randint(0, 59), second=random.randint(0, 59))


def main():
    rows = []
    for (acct_id, _name, _ind, _tz, n_loc, rate) in ACCOUNTS:
        locations = [f"Site {chr(ord('A') + i)}" for i in range(n_loc)]
        day = RANGE_START
        while day < RANGE_END:
            n = 0
            if rate > 0:
                n = max(0, int(random.gauss(rate, rate * 0.45)))
                if day.weekday() >= 5:  # quieter weekends
                    n = int(n * 0.35)
            for _ in range(n):
                et = random.choices(EVENT_TYPES, weights=EVENT_WEIGHTS)[0]
                rows.append(
                    (
                        acct_id,
                        random.choice(locations),
                        et,
                        business_hour_dt(day),
                        pick_duration(et),
                        pick_outcome(et),
                    )
                )
            day += timedelta(days=1)

    # Planted: burst day for one account
    burst_locations = [f"Site {chr(ord('A') + i)}" for i in range(15)]
    for _ in range(BURST_EVENTS):
        et = random.choices(EVENT_TYPES, weights=EVENT_WEIGHTS)[0]
        rows.append(
            (
                BURST_ACCOUNT_ID,
                random.choice(burst_locations),
                et,
                business_hour_dt(BURST_DAY),
                pick_duration(et),
                pick_outcome(et),
            )
        )

    rows.sort(key=lambda r: (r[3], r[0]))

    # Planted: exact duplicates (same values, new id) sprinkled deterministically
    dup_sources = random.sample(range(len(rows)), N_DUPLICATES)
    duplicates = [rows[i] for i in dup_sources]

    all_rows = rows + duplicates
    all_rows.sort(key=lambda r: (r[3], r[0]))

    out = []
    out.append("-- Relay take-home seed data (deterministic; generated by seed/generate_seed.py)")
    out.append("-- All occurred_at values are UTC.")
    out.append("")
    out.append("-- accounts")
    for (acct_id, name, industry, tz, _n_loc, _rate) in ACCOUNTS:
        created = datetime(2025, random.randint(1, 12), random.randint(1, 28), 12, 0, 0)
        out.append(
            f"INSERT INTO accounts (id, name, industry, timezone, created_at) VALUES "
            f"({acct_id}, {sql_str(name)}, {sql_str(industry)}, {sql_str(tz)}, {sql_ts(created)});"
        )
    out.append("")
    out.append("-- activity_events")
    for i, (acct_id, loc, et, ts, dur, outc) in enumerate(all_rows, start=1):
        dur_s = "NULL" if dur is None else str(dur)
        outc_s = "NULL" if outc is None else sql_str(outc)
        out.append(
            f"INSERT INTO activity_events (id, account_id, location, event_type, occurred_at, duration_seconds, outcome) VALUES "
            f"({i}, {acct_id}, {sql_str(loc)}, {sql_str(et)}, {sql_ts(ts)}, {dur_s}, {outc_s});"
        )

    print("\n".join(out))


if __name__ == "__main__":
    main()
