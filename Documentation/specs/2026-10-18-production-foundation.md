# Shared development and production foundation

Status: proposed specification for review, not an implemented production system.

> Business requirements aligned on 2026-10-27 with revised [Chapter I](../CHAPTER_1.md) and [PROJECT.md](../PROJECT.md). The original foundation scope remains distinct from subsequent reservation/POS feature implementation.

## Purpose and constraints

Prepare Hole In Hub for concurrent work by multiple contributors and eventual production deployment. Success means a new contributor can reproduce the build, follow one set of rules, submit a small reviewed change, and verify database and UI changes without guessing. Automated checks must enforce the conventions that can be checked reliably.

User requirements: NoSQL is mandatory; use Tailwind CSS and GSAP for design. Select MongoDB as the single database. Keep the existing .NET 10 MVC application, Razor views, and Web → Business → Data project references. Hosting is not selected. Do not introduce microservices, a second database, a frontend framework, or a general repository abstraction.

Documentation/PROJECT.md remains the source of booking requirements. This foundation does not implement the reservation product or resolve its open business policies. Production deployment remains gated on authentication, authorization, reservation correctness, and operational acceptance.

## Scope and deliverables

1. Root AGENTS.md and CONTRIBUTING.md: architecture boundaries, commands, source-of-truth hierarchy, task ownership, review criteria, and completion requirements.
2. Repository-local skills for database changes, UI changes, and release verification, plus concise agent role instructions for implementation and review. These are instructions, not autonomous services or permission to deploy.
3. Reproducible .NET and Node tooling, formatting conventions, dependency lockfiles, and generated-asset rules.
4. Pull-request template, ownership template, and GitHub Actions checks. No remote repository or team identities are assumed.
5. MongoDB configuration, local replica-set environment, a small database foundation, and an explicit migration command with tests.
6. Unit/integration/web smoke test projects as needed to test implemented foundation behavior; future feature tests are specified separately.
7. Tailwind design tokens, GSAP integration conventions, and a minimal shared motion implementation with verification.
8. Architecture, onboarding, testing, migration, design, security, and operations documentation, linked from README.md.

## Collaboration and ownership

Use short-lived branches and small pull requests. Assign one contributor to each task and identify shared files before editing. Parallel work may proceed on independent files; coordinate changes to Program.cs, project files, package manifests/locks, shared layout, tokens, and migration manifests. Rebase or merge the target branch before final verification; never discard another contributor's work.

Changes include their affected callers, configuration, tests, and documentation. Domain policy changes require an explicit decision recorded with the requirement; agent instructions cannot override user requirements. Record architectural decisions only when they affect multiple contributors or deployment.

Implementation agents follow the same rules as humans. Review agents report actionable findings and do not silently change scope. Migration and release roles must report actual checks and failures. Agent role documents do not automatically create parallel agents or grant access to credentials, production, or external messaging.

The current directory has no Git repository. Implementation may prepare versioned files here, but must not create a remote, choose branch protection settings, invent CODEOWNERS identities, or claim CI is active. Document repository initialization and administrator setup. Git initialization is not required to review these artifacts.

## Application boundaries

Web owns controllers, Razor/view models, HTTP validation, authorization wiring, and assets. Business owns shared booking policy and orchestration. Data owns MongoDB documents, validators/indexes, persistence operations, and transaction mechanics. Preserve the current dependency direction; expose narrow operations when features need them rather than creating speculative CRUD interfaces.

Use asynchronous database operations with cancellation tokens. Validate configuration at startup for enabled database-dependent functionality. Keep customer data and credentials out of logs. Return safe error responses with correlation identifiers; preserve diagnostic exceptions in protected application logs.

## MongoDB foundation

Use the official MongoDB .NET driver with one application-lifetime MongoClient. Configure connection URI and database name through configuration/environment variables or development user secrets. Commit only non-secret examples. Local development and integration tests use a single-node replica set; production uses a managed or operated replica set with TLS, access control, backup, and restore capability.

Do not silently fall back to an in-memory database or standalone MongoDB. Database integration tests must fail clearly when their required environment is unavailable. Basic liveness reports process availability; readiness checks database reachability and required migration state without returning connection details.

Do not invent reservation documents before unresolved scheduling and identity decisions are settled. The foundation initially needs migration metadata and the minimal documents necessary to exercise its persistence behavior. Production reservation/customer collections and their migrations arrive with their features.

## Migration contract

NoSQL still needs controlled schema evolution. Each migration has a unique immutable ordered identifier, a description, a checksum, and explicit changes to documents, collection validators, or indexes. Applied migrations are recorded with identifier, checksum, and completion time. An applied identifier with changed content fails verification.

Run migrations through an explicit deployment command, never automatically in web startup or independently in every replica. A database-backed exclusive lease prevents concurrent runners; renewal and ownership checks prevent a runner from continuing after lease loss. Use a bounded acquisition timeout and release only the lease owned by that runner.

Migration operations must tolerate interruption and rerun. Mark a migration applied only after all its operations succeed. Do not assume index/collection operations and all backfill batches can share one atomic transaction. Use bounded batches for backfills. A failed run returns a nonzero exit code and preserves enough metadata for diagnosis.

Prefer expand → migrate → contract so old and new application versions can coexist during rollout. Destructive changes require a reviewed backup/restore procedure and compatibility window. Roll back application binaries when compatible; data recovery uses a tested forward repair or restore, not an automatic destructive down migration.

## Reservation concurrency contract for future features

A query followed by inserts, even inside a transaction, is insufficient to enforce interval exclusivity. Every allocation mutation must contend on the same persisted guard documents as competing requests.

