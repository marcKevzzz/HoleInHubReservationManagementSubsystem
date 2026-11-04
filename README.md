# Hole In Hub Golf & Lounge Reservation Management Subsystem

A web-based reservation subsystem for simulator bays and private spaces at Hole In Hub Golf & Lounge, Quezon City, Philippines. Built with **.NET 10**, **ASP.NET Core MVC**, **MongoDB 7.0+**, **Tailwind CSS v4**, and **GSAP**.

---

## 📚 Project Documentation

### Governance & Contributing

- [Agent & Contributor Guidelines](AGENTS.md) — Architectural boundaries, source-of-truth hierarchy, and coding standards.
- [Contributing Guide](CONTRIBUTING.md) — Git branching rules, Conventional Commits, and pull request workflows.
- [Pull Request Checklist](.github/pull_request_template.md) — Automated and manual review gates.

### Technical & Operational Specifications

- [Current implementation](Documentation/CURRENT-SYSTEM.md) — Features present in the working tree, exact booking rules and remaining work (2026-11-04).
- [Implemented database structure](Documentation/DATABASE.md) — Actual collections, reservation fields, validators and migrations 001–007.

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
| **`HoleInHub.Web`**                 | Razor views, ViewModels, Controllers, Tailwind v4, local GSAP motion, health probes | .NET 10 ASP.NET Core MVC, Tailwind CSS 4.3, GSAP 3.13 |
| **`HoleInHub.Business`**            | Domain policies, reservation services, health services, DTOs                        | .NET 10 Class Library                                 |
| **`HoleInHub.Data`**                | MongoDB persistence, migration runner, distributed leasing, indexes, entities       | `MongoDB.Driver 3.2.1`                                |
| **`HoleInHub.Migrator`**            | Standalone CLI runner for schema evolution and index deployment                     | .NET 10 Console, `Microsoft.Extensions.Hosting`       |
| **`tests/HoleInHub.Tests.Unit`**        | Pure logic tests (booking rules, migration CLI, checksums, configuration)                          | xUnit, FluentAssertions                               |
| **`tests/HoleInHub.Tests.Web`**         | WebApplicationFactory tests (accounts, guest booking, ownership, pages and outages)                | `Microsoft.AspNetCore.Mvc.Testing`                    |
| **`tests/HoleInHub.Tests.Integration`** | MongoDB replica set integration tests (allocation, booking, outbox and migrations)                    | MongoDB 7.0+ Replica Set (`rs0`), xUnit               |

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

Choose local MongoDB below or follow the [Atlas collaborator setup](Documentation/ONBOARDING.md#collaborator-setup-on-windows), including persistent environment variables and Mailpit. Each collaborator supplies their own credentials; `.env` files are not loaded automatically.

### 3. Start Local MongoDB Replica Set

```powershell
docker compose -f docker-compose.mongo.yml up -d
```

### 4. Select the Local Database and Run Migrations

These process overrides keep the local quickstart separate from persistent Atlas settings:

```powershell
$env:MongoDb__ConnectionString = 'mongodb://localhost:27017/?replicaSet=rs0'
$env:MongoDb__DatabaseName = 'holeinhub_dev'
$env:MongoDb__ReplicaSetName = 'rs0'
dotnet run --project HoleInHub.Migrator -- migrate
```

### 5. Run the Automated Tests

```powershell
# Unit & Web Smoke Tests (Fast, no database needed)
dotnet test HoleInHubReservationManagementSubsystem.slnx --filter "Category!=Integration"

# Full Replica Set Integration Tests (Requires Docker MongoDB)
$env:MongoDb__ConnectionString = 'mongodb://localhost:27017/?replicaSet=rs0'
dotnet test HoleInHubReservationManagementSubsystem.slnx --filter "Category=Integration"
```

### Local Test Email Without Docker

In a separate PowerShell 7 terminal on Windows x64:

```powershell
pwsh -File scripts/start-mailpit.ps1
```

Open <http://localhost:8025> for verification and reset emails. The launcher installs Mailpit locally on first run; existing Web development SMTP settings work unchanged. See [local email testing](Documentation/ONBOARDING.md#local-email-testing-without-docker) for setup and testing details. Atlas plus this launcher requires no Docker.

### 6. Run the Web Application

```powershell
dotnet watch --project HoleInHub.Web run --launch-profile http
```

Open <http://localhost:5275>. The optional `https` profile uses <https://localhost:7077>. Check <http://localhost:5275/health/ready> before testing sign-in or availability.

---

## 🎨 Design System & Motion

- **Tailwind CSS v4**: Tokens defined in `HoleInHub.Web/Styles/site.css` under `@theme`.
- **GSAP Motion**: Bundled locally in `HoleInHub.Web/wwwroot/js/vendor/gsap.min.js`. Progressive enhancement with automatic OS `prefers-reduced-motion` compliance.
- Continuous CSS watch command:
  ```powershell
  cd HoleInHub.Web
  npm run watch:css
  ```
