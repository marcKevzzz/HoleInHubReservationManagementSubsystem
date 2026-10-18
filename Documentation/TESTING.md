# Testing Strategy and Test Suites

This document describes the automated test architecture for the Hole In Hub Reservation Management Subsystem.

---

## 1. Test Project Structure

The solution contains three specialized test projects:

| Project | Type | External Dependencies | Execution Target |
| :--- | :--- | :--- | :--- |
| **`HoleInHub.Tests.Unit`** | Pure logic tests | None | Fast CI & pre-commit (&lt; 200 ms) |
| **`HoleInHub.Tests.Web`** | ASP.NET Core smoke tests | In-memory web pipeline | Verify routing, middleware, correlation, layouts |
| **`HoleInHub.Tests.Integration`** | Database integration | MongoDB 7.0+ Replica Set (`rs0`) | Migration ordering, lease locking, checksum verification |

---

## 2. Test Categories & Execution

### Fast Test Suite (Pre-Commit / CI Gate 1)
Runs all unit and web tests without needing MongoDB:
```powershell
dotnet test HoleInHubReservationManagementSubsystem.slnx --filter "Category!=Integration"
```

### Full Integration Test Suite (CI Gate 2)
Requires a running local or containerized MongoDB replica set:
```powershell
docker compose -f docker-compose.mongo.yml up -d
dotnet test HoleInHubReservationManagementSubsystem.slnx --filter "Category=Integration"
```

---

## 3. Key Test Coverage Areas

1. **Migration Ordering**:
   Ensures migrations execute deterministically in sequential alphanumeric order.
2. **Checksum Integrity**:
   Verifies that any modification to an already applied migration script throws `MigrationChecksumMismatchException`.
3. **Lease Contention**:
   Simulates concurrent runners attempting to acquire the distributed lease simultaneously; ensures secondary runners are rejected.
4. **Idempotent Reruns**:
   Verifies that running the migration runner repeatedly against an up-to-date database completes with 0 newly applied migrations and no errors.
5. **Correlation Tracking**:
   Confirms every HTTP request generates or propagates `X-Correlation-ID` header.
6. **Health Probes**:
   Verifies `/health/live` returns HTTP 200 OK.
