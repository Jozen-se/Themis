#!/bin/sh
# Runs the Themis database bootstrap on the first start of the compose PostgreSQL (RR-59, RR-65). The postgres image
# runs this directory's scripts only when the data volume is empty; docker compose down -v resets it.
#
# 1. db/bootstrap/01-roles.sql: roles, schemas and revokes, without passwords.
# 2. db/bootstrap/02-role-passwords.psql: the LOGIN passwords, taken by \getenv from THEMIS_MIGRATOR_PASSWORD,
#    THEMIS_APP_PASSWORD and THEMIS_REPORTING_PASSWORD, so they never appear on a command line. A missing variable
#    stops the bootstrap with a message naming it. compose.yaml sets development placeholders only.
set -eu

bootstrap=/themis-bootstrap

psql --no-psqlrc -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
    -f "$bootstrap/01-roles.sql"

psql --no-psqlrc -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
    -c '\getenv themis_migrator_password THEMIS_MIGRATOR_PASSWORD' \
    -c '\getenv themis_app_password THEMIS_APP_PASSWORD' \
    -c '\getenv themis_reporting_password THEMIS_REPORTING_PASSWORD' \
    -f "$bootstrap/02-role-passwords.psql"
