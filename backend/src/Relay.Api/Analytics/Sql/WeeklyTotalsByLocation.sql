-- Weekly event totals per location, for one account, across the current week and the
-- N baseline weeks immediately before it.
--
-- Contract with the C# caller (PLAN.md Amendment 2): SQL aggregates as far as weekly
-- totals and stops there. Median, MAD, the typical band and the status are computed in
-- C# over this result set, which is at most (locations x weeks) rows -- 15 x 13 for the
-- largest account in the seed.
--
-- Parameters:
--   @accountId        int   -- always scoped: 'Site A' exists in 19 different accounts
--   @currentWeekStart date  -- Monday of the week under review, account-local
--   @windowStart      date  -- Monday of the oldest baseline week (computed by the caller
--                              so this file stays a single composable statement)
--   @baselineWeeks    int   -- 4 | 8 | 12
--   @eventType        nvarchar(40) -- 'all', or one of the three event types
--
-- Three things this has to get right, each measured in analysis/:
--   1. DEDUPLICATION. 12 exact value-duplicates carry distinct ids. Counting both copies
--      inflates whichever baseline week they land in.
--   2. GAP FILLING. GROUP BY returns only weeks that have rows. A location that went
--      silent for a week must produce 0, not disappear -- a median taken over 5 of 8
--      weeks is wrong and looks perfectly fine.
--   3. LOCAL WEEKS. Buckets come from local_week_start, derived at load time in the
--      account's own timezone, never from occurred_at directly.

WITH week_offsets AS (
    -- Recursive rather than GENERATE_SERIES: that function needs compatibility level 160,
    -- and this stays runnable on older SQL Server. Depth is at most 12.
    SELECT 0 AS n
    UNION ALL
    SELECT n + 1 FROM week_offsets WHERE n < @baselineWeeks
),
weeks AS (
    SELECT CAST(DATEADD(WEEK, -n, @currentWeekStart) AS date) AS week_start
    FROM week_offsets
),
locations AS (
    -- Any location with activity somewhere in the window. One that has baseline volume but
    -- went quiet this week still appears -- that is precisely the location needing
    -- attention. One with no activity anywhere in the window drops off instead of reading
    -- as "-100%" forever.
    SELECT DISTINCT location
    FROM activity_events
    WHERE account_id = @accountId
      AND local_week_start BETWEEN @windowStart AND @currentWeekStart
),
spine AS (
    SELECT l.location, w.week_start
    FROM locations AS l
    CROSS JOIN weeks AS w
),
deduped AS (
    -- Keep the lowest id of each exact value tuple. GROUP BY treats NULLs as equal, which
    -- is what we want: duplicates with a NULL outcome or duration must collapse too.
    SELECT MIN(id) AS id
    FROM activity_events
    WHERE account_id = @accountId
      AND local_week_start BETWEEN @windowStart AND @currentWeekStart
    GROUP BY location, event_type, occurred_at, duration_seconds, outcome
),
totals AS (
    SELECT e.location,
           e.local_week_start AS week_start,
           COUNT_BIG(*) AS event_count
    FROM activity_events AS e
    INNER JOIN deduped AS d ON d.id = e.id
    WHERE @eventType = 'all' OR e.event_type = @eventType
    GROUP BY e.location, e.local_week_start
)
SELECT s.location                              AS Location,
       s.week_start                            AS WeekStart,
       CAST(ISNULL(t.event_count, 0) AS int)   AS EventCount
FROM spine AS s
LEFT JOIN totals AS t
       ON t.location   = s.location
      AND t.week_start = s.week_start
ORDER BY s.location, s.week_start;
