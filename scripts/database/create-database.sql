-- Creates the database user and database for the Postgres adapter of WsLink.Api, the weather_data table
-- (timescaledb.sql in this folder) and grants the user only INSERT on it.
-- Run with psql as a superuser (the TimescaleDB extension can be created only by a superuser), e.g.
--   psql -h <host> -U postgres -d postgres -v password='<password of wslink>' -f scripts/database/create-database.sql
-- The password is passed as a psql variable, so it is not stored in this file. Safe to run again:
-- an existing user, database or table is kept (the password and owner of existing objects are not changed).
-- Not needed for the compose.yaml postgres service, it creates the user, database and table itself.

\set ON_ERROR_STOP on

-- An empty password (e.g. -v password="$UNSET_VARIABLE") would create a user that cannot log in,
-- and a later run would keep it. Raised as an error, so psql exits with a non-zero code (ON_ERROR_STOP).
\if :{?password}
\else
    \set password ''
\endif
SELECT trim(:'password') = '' AS password_missing \gset
\if :password_missing
    DO $$ BEGIN RAISE EXCEPTION 'Missing password, run with -v password=...'; END $$;
\endif

SELECT format('CREATE ROLE wslink LOGIN PASSWORD %L', :'password')
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'wslink') \gexec

-- Owned by the superuser running this script, not by wslink: the database owner also owns the public schema
-- (PostgreSQL 15+) and a schema owner can drop any table in it.
SELECT 'CREATE DATABASE wslink'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'wslink') \gexec

GRANT CONNECT ON DATABASE wslink TO wslink;

\connect wslink

-- Path relative to this file (same folder), so the script works from any working directory.
\ir timescaledb.sql

-- The service only inserts, so it gets no other privilege on the table or the schema.
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA public TO wslink;
GRANT INSERT ON weather_data TO wslink;
