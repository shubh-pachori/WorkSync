#!/bin/bash
# Creates the two service databases on first container start.
# Each service then applies its own EF Core migrations on boot.
set -e

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-EOSQL
    CREATE DATABASE ai_timesheet_identity_db;
    CREATE DATABASE ai_timesheet_timesheet_db;
EOSQL
