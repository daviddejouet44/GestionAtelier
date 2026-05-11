# AGENTS.md

## Cursor Cloud specific instructions

### Overview

GestionAtelier is an ASP.NET Core 8.0 web application for print shop (offset/digital) workshop management. It provides a Kanban board for tracking print jobs through production stages plus a client portal for order submission.

### Required services

| Service | Purpose | Startup |
|---------|---------|---------|
| MongoDB | Primary data store (jobs, users, settings) | `mongod --dbpath /var/lib/mongodb --logpath /var/log/mongodb/mongod.log --fork --bind_ip 127.0.0.1 --port 27017` |
| ASP.NET Kestrel | Web server on port 5080 | `GA_HOTFOLDERS_ROOT=/tmp/hotfolders dotnet run -r linux-x64 --no-self-contained` (from `/workspace`) |

### Build & Run

```bash
# Restore + build
dotnet restore -r linux-x64
dotnet build -r linux-x64 --no-self-contained

# Run (requires MongoDB running on localhost:27017)
export GA_HOTFOLDERS_ROOT=/tmp/hotfolders
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

### Seeding data

On a fresh MongoDB, create an admin user:
```bash
mongosh --quiet --eval 'db = db.getSiblingDB("GestionAtelier"); db.users.insertOne({ id: "001", login: "admin", password: "admin123", profile: 3, name: "Administrateur" })'
```

### Environment variables

| Variable | Required | Default | Notes |
|----------|----------|---------|-------|
| `GA_HOTFOLDERS_ROOT` | Yes (on Linux) | `C:\Flux` | Set to `/tmp/hotfolders` |
| `GA_ENCRYPTION_KEY` | No | Derived from hardcoded passphrase | AES-256 key for stored credentials (≥32 chars) |