Recommended scheme: guard each resource and venue-local calendar date touched by its half-open interval [start, end). Acquire/update guards in stable resource/date order within the same transaction that checks conflicts and writes reservations/allocations/history. Cover every touched date, including cross-midnight intervals; an interval ending exactly at midnight does not touch the following day. Store instants in UTC and derive dates using the Philippine venue timezone.

Whole-shop requests enumerate all bookable resources through the same guard mechanism. Changes to the bookable resource set require a common catalog guard also acquired by allocation operations, preventing a resource from being added outside a concurrent whole-shop allocation. Rescheduling locks both old and new resource/date sets and commits all changes atomically.

Retry transient transaction conflicts with bounded attempts, rechecking availability on each retry. Business conflicts return a useful conflict response. Use a unique idempotency key to detect repeated submissions and version checks to reject stale staff edits. Follow the revised Pending hold/expiry policy in [PROJECT.md](../PROJECT.md): ordinary online requests have a proposed 30-minute deadline during agreed staff review hours, alerts at 15 and 25 minutes, and atomic release on expiry. Whole-shop deposit deadlines and confirmed staff-managed no-show handling remain separate. Notifications run after commit and must not roll back a successful booking.

These are future implementation acceptance criteria, not claims that the starter already prevents double booking. Re-evaluate guard granularity only against measured contention and verified correctness.

## Tests and CI

Unit tests cover pure behavior introduced by this foundation. Integration tests use real MongoDB replica-set semantics, isolated database names, deterministic cleanup limited to test-owned databases, and no production connection strings. Cover migration ordering, checksum mismatch, concurrent runners, rerun after failure, lease loss, and configuration validation.

Future reservation changes require concurrency tests for simultaneous overlapping requests, adjacent intervals, all-or-nothing multi-bay writes, whole-shop/resource creation races, cross-midnight allocation, safe rescheduling, duplicate submissions, and stale edits. Permission and ownership tests accompany authentication features. Do not add placeholder tests that pass without exercising behavior.

CI installs pinned toolchains, restores locked dependencies, builds CSS/JS, checks formatting, builds Release, runs unit and MongoDB integration tests, and publishes a deployable artifact. Separate test categories clearly; missing integration infrastructure fails its CI job. PR checks must not require production secrets. Deployment is a separately approved workflow after hosting is selected.

Document exact local equivalents of CI commands. Claim success only after executing checks and inspecting results; list unavailable checks and environmental limitations.

## Tailwind and GSAP design rules

Keep Razor and the existing Tailwind build. Define a small shared token set for semantic colors, typography, spacing, focus, radii, and motion. Use reusable Razor partials/components when repeated UI exists; do not prebuild a component catalog with no consumers. Maintain one shared layout and consistent form/error/status patterns.

Install GSAP through a locked npm dependency and serve a locally built/copied browser asset. No runtime CDN dependency. Generated CSS and copied vendor assets have one documented owner and reproducible build; contributors edit sources. Build/publish must work from a clean checkout and include generated assets in the publish output.

Motion progressively enhances an already usable interface. Respect prefers-reduced-motion and skip nonessential animation. Avoid hidden initial content that stays invisible when JavaScript fails. Use transforms/opacity, clean up animation contexts when applicable, and keep booking correctness independent of animation completion.

Require keyboard access, visible focus, semantic labels, associated validation messages, sufficient contrast, responsive layouts, and status meaning beyond color. Adopt WCAG 2.2 AA as the design target; do not claim conformance without an audit. Reserve assertive motion for meaningful feedback rather than continuous decoration. No full visual redesign is included.

## Security and production operations

Keep secrets outside source control, use least-privilege database users, and avoid public database exposure. Enforce server-side authorization and customer ownership when those features are implemented. State-changing browser forms require anti-forgery protection; input validation, safe output encoding, secure cookies, and login throttling are release requirements. Configure trusted proxy handling only for the selected host topology.

Document development, CI, staging, and production settings separately. Provide an environment variable reference, health-check behavior, structured logging conventions, and a release checklist. Record the deployed application version and migration state. Monitor errors, database availability, migration failures, and failed background work without logging personal data.

A production release requires a staging migration rehearsal, restored-backup verification, smoke tests, rollback compatibility assessment, HTTPS/proxy verification, configured secrets, and an accountable operator. Hosting-specific manifests, notification provider setup, actual deployment, and real performance targets require later infrastructure/business decisions.

## Implementation sequence

1. Team instructions, docs index, formatting/toolchain conventions, and contribution templates.
2. MongoDB local environment, configuration, migration tooling, and meaningful integration tests.
3. Tailwind/GSAP asset integration, minimal shared tokens/motion, and publish verification.
4. CI, onboarding, and operations instructions; run the documented clean build/test/publish checks.

Each stage updates documentation and tests in the same change. Detailed file-level implementation steps and execution method follow approval of this specification.

## Acceptance and exclusions

Acceptance: a contributor can follow README.md from a clean checkout, reproduce assets/build/tests, run migrations twice safely against a local replica set, and understand rules for ownership, database changes, UI changes, and release. CI configuration provides equivalent gates once pushed to GitHub. Implementation reports evidence and remaining limitations accurately.

Excluded from this foundation: full booking features, authentication product flows, invented business policies, production billing/POS/vouchers, chatbots, notifications, a comprehensive redesign, a second database, and an actual production deployment. The revised Chapter I requires an agreed POS integration interface in a subsequent feature phase; billing/payment/voucher processing remains external. This foundation does not implement that interface, and simulated payment evidence must not be treated as verified evidence.
