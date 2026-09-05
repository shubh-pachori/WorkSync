#!/usr/bin/env bash
#
# Loads local development secrets into `dotnet user-secrets` for both services.
#
# Secrets are no longer committed to appsettings.json. This script is the one-command
# replacement: run it once after cloning, then `dotnet run` each service as usual.
#
#   ./scripts/set-dev-secrets.sh
#
# Values can be overridden from the environment, e.g.
#   POSTGRES_PASSWORD='...' JWT_KEY='...' ./scripts/set-dev-secrets.sh
#
set -euo pipefail

cd "$(dirname "$0")/.."

POSTGRES_USER="${POSTGRES_USER:-aitimesheet}"
POSTGRES_PASSWORD="${POSTGRES_PASSWORD:-}"
POSTGRES_HOST="${POSTGRES_HOST:-localhost}"
POSTGRES_PORT="${POSTGRES_PORT:-5432}"

# Generate strong local-only secrets when none were supplied.
random_secret() { openssl rand -base64 36 | tr -d '\n/+=' | cut -c1-44; }

if [[ -z "$POSTGRES_PASSWORD" ]]; then
  if [[ -f .env ]]; then
    POSTGRES_PASSWORD="$(grep -E '^POSTGRES_PASSWORD=' .env | cut -d= -f2- || true)"
  fi
fi

if [[ -z "$POSTGRES_PASSWORD" ]]; then
  echo "POSTGRES_PASSWORD is not set and no .env file was found."
  echo "Copy .env.example to .env (and edit it), or export POSTGRES_PASSWORD, then re-run."
  exit 1
fi

JWT_KEY="${JWT_KEY:-$(random_secret)}"
INTERNAL_API_KEY="${INTERNAL_API_KEY:-$(random_secret)}"

IDENTITY_DB="Host=${POSTGRES_HOST};Port=${POSTGRES_PORT};Database=ai_timesheet_identity_db;Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}"
TIMESHEET_DB="Host=${POSTGRES_HOST};Port=${POSTGRES_PORT};Database=ai_timesheet_timesheet_db;Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}"

IDENTITY=backend/AITimesheet.IdentityService
TIMESHEET=backend/AITimesheet.TimesheetService

echo "Setting identity service secrets…"
dotnet user-secrets --project "$IDENTITY" set "ConnectionStrings:DefaultConnection" "$IDENTITY_DB" >/dev/null
dotnet user-secrets --project "$IDENTITY" set "Jwt:Key" "$JWT_KEY" >/dev/null
dotnet user-secrets --project "$IDENTITY" set "Internal:ApiKey" "$INTERNAL_API_KEY" >/dev/null

echo "Setting timesheet service secrets…"
# The JWT key and internal API key MUST match across both services.
dotnet user-secrets --project "$TIMESHEET" set "ConnectionStrings:DefaultConnection" "$TIMESHEET_DB" >/dev/null
dotnet user-secrets --project "$TIMESHEET" set "Jwt:Key" "$JWT_KEY" >/dev/null
dotnet user-secrets --project "$TIMESHEET" set "Internal:ApiKey" "$INTERNAL_API_KEY" >/dev/null

echo
echo "Done. Secrets are stored outside the repository, under:"
echo "  ~/.microsoft/usersecrets/  (Linux/macOS)"
echo "  %APPDATA%\\Microsoft\\UserSecrets\\  (Windows)"
echo
echo "To use Azure OpenAI instead of the built-in fallback generator:"
echo "  dotnet user-secrets --project $TIMESHEET set \"AzureOpenAI:Endpoint\" \"https://<resource>.openai.azure.com\""
echo "  dotnet user-secrets --project $TIMESHEET set \"AzureOpenAI:ApiKey\" \"<key>\""
