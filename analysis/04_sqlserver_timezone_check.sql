SET NOCOUNT ON;
PRINT '--- 1. engine / platform ---';
SELECT @@VERSION AS v;

PRINT '--- 2. how many zones, and what do the names look like? ---';
SELECT COUNT(*) AS zone_count FROM sys.time_zone_info;
SELECT TOP 5 name FROM sys.time_zone_info ORDER BY name;

PRINT '--- 3. is an IANA name present in sys.time_zone_info? ---';
SELECT name, current_utc_offset, is_currently_dst
FROM sys.time_zone_info
WHERE name IN ('America/Chicago', 'Central Standard Time',
               'America/Phoenix', 'US Mountain Standard Time');

PRINT '--- 4. AT TIME ZONE with a WINDOWS id ---';
BEGIN TRY
    SELECT CAST('2026-06-03 18:00:00' AS datetime2) AT TIME ZONE 'UTC'
           AT TIME ZONE 'Central Standard Time' AS windows_id_result;
END TRY
BEGIN CATCH
    SELECT 'FAILED: ' + ERROR_MESSAGE() AS windows_id_result;
END CATCH

PRINT '--- 5. AT TIME ZONE with an IANA id (the claim under test) ---';
BEGIN TRY
    SELECT CAST('2026-06-03 18:00:00' AS datetime2) AT TIME ZONE 'UTC'
           AT TIME ZONE 'America/Chicago' AS iana_id_result;
END TRY
BEGIN CATCH
    SELECT 'FAILED: ' + ERROR_MESSAGE() AS iana_id_result;
END CATCH

PRINT '--- 6. DST correctness: does it shift across the 2026-03-08 transition? ---';
BEGIN TRY
    SELECT
        CAST('2026-02-15 18:00:00' AS datetime2) AT TIME ZONE 'UTC'
            AT TIME ZONE 'America/Chicago' AS winter_iana,
        CAST('2026-06-15 18:00:00' AS datetime2) AT TIME ZONE 'UTC'
            AT TIME ZONE 'America/Chicago' AS summer_iana;
END TRY
BEGIN CATCH
    SELECT 'FAILED: ' + ERROR_MESSAGE() AS dst_result;
END CATCH

PRINT '--- 7. Phoenix (no DST) via IANA ---';
BEGIN TRY
    SELECT
        CAST('2026-02-15 18:00:00' AS datetime2) AT TIME ZONE 'UTC'
            AT TIME ZONE 'America/Phoenix' AS phoenix_winter,
        CAST('2026-06-15 18:00:00' AS datetime2) AT TIME ZONE 'UTC'
            AT TIME ZONE 'America/Phoenix' AS phoenix_summer;
END TRY
BEGIN CATCH
    SELECT 'FAILED: ' + ERROR_MESSAGE() AS phoenix_result;
END CATCH

PRINT '--- 8. every IANA zone used by the seed accounts ---';
BEGIN TRY
    SELECT z.tz,
           CAST('2026-06-15 18:00:00' AS datetime2) AT TIME ZONE 'UTC' AT TIME ZONE z.tz AS converted
    FROM (VALUES ('America/Chicago'), ('America/New_York'), ('America/Denver'),
                 ('America/Los_Angeles'), ('America/Phoenix'), ('UTC')) AS z(tz);
END TRY
BEGIN CATCH
    SELECT 'FAILED: ' + ERROR_MESSAGE() AS all_zones_result;
END CATCH
