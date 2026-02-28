-- PostgreSQL initialization script
-- Runs once on first container start (when the data volume is empty).
-- The database and user already exist (created by POSTGRES_* env vars).
-- This script sets up permissions and extensions only.

-- Required extensions
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
CREATE EXTENSION IF NOT EXISTS "pg_trgm";  -- for trigram text search

-- Grant privileges to the app user
-- (POSTGRES_USER already owns the db; app user needs DML rights after migrations run)
GRANT ALL PRIVILEGES ON DATABASE servercat TO servercat_app;

-- Set search path for the app user
ALTER ROLE servercat_app SET search_path = public;

\echo 'PostgreSQL init complete.'
