# AGENTS.md

## Cursor Cloud specific instructions

### Overview

GestionAtelier is an ASP.NET Core 8.0 web application for print shop (offset/digital) workshop management. It provides a Kanban board for tracking print jobs through production stages plus a client portal for order submission.

### Required services

| Service | Purpose | Startup |
|---------|---------|---------|
| MongoDB | Primary data store (jobs, users, settings) | `mongod --dbpath /var/lib/mongodb --logpath /var/log/mongodb/mongod.log --fork --bind_ip 127.0.0.1 --port 27017` |
| ASP.NET Kestrel | Web server on port 5080 | `GA_HOTFOLDERS_ROOT=/tmp/hotfolders JWT_SECRET=... LICENSE_PUBLIC_KEY=... dotnet run -r linux-x64 --no-self-contained` (from `/workspace`) |

### Build & Run

```bash
# Restore + build
dotnet restore -r linux-x64
dotnet build -r linux-x64 --no-self-contained

# Run (requires MongoDB running on localhost:27017)
export GA_HOTFOLDERS_ROOT=/tmp/hotfolders
# Required or the app crashes on startup / returns HTTP 500 on authenticated requests (see below)
export JWT_SECRET="dev-jwt-secret-at-least-32-characters-long-xxxx"
export LICENSE_PUBLIC_KEY="dev-dummy-key"          # any non-empty value lets the app boot
export GA_ENCRYPTION_KEY="dev-encryption-key-at-least-32-characters-long"
dotnet run -r linux-x64 --no-self-contained
```

The app listens on `http://localhost:5080`. Interfaces:
- `/pro/` — Internal staff Kanban board
- `/portal/` — Client portal

### Key caveats

- The `.csproj` has `<RuntimeIdentifier>win-x64</RuntimeIdentifier>`. On Linux you must override with `-r linux-x64 --no-self-contained` for both build and run commands.
- The `NormalizeFs` helper in `JobsMoveEndpoints.cs` converts `/` to `\` (Windows convention). File move operations via the API will fail on Linux. Upload and list operations work correctly.
- No `appsettings.json` is committed; the app uses in-code defaults and MongoDB for configuration.
- `GA_HOTFOLDERS_ROOT` must be set to a Linux path (default is `C:\Flux` which doesn't exist on Linux). Use `/tmp/hotfolders`.
- No automated test suite exists in this repository.
- No linter configuration (`.editorconfig`, `dotnet format` rules) is committed.
- **Startup requires `JWT_SECRET` and `LICENSE_PUBLIC_KEY`** (neither is committed). Without `LICENSE_PUBLIC_KEY` (and no `data/public.key`), the app throws a `TypeInitializationException` at startup and exits. Without `JWT_SECRET` (≥32 chars), the app boots but every authenticated request fails with HTTP 500 (`AuthHelper.GetSigningKey`). See the env-var table below.

### Licence gating & local UI testing

- The staff UI (`/pro/`) is gated by a signed licence file (`data/license.lic`, machine-bound, not committed). With `LICENSE_PUBLIC_KEY` set to a dummy value and no valid `.lic`, `GET /api/license/status` returns `level: 0`: the app boots and all APIs work, but the `/pro/` navigation and Kanban tiles are hidden and a licence modal opens (see `wwwroot_pro/js/license.js` → `applyLicenseUI`).
- To exercise the `/pro/` UI end-to-end without a real licence, temporarily force the client-side level in `loadLicenseStatus()` (`license.js`), e.g. set `data.isValid = true; data.level = 3;` before `applyLicenseUI(data)`. **This is a test-only hack — never commit it.**
- The `/portal/` client interface and the JSON APIs are not affected by the licence level.

### Seeding data

On a fresh MongoDB, create an admin user:
```bash
mongosh --quiet --eval 'db = db.getSiblingDB("GestionAtelier"); db.users.insertOne({ id: "001", login: "admin", password: "admin123", profile: 3, name: "Administrateur" })'
```

### Environment variables

| Variable | Required | Default | Notes |
|----------|----------|---------|-------|
| `GA_HOTFOLDERS_ROOT` | Yes (on Linux) | `C:\Flux` | Set to `/tmp/hotfolders` |
| `JWT_SECRET` | Yes | — | JWT signing key (≥32 chars). Missing ⇒ HTTP 500 on every authenticated request. |
| `LICENSE_PUBLIC_KEY` | Yes | `data/public.key` (not committed) | RSA public key for licence validation. Missing ⇒ crash at startup. Any non-empty value lets the app boot with an "invalid licence" (level 0). |
| `GA_ENCRYPTION_KEY` | No | Derived from hardcoded passphrase | AES-256 key for stored credentials (≥32 chars) |
