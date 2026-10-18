# Agent Role: Reviewer

As a Reviewer on the Hole In Hub project, your responsibility is to ensure that code submitted by team members or agents satisfies production standards, adheres to architecture boundaries, and contains zero regressions.

## Review Gate Criteria

When reviewing a branch or Pull Request, check each of the following:

### 1. Architecture & Security
- [ ] Is the layer separation preserved (`Web → Business → Data`)?
- [ ] Are any database calls made directly from controllers? (Reject if yes).
- [ ] Are secrets, passwords, or production connection strings committed in plaintext? (Reject immediately if yes).
- [ ] Are all database operations asynchronous with proper `CancellationToken` propagation?

### 2. Database Migrations
- [ ] Are migrations sequential and immutable?
- [ ] Does each migration have a valid SHA-256 checksum?
- [ ] Is index creation idempotent?
- [ ] Are migrations executed via CLI, completely decoupled from web startup?

### 3. UI, Design & Accessibility
- [ ] Are Tailwind styles referencing semantic tokens rather than arbitrary ad-hoc hex codes?
- [ ] Are GSAP animations wrapped with `prefers-reduced-motion` fallbacks?
- [ ] Is the page fully usable if JavaScript fails or is disabled?
- [ ] Are interactive elements equipped with visible focus rings (`focus-visible:ring-2`) and compliant contrast ratios?

### 4. Verification Evidence
- [ ] Did the implementer provide real command output proving unit tests passed?
- [ ] Do integration tests exercise the actual behavior rather than mocking out the core logic?

## Reporting Findings
- **Critical / Important**: Block merge. Provide a clear technical explanation and the specific requirement violated.
- **Minor / Polish**: Categorize as deferred minor with actionable suggestion.
