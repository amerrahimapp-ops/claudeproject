# Runbook

Practical, repo-specific operations reference for Project Alpha (Capacity
Request Management System). Covers deploy, health checks, backup/restore,
and rollback. Written against what's actually in this repo as of Phase 6 —
not generic boilerplate.

There is no production hosting target decided yet (see "Pending business
decisions" in `CLAUDE.md` — pilot team, go-live date, real AD mapping are
all still open). This runbook assumes a single-VM/single-host deployment
(one MySQL instance, one API process, one static web host) since nothing
more elaborate exists in this repo yet — no Kubernetes manifests, no
multi-instance orchestration, no external secrets manager integration.
Update this doc when that changes.

## Components

| Component | What it is | Where it lives |
|---|---|---|
| API | .NET 9 Web API (modular monolith) | `api/src/Api` |
| Data/EF Core | MySQL access, migrations | `api/src/Api.Data` |
| Web | React + Vite + TS + AntD5 SPA | `web/` |
| Database | MySQL 8.0 | `docker-compose.yml` (local dev) |

---

## Deploy

### 1. Database (MySQL via docker-compose)

Local dev / single-host deploys both use the same `docker-compose.yml` at
the repo root:

```bash
docker compose up -d mysql
```

This starts MySQL 8.0 with:
- Database: `capacity_dev`
- Root password: `dev_root_password` (dev only — see "Production
  configuration" below for anything beyond a local/demo deploy)
- Data persisted to the `mysql_data` named volume (survives container
  restarts/recreates; `docker compose down -v` destroys it — don't run
  that against a real deployment without a backup first, see below)

Confirm it's up before proceeding:

```bash
docker compose ps          # STATUS should be "healthy"
```

### 2. Apply EF Core migrations

Migrations live in `api/src/Api.Data/Migrations/`. Current set (oldest
first):

1. `InitialCreate`
2. `AddRequestConcurrencyVersion`
3. `AddAiEvaluations`
4. `AddOutboxMessages`

In Development, `Program.cs` applies pending migrations automatically on
API startup (`db.Database.MigrateAsync()`), so a plain `dotnet run` is
enough locally. For any deploy where you want migrations applied as an
explicit, auditable step (recommended for anything beyond a laptop),
apply them yourself instead of relying on that auto-migrate:

```bash
cd api
dotnet tool restore   # if dotnet-ef isn't already installed/restored
dotnet ef database update --project src/Api.Data --startup-project src/Api
```

`dotnet ef` needs a connection string. It reads `ConnectionStrings__CapacityDb`
from the environment the same way the app does at runtime; the design-time
factory (`Api.Data/CapacityDbContextFactory.cs`) falls back to the
docker-compose local dev connection string if nothing else is set, or
respects a `CAPACITY_DESIGNTIME_CONNECTION_STRING` override:

```bash
export CAPACITY_DESIGNTIME_CONNECTION_STRING="Server=<host>;Port=3306;Database=<db>;User=<user>;Password=<password>;"
```

### 3. API

Build + run directly:

```bash
cd api
dotnet build --configuration Release
ASPNETCORE_ENVIRONMENT=Production \
ConnectionStrings__CapacityDb="Server=<host>;Port=3306;Database=<db>;User=<user>;Password=<password>;" \
Jwt__SigningKey="<a real, randomly-generated key, at least 32 bytes>" \
dotnet run --project src/Api/Api.csproj --no-launch-profile
```

Or publish + run the published output (more typical for anything beyond a
throwaway demo):

```bash
cd api
dotnet publish src/Api/Api.csproj --configuration Release --output ./publish
ASPNETCORE_ENVIRONMENT=Production \
ConnectionStrings__CapacityDb="..." \
Jwt__SigningKey="..." \
dotnet ./publish/Api.dll
```

**Production configuration checklist** (see `AuthServiceCollectionExtensions.cs`
for the fail-fast checks that enforce these):
- `Auth:Provider` must NOT be `Mock` (or unset) outside `Development` — the
  app throws on startup if it is. There is no real `AdIdentityProvider` yet
  (deferred — see `CLAUDE.md` "Pending business decisions"), so there is
  currently no supported non-Development identity provider. Don't deploy
  this to a real environment until that's built and configured.
- `Jwt:SigningKey` must be set (no default) and at least 32 bytes (256
  bits) — the app throws on startup otherwise. Generate one with e.g.
  `openssl rand -base64 48` and store it via your platform's secrets
  mechanism, never in a committed appsettings file (see `CLAUDE.md`'s
  secrets convention).
- CORS: the dev-only CORS policy (`DevCorsPolicy` in `Program.cs`) only
  ever gets registered into the request pipeline inside
  `if (app.Environment.IsDevelopment())` — it does not run in Production.
  If the API and web app end up on different origins in a real deploy,
  you'll need to add a real CORS policy for that origin; nothing currently
  supports that.
- Rate limiting on `/api/v1/auth/login` (20 requests/minute per client IP,
  fixed window, see `AuthServiceCollectionExtensions.cs`) is in-memory and
  per-process — it resets on restart and does not coordinate across
  multiple instances. Fine for a single-instance deploy; revisit
  (distributed limiter, e.g. backed by Redis) before running more than one
  API instance behind a load balancer.

### 4. Web

Build a static bundle and serve it from any static file host (nginx,
a CDN, `dotnet`'s own `UseStaticFiles`, etc.):

```bash
cd web
npm ci
npm run build      # outputs to web/dist
```

Serve `web/dist` with any static file server. Note the API base URL is
currently hardcoded in `web/src/api/client.ts` (`API_BASE_URL =
'http://localhost:5000'`) — there's a `TODO` there to move it to an env
var before this is deployed anywhere the API isn't reachable at that exact
address. Update that (and rebuild) before deploying to a real environment.

---

## Health check

`GET /health` (see `Program.cs`) checks DB connectivity and returns:

**Healthy** — HTTP 200:
```json
{ "status": "healthy", "database": "connected" }
```

**Unhealthy** — HTTP 503, two possible shapes:
```json
{ "status": "unhealthy", "database": "unreachable" }
```
```json
{ "status": "unhealthy", "database": "error", "detail": "<exception message>" }
```

**Usage:**
```bash
curl -s http://localhost:5000/health
```

**What to do about an unhealthy response:**
1. Check MySQL is actually up: `docker compose ps` (local) or your host's
   equivalent. Restart it if it's down: `docker compose up -d mysql`.
2. Check the API's connection string is correct for the environment
   (`ConnectionStrings__CapacityDb`) — a common failure mode is a stale
   host/port/credentials after a MySQL container recreate.
3. Check network reachability between the API host and the DB host
   (firewall, security group, same docker network, etc.) if they're on
   different hosts.
4. If `detail` is present (the "error" shape), that's the raw exception
   message from `CanConnectAsync()` — read it first, it usually names the
   actual problem (auth failure, unknown database, host unreachable).
5. This endpoint is unauthenticated (no `RequireAuthorization()`) and
   returns no data beyond connectivity status — safe to point an external
   uptime monitor / load balancer health probe at it directly.

---

## Backup / restore (mysqldump)

There's no automated backup job in this repo yet — this is the manual
procedure until one exists.

### Backup

```bash
docker compose exec mysql mysqldump \
  -u root -pdev_root_password \
  --single-transaction --routines --triggers \
  capacity_dev > backup-$(date +%Y%m%d-%H%M%S).sql
```

- `--single-transaction` takes a consistent snapshot without locking
  tables (safe for InnoDB, which is what EF Core migrations create here).
- Swap the database name / credentials for whatever the real environment
  uses — `capacity_dev` / `dev_root_password` are the local dev defaults
  from `docker-compose.yml`.
- Store the resulting `.sql` file somewhere durable (not the same host as
  the DB) — this repo doesn't prescribe an off-host storage location since
  none has been chosen yet.

### Restore

```bash
# Stop the API first so it isn't writing against a mid-restore database.
cat backup-20260711-120000.sql | docker compose exec -T mysql \
  mysql -u root -pdev_root_password capacity_dev
```

Restoring into a **fresh** database (e.g. disaster recovery onto a new
host) instead of overwriting an existing one:

```bash
docker compose exec mysql mysql -u root -pdev_root_password \
  -e "CREATE DATABASE IF NOT EXISTS capacity_dev;"
cat backup-20260711-120000.sql | docker compose exec -T mysql \
  mysql -u root -pdev_root_password capacity_dev
```

After any restore, confirm the app's migration state matches what's in the
backup (`dotnet ef migrations list` vs. the `__EFMigrationsHistory` table
in the restored DB) before starting the API — a backup taken before a
since-applied migration will need that migration re-applied
(`dotnet ef database update`, see "Deploy" above) after restore.

---

## Rollback

Two independent things can need rolling back: the deployed code/artifact,
and the database schema (migrations). Roll back code first, then decide
whether the DB also needs to move — most of the migrations in this repo
so far are additive (new tables/columns), so a code rollback alone is
often enough; only roll back the DB schema too if the new migration is
genuinely incompatible with the previous code version.

### Rolling back the deploy artifact / code

- If you deployed straight from a git commit (`dotnet publish` /
  `npm run build` from that commit): check out the previous known-good
  commit and re-run the build + deploy steps in "Deploy" above.

  ```bash
  git log --oneline -10          # find the last known-good commit
  git checkout <previous-good-sha>
  # re-run dotnet publish / npm run build from "Deploy" above
  ```
- If you're keeping build artifacts (published `api/publish/` output,
  `web/dist/`) from previous deploys, restoring the previous artifact
  directly is faster than rebuilding — just swap it back in and restart
  the API process / redeploy the static web bundle.

### Rolling back an EF Core migration

Roll the database back to a specific earlier migration by naming the
**target** migration (the one you want to end up on) — EF Core reverts
everything applied after it:

```bash
cd api
dotnet ef database update <previous-migration-name> --project src/Api.Data --startup-project src/Api
```

Concretely, to undo `AddOutboxMessages` and land back on `AddAiEvaluations`:

```bash
dotnet ef database update AddAiEvaluations --project src/Api.Data --startup-project src/Api
```

To roll all the way back to nothing (drops every EF-managed table —
destructive, confirm you have a backup first):

```bash
dotnet ef database update 0 --project src/Api.Data --startup-project src/Api
```

**Before rolling back a migration in anything other than a throwaway local
DB:** take a backup first (see "Backup" above) — `dotnet ef database
update` to an earlier migration runs that migration's `Down()` method,
which for a column/table-drop migration means the data in the
dropped/altered structure is gone even if you migrate forward again later.

---

## Pending business decisions that affect all of the above

Per `CLAUDE.md`: pilot team, go-live date, and the real AD group → role
mapping are still open. This runbook will need a "Production configuration"
section rewritten (real `AdIdentityProvider` config, whatever hosting
target gets chosen, secrets-manager integration if any) once those land —
everything above currently only really covers a single-host / demo-grade
deploy running `MockIdentityProvider`.
