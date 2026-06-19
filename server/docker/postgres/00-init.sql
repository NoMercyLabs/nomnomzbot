-- Ensure the nomercybot role exists and the password matches POSTGRES_PASSWORD.
-- This runs only on first-time volume initialization (empty data dir).
-- For stale volumes with wrong credentials: docker compose down -v && docker compose up

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'nomercybot') THEN
        CREATE ROLE nomercybot WITH LOGIN PASSWORD 'nomercybot_dev';
    END IF;
END
$$;

GRANT ALL PRIVILEGES ON DATABASE nomercybot TO nomercybot;
