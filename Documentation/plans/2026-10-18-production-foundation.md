# Production Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Establish an efficient, consistent, conflict-resistant, production-grade foundation for the Hole In Hub Reservation Management Subsystem across team collaboration rules, agent/skill workflows, MongoDB configuration and lease-guarded migrations, automated tests (unit, integration, smoke), a Tailwind v4 + GSAP design system, and full operations/architecture documentation.

**Architecture:** Maintain strict 3-tier layering (`Web → Business → Data`) in .NET 10 with Razor views and MongoDB. Database migrations run via a dedicated lease-guarded CLI command; local development uses a replica set; styling and motion are driven by local Tailwind CSS v4 and locked GSAP scripts with WCAG 2.2 AA and `prefers-reduced-motion` compliance.

**Tech Stack:** .NET 10 ASP.NET Core MVC, MongoDB .NET Driver 3.x, Tailwind CSS 4.x, GSAP 3.x, xUnit, FluentAssertions, Microsoft.AspNetCore.Mvc.Testing, GitHub Actions, Docker Compose.

**Spec:** [Documentation/specs/2026-10-18-production-foundation.md](file:///c:/Users/Admin/source/repos/HoleInHubReservationManagementSubsystem/Documentation/specs/2026-10-18-production-foundation.md)

## Global Constraints

- Mandatory NoSQL: MongoDB is the single primary database; no relational or secondary database, no in-memory fallbacks in production.
- Strictly preserve dependency direction: `HoleInHub.Web` → `HoleInHub.Business` → `HoleInHub.Data`.
- Do not introduce a generic repository abstraction or premature CRUD layers.
- Do not invent speculative reservation entities before business policies are settled; foundation models are restricted to migration metadata, lease management, and health/ping documents.
- Migrations must never execute automatically inside web startup; they run only via an explicit CLI command under a distributed lease.
- No runtime CDN dependencies: GSAP and Tailwind assets must be bundled or copied locally via npm.
- Motion must progressively enhance and strictly respect `prefers-reduced-motion`.
- All database operations must be asynchronous and accept `CancellationToken`.
- No secrets in source code; configuration validated via strongly-typed options.

## Review Focus

- Lease loss during long-running migration: runner must abort safely without marking incomplete migrations as applied.
- Tampered or modified migration script: runner must detect checksum mismatch against existing `__migrations` collection and immediately abort.
- JavaScript disabled or `prefers-reduced-motion` active: page UI, navigation, and reservation flows must remain fully visible and operational without motion errors.
- Concurrent migration attempts: secondary runners must detect active lease and terminate with a clear error and non-zero exit code.
- Missing MongoDB replica set: integration tests and readiness health checks must fail fast with clear diagnostics rather than hanging or silently passing.

---

### Task 1: Governance, Team Conventions, and CI Workflows

**Files:**
- Create: `AGENTS.md`
- Create: `CONTRIBUTING.md`
- Create: `.editorconfig`
- Create: `.gitattributes`
- Create: `.github/pull_request_template.md`
- Create: `.github/CODEOWNERS`
- Create: `.github/workflows/ci.yml`
- Create: `Directory.Build.props`

**Interfaces:**
- Produces: Standardized code formatting rules, Git line-ending normalizations, branch/PR standards, and GitHub Actions CI definition (`build`, `format-check`, `unit-test`, `css-check`).

- [ ] **Step 1: Create `.editorconfig` and `.gitattributes`**
  Define standard C# 12 / .NET 10 style rules (tabs/spaces, naming conventions, nullable enabled, explicit braces) and ensure LF / CRLF normalization.

- [ ] **Step 2: Create `Directory.Build.props`**
  Configure repository-wide compiler settings: `TreatWarningsAsErrors=false` (for dev), nullable reference types enabled, deterministic builds, and shared metadata.

- [ ] **Step 3: Create `AGENTS.md` and `CONTRIBUTING.md`**
  Document the source-of-truth hierarchy, project references (`Web → Business → Data`), short-lived branch rules, commit message standards (Conventional Commits), conflict avoidance strategies, and role instructions for human and AI pair programmers.

- [ ] **Step 4: Create `.github/pull_request_template.md` and `.github/CODEOWNERS`**
  Provide structured PR verification checklists (unit tests, migration safety, accessibility/reduced-motion check, doc updates) and modular ownership mapping.

- [ ] **Step 5: Create `.github/workflows/ci.yml`**
  Define the automated CI pipeline: checkout, setup .NET 10, setup Node.js 22, restore npm dependencies, build Tailwind CSS, `dotnet build --configuration Release`, run unit tests, and build publish artifact.

- [ ] **Step 6: Verify formatting and repository consistency**
  Run: `dotnet format --verify-no-changes` or `dotnet build` from root to confirm project files respect the conventions.

---

### Task 2: Repository-Local Skills and Agent Role Guidelines

**Files:**
- Create: `.agents/skills/db-migration/SKILL.md`
- Create: `.agents/skills/ui-motion/SKILL.md`
- Create: `.agents/skills/release-verify/SKILL.md`
- Create: `.agents/roles/implementer.md`
- Create: `.agents/roles/reviewer.md`

**Interfaces:**
- Produces: Executable instructions for creating database migrations, writing accessible Tailwind/GSAP UI components, and running the release verification checklist.

- [ ] **Step 1: Author `.agents/skills/db-migration/SKILL.md`**
  Detail the contract for authoring a new migration: immutable incrementing ID (`YYYYMMDDHHMM_Description.cs`), deterministic SHA256 checksum, idempotent operations, and lease protection rules.

- [ ] **Step 2: Author `.agents/skills/ui-motion/SKILL.md`**
  Detail design rules for Tailwind v4 tokens, accessible GSAP animations, WCAG 2.2 AA color contrast rules, keyboard focus rings, and required `@media (prefers-reduced-motion)` handling.

- [ ] **Step 3: Author `.agents/skills/release-verify/SKILL.md`**
  Step-by-step pre-flight checklist: clean build, migration dry-run, integration test status, asset verification, health check probe, and secrets validation.

- [ ] **Step 4: Author `.agents/roles/implementer.md` and `.agents/roles/reviewer.md`**
  Guidelines for contributors and AI subagents covering scope boundaries, non-destructive refactoring, test expectations, and actionable code review.

---

### Task 3: MongoDB Foundation and Data Access Layer

**Files:**
- Modify: `HoleInHub.Data/HoleInHub.Data.csproj`
- Create: `HoleInHub.Data/Configuration/MongoDbOptions.cs`
- Create: `HoleInHub.Data/Context/IMongoDbContext.cs`
- Create: `HoleInHub.Data/Context/MongoDbContext.cs`
- Create: `HoleInHub.Data/Entities/MigrationRecord.cs`
- Create: `HoleInHub.Data/Entities/MigrationLease.cs`
- Create: `docker-compose.mongo.yml`

**Interfaces:**
- Consumes: MongoDB configuration settings (`ConnectionString`, `DatabaseName`, `ReplicaSetName`).
- Produces: `IMongoDbContext` with collection accessors for `__migrations`, `__migration_leases`, and core health pings.

- [ ] **Step 1: Add official MongoDB driver to `HoleInHub.Data`**
  Update `HoleInHub.Data.csproj` with `<PackageReference Include="MongoDB.Driver" Version="3.2.1" />` and Microsoft configuration extensions.

- [ ] **Step 2: Define `docker-compose.mongo.yml`**
  Configure a single-node replica set MongoDB 7.0/8.0 service with automated replica set initialization (`rs.initiate()`) for local development and integration tests.

- [ ] **Step 3: Implement `MongoDbOptions.cs` with validation**
  Create strongly typed options with `[Required]` data annotations and connection string sanity checks.

- [ ] **Step 4: Implement entities `MigrationRecord.cs` and `MigrationLease.cs`**
  Define documents with BSON attributes: `MigrationId`, `Description`, `Checksum`, `AppliedAtUtc`, `ExecutionTimeMs` for records; `LeaseId`, `Owner`, `AcquiredAtUtc`, `ExpiresAtUtc` for leases.

- [ ] **Step 5: Implement `IMongoDbContext` and `MongoDbContext`**
  Provide singleton client lifecycle management, database handles, and typed collection accessors with cancellation token support.

---

### Task 4: Distributed Lease-Guarded Migration Engine

**Files:**
- Create: `HoleInHub.Data/Migrations/IMongoMigration.cs`
- Create: `HoleInHub.Data/Migrations/MongoMigrationLeaseManager.cs`
- Create: `HoleInHub.Data/Migrations/MongoMigrationRunner.cs`
- Create: `HoleInHub.Data/Migrations/Definitions/001_InitialFoundation.cs`
- Create: `HoleInHub.Migrator/HoleInHub.Migrator.csproj`
- Create: `HoleInHub.Migrator/Program.cs`
- Modify: `HoleInHubReservationManagementSubsystem.slnx`

**Interfaces:**
- Consumes: `IMongoDbContext`, collection definitions, migration classes.
- Produces: Standalone CLI executable `HoleInHub.Migrator` callable via `dotnet run --project HoleInHub.Migrator -- migrate`.

- [ ] **Step 1: Define `IMongoMigration.cs`**
  Contract specifying `string MigrationId`, `string Description`, `string CalculateChecksum()`, and `Task UpAsync(IMongoDatabase database, CancellationToken ct)`.

- [ ] **Step 2: Implement `MongoMigrationLeaseManager.cs`**
  Implement atomic distributed lease acquisition via MongoDB `FindOneAndUpdate` with heartbeat renewal and explicit release. Timeout after bounded duration (e.g. 60 seconds).

- [ ] **Step 3: Implement `MongoMigrationRunner.cs`**
  Discover migrations in order, acquire lease, check applied records in `__migrations`, verify checksums against previous runs (throw on mismatch), execute pending `UpAsync`, record completion, and release lease.

- [ ] **Step 4: Implement baseline migration `001_InitialFoundation.cs`**
  Create collection validation rules, unique indexes on migration records, and a dummy `SystemHeartbeat` document.

- [ ] **Step 5: Create console project `HoleInHub.Migrator` and link in `.slnx`**
  Build a dedicated CLI entry point that reads configuration, runs migrations with detailed logging, and returns exit code 0 on success or 1 on failure.

---

### Task 5: Business and Web Integration with Health Probes

**Files:**
- Modify: `HoleInHub.Business/HoleInHub.Business.csproj`
- Create: `HoleInHub.Business/Services/IHealthService.cs`
- Create: `HoleInHub.Business/Services/HealthService.cs`
- Modify: `HoleInHub.Web/HoleInHub.Web.csproj`
- Modify: `HoleInHub.Web/Program.cs`
- Modify: `HoleInHub.Web/appsettings.json`
- Modify: `HoleInHub.Web/appsettings.Development.json`

**Interfaces:**
- Consumes: `IMongoDbContext` registered in DI.
- Produces: `/health/live` (liveness) and `/health/ready` (readiness with MongoDB ping & migration check) endpoints.

- [ ] **Step 1: Wire `HoleInHub.Business` dependencies**
  Reference `HoleInHub.Data` from `HoleInHub.Business`. Add `IHealthService` interface and implementation checking MongoDB reachability.

- [ ] **Step 2: Configure DI and Health Endpoints in `HoleInHub.Web/Program.cs`**
  Register `MongoDbOptions`, `IMongoDbContext`, `IHealthService`, and native ASP.NET Core Health Checks mapping `/health/live` and `/health/ready`.

- [ ] **Step 3: Update `appsettings.json` and `appsettings.Development.json`**
  Provide safe local replica set connection strings: `mongodb://localhost:27017/holeinhub_dev?replicaSet=rs0`.

---

### Task 6: Tailwind CSS v4 & GSAP Motion Design System

**Files:**
- Modify: `HoleInHub.Web/package.json`
- Modify: `HoleInHub.Web/Styles/site.css`
- Create: `HoleInHub.Web/wwwroot/js/motion.js`
- Modify: `HoleInHub.Web/Views/Shared/_Layout.cshtml`
- Modify: `HoleInHub.Web/Views/Home/Index.cshtml`
- Create: `HoleInHub.Web/Views/Shared/_StatusBadge.cshtml`
- Modify: `HoleInHub.Web/HoleInHub.Web.csproj`

**Interfaces:**
- Produces: Semantic design tokens (colors, typography, focus rings), locally served GSAP vendor bundle, accessible card/badge components, and motion initialization script.

- [ ] **Step 1: Install GSAP and configure asset copying**
  Add `"gsap": "^3.12.5"` to `HoleInHub.Web/package.json`. Add an npm script to copy `node_modules/gsap/dist/gsap.min.js` to `wwwroot/js/vendor/gsap.min.js`.

- [ ] **Step 2: Define design tokens in `HoleInHub.Web/Styles/site.css`**
  Extend Tailwind v4 `@theme` with brand tokens:
  - Deep fairway green (`--color-fairway-*`), Warm bunker sand (`--color-sand-*`), Dark slate lounge (`--color-lounge-*`), Amber warning, Emerald success.
  - Consistent focus ring utilities (`focus-visible:ring-2 focus-visible:ring-offset-2`).

- [ ] **Step 3: Implement accessible motion in `motion.js`**
  Create `window.HoleInHubMotion` wrapper checking `window.matchMedia('(prefers-reduced-motion: reduce)')`. If reduced motion is requested, immediately set final states and skip tweens.

- [ ] **Step 4: Update `_Layout.cshtml` and `Index.cshtml`**
  Incorporate responsive header, navigation with accessible contrast, footer, GSAP vendor script tag, `motion.js`, and animated dashboard cards on Home Index.

- [ ] **Step 5: Verify Tailwind and GSAP build**
  Run: `npm run build:css` inside `HoleInHub.Web` to ensure clean compilation into `wwwroot/css/site.css`.

---

### Task 7: Automated Test Suite (Unit, Integration, and Smoke)

**Files:**
- Create: `HoleInHub.Tests.Unit/HoleInHub.Tests.Unit.csproj`
- Create: `HoleInHub.Tests.Unit/Migrations/MigrationOrderingTests.cs`
- Create: `HoleInHub.Tests.Unit/Migrations/MigrationChecksumTests.cs`
- Create: `HoleInHub.Tests.Integration/HoleInHub.Tests.Integration.csproj`
- Create: `HoleInHub.Tests.Integration/Fixtures/MongoDbFixture.cs`
- Create: `HoleInHub.Tests.Integration/Migrations/MongoMigrationRunnerTests.cs`
- Create: `HoleInHub.Tests.Web/HoleInHub.Tests.Web.csproj`
- Create: `HoleInHub.Tests.Web/Smoke/HealthEndpointTests.cs`
- Modify: `HoleInHubReservationManagementSubsystem.slnx`

**Interfaces:**
- Produces: Executable test suites covering pure logic (Unit), real MongoDB replica set behaviors (Integration), and HTTP pipeline (Web Smoke).

- [ ] **Step 1: Implement Unit Tests in `HoleInHub.Tests.Unit`**
  Test migration ordering logic, checksum detection for altered migrations, and options validation without requiring external databases.

- [ ] **Step 2: Implement Integration Tests in `HoleInHub.Tests.Integration`**
  Create `MongoDbFixture` with isolated database names per test run. Test:
  - Sequential migration application.
  - Idempotent rerun (running migration twice succeeds without reapplying).
  - Checksum mismatch error.
  - Lease acquisition and lock contention rejection.

- [ ] **Step 3: Implement Web Smoke Tests in `HoleInHub.Tests.Web`**
  Use `WebApplicationFactory<Program>` to verify `/health/live` returns 200 OK and homepage returns standard layout markup.

- [ ] **Step 4: Add all test projects to `HoleInHubReservationManagementSubsystem.slnx`**
  Run `dotnet test` across the entire solution.

---

### Task 8: Production Documentation Suite and Onboarding

**Files:**
- Create: `Documentation/ARCHITECTURE.md`
- Create: `Documentation/ONBOARDING.md`
- Create: `Documentation/TESTING.md`
- Create: `Documentation/MIGRATIONS.md`
- Create: `Documentation/DESIGN_SYSTEM.md`
- Create: `Documentation/SECURITY.md`
- Create: `Documentation/OPERATIONS.md`
- Modify: `README.md`

**Interfaces:**
- Produces: Comprehensive documentation suite guiding developers, operations, and reviewers from setup to deployment.

- [ ] **Step 1: Author `ARCHITECTURE.md` and `ONBOARDING.md`**
  Map system components, data flow, MongoDB replica set expectations, environment setup, and first-time contributor steps.

- [ ] **Step 2: Author `MIGRATIONS.md` and `TESTING.md`**
  Document the schema migration lifecycle, lease mechanics, rollback guidelines, and test execution instructions.

- [ ] **Step 3: Author `DESIGN_SYSTEM.md` and `SECURITY.md`**
  Document Tailwind v4 token usage, GSAP animation guidelines, accessibility rules, anti-forgery protection, and credential hygiene.

- [ ] **Step 4: Author `OPERATIONS.md` and update `README.md`**
  Document deployment preparation, health probe usage, environment variables table, backup/restore guidance, and update `README.md` navigation.

---

### Task 9: Final Clean Verification and Build Check

- [ ] **Step 1: Verify clean build from solution root**
  Run `dotnet build HoleInHubReservationManagementSubsystem.slnx` to ensure zero compilation errors.

- [ ] **Step 2: Run all unit and smoke tests**
  Run `dotnet test HoleInHubReservationManagementSubsystem.slnx --filter "Category!=Integration"` to verify non-database dependent tests pass cleanly.

- [ ] **Step 3: Verify Tailwind CSS output and vendor scripts**
  Confirm `wwwroot/css/site.css` and `wwwroot/js/vendor/gsap.min.js` are present and up to date.
