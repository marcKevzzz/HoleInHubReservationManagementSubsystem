## Description
<!-- Brief 2-3 sentence overview of what this pull request introduces or fixes. -->

## Type of Change
- [ ] `feat`: New capability or domain feature
- [ ] `fix`: Bug fix
- [ ] `refactor`: Structural improvement without behavioral change
- [ ] `test`: Unit / Integration / Smoke test additions
- [ ] `docs`: Documentation update
- [ ] `chore`: Tooling, CI, dependencies

## Scope & Architectural Boundaries
- [ ] Follows strict layering: `Web → Business → Data`
- [ ] No direct database calls from `HoleInHub.Web`
- [ ] No in-memory database mocks used for production code
- [ ] All database/network methods are asynchronous and accept `CancellationToken`

## Database Migrations (if applicable)
- [ ] Migration uses sequential, immutable identifier (e.g., `002_AddCustomers.cs`)
- [ ] SHA-256 checksum is calculated and deterministic
- [ ] Operations are idempotent and tolerate rerun
- [ ] Migration was verified via CLI (`HoleInHub.Migrator`) and not in web startup

## UI, Styling & Accessibility (if applicable)
- [ ] Styled with Tailwind CSS v4 design tokens in `site.css`
- [ ] No external CDN links added (GSAP or vendor JS served locally)
- [ ] Animations verify and honor `@media (prefers-reduced-motion: reduce)`
- [ ] UI is fully readable and usable if JavaScript fails to load
- [ ] Meets WCAG 2.2 AA standards (visible focus rings, minimum 4.5:1 text contrast)

## Verification Checklist
- [ ] `dotnet build HoleInHubReservationManagementSubsystem.slnx` passed cleanly (0 errors)
- [ ] `dotnet test HoleInHubReservationManagementSubsystem.slnx --filter "Category!=Integration"` passed
- [ ] `npm run build:css` executed and updated `wwwroot/css/site.css`
- [ ] Relevant documentation updated under `Documentation/`
