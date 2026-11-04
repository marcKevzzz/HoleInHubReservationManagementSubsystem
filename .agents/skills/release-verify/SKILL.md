---
name: release-verify
description: Pre-release and PR verification procedures for Hole In Hub Reservation Management Subsystem
---

# Hole In Hub Release & PR Verification Skill

This skill contains the authoritative checklist and command sequence required before merging code into `main` or preparing a production release.

## 1. Local Pre-PR Verification Sequence

Run the following commands from the repository root. For database configuration or preview startup, first read [Developer Onboarding](../../../Documentation/ONBOARDING.md#configuration-scope-and-secrets). Preserve the configured launch environment when restarting Web, and verify `/health/ready` before claiming sign-in or availability works. Page rendering alone does not verify MongoDB access. Web and Migrator currently load environment variables; Migrator does not load `.env` or Web user secrets. Keep the integration suite on isolated local databases even when your application uses Atlas.

### Step 1: Verify Code Formatting
```powershell
dotnet format HoleInHubReservationManagementSubsystem.slnx --verify-no-changes
```

### Step 2: Clean Rebuild
```powershell
dotnet clean HoleInHubReservationManagementSubsystem.slnx
dotnet build HoleInHubReservationManagementSubsystem.slnx --configuration Release
```

### Step 3: Fast Test Suite (Unit & Web Smoke)
```powershell
dotnet test HoleInHubReservationManagementSubsystem.slnx --configuration Release --filter "Category!=Integration"
```

### Step 4: MongoDB Integration Test Suite (Local Replica Set)
```powershell
docker compose -f docker-compose.mongo.yml up -d
dotnet test HoleInHubReservationManagementSubsystem.slnx --filter "Category=Integration"
```

### Step 5: Asset & Tailwind Build Verification
```powershell
cd HoleInHub.Web
npm run build
cd ..
```
