-- Read-only checks. No tables, roles, extensions or Auth settings are changed.
SELECT current_database() AS database_name,
       current_user AS database_user,
       current_timestamp AS checked_at;

-- Shows TLS status for this session when run using a direct PostgreSQL client.
SELECT ssl, version AS tls_version, cipher
FROM pg_stat_ssl
WHERE pid = pg_backend_pid();
