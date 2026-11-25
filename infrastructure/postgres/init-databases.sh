#!/usr/bin/env bash
set -euo pipefail

psql \
  --set=ON_ERROR_STOP=1 \
  --username "$POSTGRES_USER" \
  --dbname "$POSTGRES_DB" \
  --set=identity_password="$IDENTITY_DB_PASSWORD" \
  --set=capacity_password="$CAPACITY_DB_PASSWORD" \
  --set=audit_password="$AUDIT_DB_PASSWORD" <<'SQL'
SELECT format('CREATE ROLE identity_app LOGIN PASSWORD %L', :'identity_password') \gexec
SELECT format('CREATE ROLE capacity_app LOGIN PASSWORD %L', :'capacity_password') \gexec
SELECT format('CREATE ROLE audit_app LOGIN PASSWORD %L', :'audit_password') \gexec

SELECT format('CREATE DATABASE identity_db OWNER identity_app ENCODING %L', 'UTF8') \gexec
SELECT format('CREATE DATABASE capacity_db OWNER capacity_app ENCODING %L', 'UTF8') \gexec
SELECT format('CREATE DATABASE audit_db OWNER audit_app ENCODING %L', 'UTF8') \gexec

REVOKE CONNECT ON DATABASE postgres FROM PUBLIC;
REVOKE CONNECT ON DATABASE identity_db FROM PUBLIC;
REVOKE CONNECT ON DATABASE capacity_db FROM PUBLIC;
REVOKE CONNECT ON DATABASE audit_db FROM PUBLIC;

GRANT CONNECT ON DATABASE identity_db TO identity_app;
GRANT CONNECT ON DATABASE capacity_db TO capacity_app;
GRANT CONNECT ON DATABASE audit_db TO audit_app;
SQL
