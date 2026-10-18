# Architecture Reference

## 1. System Overview

The **Hole In Hub Golf & Lounge Reservation Management Subsystem** is an indoor simulator bay scheduling and private event reservation application built with .NET 10, ASP.NET Core MVC, MongoDB, Tailwind CSS v4, and GSAP.

```
┌────────────────────────────────────────────────────────┐
│                   HoleInHub.Web                        │
│   (Controllers, ViewModels, Razor Views, Assets)       │
└───────────────────────────┬────────────────────────────┘
                            │
                            ▼
┌────────────────────────────────────────────────────────┐
│                  HoleInHub.Business                    │
│   (Domain Policies, Reservation Services, DTOs)        │
└───────────────────────────┬────────────────────────────┘
                            │
                            ▼
┌────────────────────────────────────────────────────────┐
│                   HoleInHub.Data                       │
│   (MongoDB Context, Entities, Indexes, Migrations)     │
└────────────────────────────────────────────────────────┘
```

## 2. Layering & Responsibility Boundaries

1. **`HoleInHub.Web`**:
   - Presentation tier.
   - Hosts Razor views, MVC controllers, ViewModels, Tailwind CSS v4 compilation, and local GSAP motion scripts.
   - Enforces HTTP input validation, anti-forgery tokens, correlation ID propagation, and exposes health probes (`/health/live`, `/health/ready`).
   - **Prohibition**: Never directly references `HoleInHub.Data` or MongoDB driver namespaces.

2. **`HoleInHub.Business`**:
   - Core domain logic and application services.
   - Enforces booking validation rules, capacity holds, venue time calculations (UTC instant to `Asia/Manila` local calendar), and health diagnostics.
   - **Prohibition**: Independent of UI concerns; never imports Razor or HTTP context types.

3. **`HoleInHub.Data`**:
   - Persistence tier using official `MongoDB.Driver`.
   - Manages singleton `IMongoDbContext`, typed collections, BSON serialization attributes, and distributed lease locking.
   - Encapsulates database migration logic (`MongoMigrationRunner`).
   - **Prohibition**: No speculative generic repository abstractions; operations are explicit and narrow.

4. **`HoleInHub.Migrator`**:
   - Dedicated console CLI for running database migrations under an exclusive distributed lease.
   - Completely decoupled from web startup to prevent race conditions across multiple web replicas.

---

## 3. Database Engine & Concurrency Design

- **Sole Database**: MongoDB 7.0+ (replica set `rs0` in local development and test; managed replica set in production).
- **Reservation Exclusivity Contract (for future feature phases)**:
  - Intervals are treated as half-open: `[startUtc, endUtc)`.
  - Mutations contend on atomic guard documents per resource and venue-local calendar date (`Asia/Manila`).
  - Whole-shop bookings acquire guards across all bookable bays and catalog versions in a single atomic transaction.
  - Submissions enforce idempotency via client-supplied request tokens.
