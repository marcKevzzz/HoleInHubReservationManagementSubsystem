# Hole In Hub Golf & Lounge Reservation Management Subsystem

A web-based reservation subsystem for simulator bays and private spaces at Hole In Hub Golf & Lounge, Quezon City, Philippines. Built with **.NET 10**, **ASP.NET Core MVC**, **MongoDB 7.0+**, **Tailwind CSS v4**, and **GSAP**.

---

## 📚 Project Documentation

### Governance & Contributing

- [Agent & Contributor Guidelines](AGENTS.md) — Architectural boundaries, source-of-truth hierarchy, and coding standards.
- [Contributing Guide](CONTRIBUTING.md) — Git branching rules, Conventional Commits, and pull request workflows.
- [Pull Request Checklist](.github/pull_request_template.md) — Automated and manual review gates.

### Technical & Operational Specifications

- [Project Overview & Business Requirements](Documentation/PROJECT.md) — Definitive rules for bays, whole-shop bookings, capacity holds, and venue policies.
- [Architecture & Layering Reference](Documentation/ARCHITECTURE.md) — Three-tier boundaries, dependency contracts, and concurrency design.
- [Developer Onboarding](Documentation/ONBOARDING.md) — Step-by-step setup from a clean checkout to running locally.
- [Automated Testing Strategy](Documentation/TESTING.md) — Unit tests, web smoke tests, and replica-set integration suites.
- [MongoDB Migrations Guide](Documentation/MIGRATIONS.md) — Lease locking, SHA-256 checksums, and schema evolution.
- [Design System & GSAP Motion](Documentation/DESIGN_SYSTEM.md) — Tailwind tokens, accessible motion, and WCAG 2.2 AA rules.
- [Security Guidelines](Documentation/SECURITY.md) — Secret management, anti-forgery, and correlation logging.
- [Operations & Runbook](Documentation/OPERATIONS.md) — Health probes, environment variables, backup/restore, and deployment checklist.
- [Chapter I Manuscript](Documentation/CHAPTER_1.md) — Revised Chapter I transcription (aligned 2026-10-27).
- [Aligned System Diagrams](Documentation/diagrams/README.md) — Editable draw.io workbook, vector PDF, system flowchart, combined Level 2 DFD, architecture, and POS integration.

---

## 🏛️ Solution Architecture

The solution enforces a strict unidirectional dependency structure (`Web → Business → Data`):

| Project                                 | Responsibility                                                                      | Key Technologies                                      |
| :-------------------------------------- | :---------------------------------------------------------------------------------- | :---------------------------------------------------- |
| **`HoleInHub.Web`**                 | Razor views, ViewModels, Controllers, Tailwind v4, local GSAP motion, health probes | .NET 10 ASP.NET Core MVC, Tailwind CSS 4.3, GSAP 3.12 |
| **`HoleInHub.Business`**            | Domain policies, reservation services, health services, DTOs                        | .NET 10 Class Library                                 |
| **`HoleInHub.Data`**                | MongoDB persistence, migration runner, distributed leasing, indexes, entities       | `MongoDB.Driver 3.2.1`                                |
| **`HoleInHub.Migrator`**            | Standalone CLI runner for schema evolution and index deployment                     | .NET 10 Console, `Microsoft.Extensions.Hosting`       |
| **`tests/HoleInHub.Tests.Unit`**        | Pure logic tests (ordering, checksums, options validation)                          | xUnit, FluentAssertions                               |
| **`tests/HoleInHub.Tests.Web`**         | WebApplicationFactory smoke tests (`/health/live`, layout rendering)                | `Microsoft.AspNetCore.Mvc.Testing`                    |
| **`tests/HoleInHub.Tests.Integration`** | MongoDB replica set integration tests (lease contention, reruns)                    | MongoDB 7.0+ Replica Set (`rs0`), xUnit               |

---

## 🚀 Quickstart & Commands

### 1. Prerequisites

- **.NET 10 SDK**
- **Node.js 20+** and **npm**
- **Docker Desktop** (for local MongoDB replica set)

### 2. Build the Solution

```powershell
dotnet build HoleInHubReservationManagementSubsystem.slnx
```

_(Automatically restores dependencies, packages local GSAP, and compiles Tailwind CSS)_.

### 3. Start Local MongoDB Replica Set

```powershell
docker compose -f docker-compose.mongo.yml up -d
```

### 4. Run Database Migrations

```powershell
dotnet run --project HoleInHub.Migrator -- migrate
```

### 5. Run the Automated Tests

```powershell
# Unit & Web Smoke Tests (Fast, no database needed)
dotnet test HoleInHubReservationManagementSubsystem.slnx --filter "Category!=Integration"

# Full Replica Set Integration Tests (Requires Docker MongoDB)
dotnet test HoleInHubReservationManagementSubsystem.slnx --filter "Category=Integration"
```

### 6. Run the Web Application

```powershell
dotnet run --project HoleInHub.Web/HoleInHub.Web.csproj --launch-profile https
```

Access the application at `https://localhost:7189`.

---

## 🎨 Design System & Motion

- **Tailwind CSS v4**: Tokens defined in `HoleInHub.Web/Styles/site.css` under `@theme`.
- **GSAP Motion**: Bundled locally in `HoleInHub.Web/wwwroot/js/vendor/gsap.min.js`. Progressive enhancement with automatic OS `prefers-reduced-motion` compliance.
- Continuous CSS watch command:
  ```powershell
  cd HoleInHub.Web
  npm run watch:css
  ```
