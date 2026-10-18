# Hole In Hub Engineering & Agent Guidelines

This repository hosts the **Hole In Hub Reservation Management Subsystem** built with .NET 10, ASP.NET Core MVC, MongoDB, Tailwind CSS v4, and GSAP.

Whether you are a human software engineer or an AI coding assistant (agent), all contributions must adhere strictly to the rules, architectural boundaries, and verification criteria set out below.

---

## 1. Source-of-Truth Hierarchy

When requirements or specifications appear to conflict, resolve them in this order:
1. **User / Owner Directives**: Explicit instructions given by the project owner in the current prompt/session.
2. **Business Requirements Document**: [Documentation/PROJECT.md](file:///Documentation/PROJECT.md) (definitive rules for booking states, whole-shop booking, capacity holds, venue policies).
3. **Foundation Specification**: [Documentation/specs/2026-10-18-production-foundation.md](file:///Documentation/specs/2026-10-18-production-foundation.md) (architecture constraints, database rules, motion policies, CI gates).
4. **Implementation Plans**: Documents in `Documentation/plans/` (step-by-step verified roadmaps).
5. **Codebase Precedents**: Existing code in the repository. Never copy anti-patterns; prefer the specified standards.

---

## 2. Architectural Boundaries & Dependencies

The solution enforces a strict three-tier unidirectional dependency structure:
```
src/HoleInHub.Web  ───►  src/HoleInHub.Business  ───►  src/HoleInHub.Data
```

| Project | Responsibility | Allowed Dependencies | Prohibited |
| :--- | :--- | :--- | :--- |
| **`src/HoleInHub.Web`** | Razor views, Controllers, ViewModels, assets (Tailwind/GSAP), HTTP validation, routing, anti-forgery | `HoleInHub.Business`, Microsoft.AspNetCore.* | Direct MongoDB calls, bypassing Business tier |
| **`src/HoleInHub.Business`** | Reservation rules, booking orchestration, domain interfaces, business DTOs, health checks | `HoleInHub.Data`, Microsoft.Extensions.* | ASP.NET Core MVC UI types, Razor, HttpContext |
| **`src/HoleInHub.Data`** | MongoDB documents, BSON mappings, indexes, migrations, lease mechanics, data contexts | `MongoDB.Driver`, Microsoft.Extensions.Options | Web/UI dependencies, business domain decisions |
| **`src/HoleInHub.Migrator`** | Dedicated CLI command runner for schema evolution and index deployment | `HoleInHub.Data`, Microsoft.Extensions.Hosting | Web/UI dependencies |

### Non-Negotiable Architectural Rules:
- **NoSQL Only**: MongoDB is the sole database engine. Do not introduce relational databases, secondary engines, or in-memory database mocks for production code.
- **No Generic Repository Pattern**: Write narrow, intent-revealing queries and operations where needed.
- **Asynchronous Everywhere**: All database and I/O methods must return `Task` or `Task<T>` and accept a `CancellationToken ct`.
- **Never Run Migrations on Web Startup**: Migrations run solely through an explicit CLI execution (`dotnet run --project src/HoleInHub.Migrator -- migrate`) under an exclusive distributed lease.
- **No Secrets in Code**: Connection strings, API keys, and credentials must come from environment variables or `dotnet user-secrets`.
- **Local Bundling Only**: GSAP and Tailwind CSS must be bundled/copied locally via npm. No runtime CDN links `<script src="https://cdnjs...">`.

---

## 3. Concurrency & Reservation Correctness

For future reservation and scheduling features:
1. **Capacity Hold**: A submitted booking becomes `Pending` and holds capacity immediately. No automatic expiration without staff confirmation or explicit cancellation.
2. **Exclusivity Enforcement**: Contend on persisted guard documents per resource and date `[start, end)`. Never rely on a bare read-then-write sequence even inside a MongoDB multi-document transaction.
3. **Timezones**: Store all timestamps in UTC (`DateTimeOffset` or BSON UTC date); derive local calendar dates using `Asia/Manila` (UTC+8).
4. **Idempotency**: Submissions must carry an idempotency key to prevent double bookings from duplicate clicks or network retries.

---

## 4. UI, Styling & Motion Standards

- **Tailwind CSS v4**: Define design tokens in `src/HoleInHub.Web/Styles/site.css` using `@theme`. Never write hardcoded ad-hoc utility colors when semantic tokens exist.
- **GSAP Animations**: Progressive enhancement only.
- **Accessibility (`prefers-reduced-motion`)**: Every animation must check for reduced motion preferences (`window.matchMedia('(prefers-reduced-motion: reduce)')`). If enabled, snap immediately to the final state without animation.
- **WCAG 2.2 AA**: Maintain minimum 4.5:1 contrast for normal text and 3:1 for large text/icons. Ensure visible keyboard focus rings (`focus-visible:ring-2`) on all interactive controls.
- **No Content Hiding**: Initial markup must be fully visible and accessible if JavaScript fails to execute.

---

## 5. Development & Verification Workflow

Before completing any task or opening a pull request:
1. **Build Verification**:
   ```powershell
   dotnet build HoleInHubReservationManagementSubsystem.slnx
   ```
2. **Unit & Smoke Tests**:
   ```powershell
   dotnet test HoleInHubReservationManagementSubsystem.slnx --filter "Category!=Integration"
   ```
3. **Integration Tests (requires local MongoDB replica set)**:
   ```powershell
   docker compose -f docker-compose.mongo.yml up -d
   dotnet test HoleInHubReservationManagementSubsystem.slnx --filter "Category=Integration"
   ```
4. **Tailwind Build**:
   ```powershell
   cd src/HoleInHub.Web
   npm run build:css
   cd ../..
   ```
5. **Report Actual Evidence**: Never claim tests passed without running them and viewing the real command output.
