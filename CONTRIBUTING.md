# Contributing to Hole In Hub

Welcome! This guide outlines how engineers and contributors collaborate effectively, keep code consistent, and prevent merge conflicts in the Hole In Hub Reservation Management Subsystem.

---

## Prerequisites

1. **.NET 10 SDK** (v10.0+)
2. **Node.js** (v20 LTS or v22 LTS) and **npm**
3. **Docker Desktop** or Docker Engine (for local MongoDB replica set testing)

---

## Multi-Contributor Collaboration Rules

To ensure rapid, collision-free development when multiple people (or agents) are building features in parallel:

### 1. Short-Lived Feature Branches
- Branch names should follow standard prefixes:
  - `feat/feature-name` (e.g., `feat/bay-calendar`)
  - `fix/bug-description` (e.g., `fix/lease-timeout-renewal`)
  - `docs/topic-name` (e.g., `docs/architecture-update`)
  - `refactor/scope` (e.g., `refactor/health-checks`)
- Never push directly to `main`.
- Keep pull requests small and focused on a single responsibility. Aim for under 300 lines of functional change whenever possible.

### 2. High-Contention Files & Coordination
Certain files are touched frequently across features. Coordinate before editing:
- `HoleInHub.Web/Program.cs` (Service registrations & middleware)
- `HoleInHub.Web/Styles/site.css` (Design tokens)
- `HoleInHub.Web/Views/Shared/_Layout.cshtml` (Global layout & navigation)
- Solution and `.csproj` project files
- Migration catalogs

**Rule**: If your change requires modifying a high-contention file, register new services in dedicated extension methods (e.g., `builder.Services.AddHoleInHubData(configuration)`) to keep `Program.cs` clean and diff-friendly.

### 3. Rebase Before Verification
Always rebase or merge `main` into your feature branch before running your final verification tests:
```powershell
git fetch origin
git rebase origin/main
dotnet test HoleInHubReservationManagementSubsystem.slnx
```

---

## Commit Message Standards

We use [Conventional Commits](https://www.conventionalcommits.org/):
```
<type>(<optional scope>): <description>

[optional body]

[optional footer(s)]
```

### Allowed Types:
- `feat`: A new user-facing feature or domain capability.
- `fix`: A bug fix.
- `docs`: Documentation updates only.
- `style`: Formatting, missing semi-colons, no code change.
- `refactor`: A code change that neither fixes a bug nor adds a feature.
- `test`: Adding or correcting tests.
- `chore`: Build process, dependency updates, CI tasks.

### Examples:
- `feat(migration): add lease acquisition and timeout renewal logic`
- `fix(motion): honor prefers-reduced-motion in modal animations`
- `docs(onboarding): add docker replica set setup steps`

---

## Code Quality & Formatting

- Code formatting is governed by `.editorconfig`.
- Verify formatting locally:
  ```powershell
  dotnet format --verify-no-changes
  ```
- To auto-format your changes:
  ```powershell
  dotnet format
  ```

---

## Pull Request Process

1. Create your branch from the latest `main`.
2. Implement your changes following TDD (write tests, verify failure, implement, verify pass).
3. Ensure all automated tests pass:
   ```powershell
   dotnet test HoleInHubReservationManagementSubsystem.slnx --filter "Category!=Integration"
   ```
4. Complete the checklist in `.github/pull_request_template.md`.
5. Open your Pull Request. At least one approval from a code owner is required before merge.
