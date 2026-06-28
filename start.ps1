# One-command local run (research §5 / quickstart.md).
#   1. Start the pinned Postgres container (reads .env for credentials).
#   2. Run the ASP.NET Core host, which applies EF migrations at startup and
#      (in Development) auto-starts the Vite dev server via SpaProxy.
#
# Prereqs: Docker Desktop running; .env present (copy from .env.example);
# connection string set in user-secrets (see quickstart.md "First-time setup").
$ErrorActionPreference = 'Stop'
$repo = $PSScriptRoot

if (-not (Test-Path (Join-Path $repo '.env'))) {
    Write-Warning "No .env found. Copy .env.example to .env and set POSTGRES_PASSWORD first."
}

Write-Host "Starting PostgreSQL (docker compose up -d db)..." -ForegroundColor Cyan
docker compose --project-directory $repo up -d db

Write-Host "Launching the ASP.NET Core host (Ctrl+C to stop)..." -ForegroundColor Cyan
dotnet run --project (Join-Path $repo 'backend/src/Web')
