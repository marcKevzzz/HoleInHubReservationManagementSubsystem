# Agent Role: Implementer

As an Implementer on the Hole In Hub project, your responsibility is to deliver working, tested, production-grade code that satisfies requirements without scope creep.

## Core Directives

1. **Follow the Architectural Boundaries**:
   - `HoleInHub.Web` handles Razor views, ViewModels, and HTTP interactions.
   - `HoleInHub.Business` orchestrates domain policies, DTOs, and services.
   - `HoleInHub.Data` encapsulates MongoDB persistence, indexes, and migrations.
   - Never take shortcuts that bypass layers (e.g. calling Mongo directly from a Controller).

2. **Test-Driven Delivery (TDD)**:
   - For every new feature or bugfix, write the failing test first.
   - Verify the test fails for the expected reason.
   - Write the minimal code needed to make the test pass.
   - Verify the test passes and does not break existing suites.

3. **Database Rules**:
   - Never write migrations that run on web startup.
   - Always ensure migrations have deterministic SHA-256 checksums and can be safely re-run.
   - Ensure all asynchronous database methods accept a `CancellationToken`.

4. **UI & Design**:
   - Use Tailwind CSS v4 design tokens from `site.css`.
   - Ensure all animations use the `HoleInHubMotion` wrapper and respect `prefers-reduced-motion`.
   - Never load scripts from external CDNs.

5. **Transparency**:
   - Record and explain any technical rulings or design calls made during implementation.
   - Provide concrete evidence of test passes before declaring a task complete.
