# Existing-feature schema hardening

**Goal:** Improve validation, naming clarity, ownership, history and customer-history indexing without new admin workflows.
**Architecture:** Keep migrations 001–005 byte-for-byte unchanged. Introduce 006 with preflight validation, additive fields and indexes; retain legacy BSON names where existing migration expressions depend on them. Use a coordinated deployment, not mixed old/new writers.
**Spec:** Owner request in this chat; Documentation/PROJECT.md; current models and tests.

## Scope and decisions

- Keep AccountId as the legacy ownership/idempotency scope (including guest: hashes); add optional CustomerAccountId for an actual signed-in account. Never invent guest accounts.
- Keep guest_verifications as the physical collection because migration 004 and current TTL use it. Rename its persistence service and context/consumption properties in code; map properties to legacy BSON names. Add explicit purpose for new challenges; do not infer purpose for already-hashed legacy scopes.
- Add EventType and ReservationStatus to new history entries, retaining legacy Status descriptions for existing readers/UI. Preserve legacy history without guessing unknown events.
- Add typed validators for current reservation/account/resource/block/verification/outbox records. Validate existing documents first; fail rather than delete or fabricate invalid legacy data. Enforce start < end on reservations/blocks.
- Add (AccountId, CreatedUtc descending) history index; defer other index changes until measured. Document retention without adding unapproved deletion jobs.
- Do not apply migrations to Atlas/shared preview, implement admin features, change payment policy, rename immutable migration classes or remove existing keys.

## Tasks

- [x] Add integration tests for validator rejection, safe reruns/preflight, ownership backfill, legacy reads, new history and verification purposes; observe failures before implementation.
- [x] Add migration 006, registration/readiness, clearer persisted models/service names and update every caller/test fixture.
- [x] Rewrite database dictionary/schema JSON, update current documentation and rollout/retention instructions.
- [x] Build, unit/Web tests, isolated local integration tests, JavaScript/CSS checks; review final change and record evidence.

## Review focus

Guest retry/ownership must remain isolated; existing BSON field names and 001–005 checksums must remain unchanged; invalid old documents must not be silently repaired; encrypted context must not be logged; demo/payment and cancellation behavior must survive the stronger validator.


## Verification and shared-checkout decisions

- The initial 15 migration regression cases failed before 006 existed and passed after implementation. Expanded coverage checks legacy reads, challenge purposes, final demo-payment history state and exact published/installed validator parity.
- Migrations 001–005 were verified byte-for-byte unchanged using the pre-change SHA-256 snapshot.
- Normal Web output was locked by an existing preview. Preserved that process and built with --artifacts-path to a temporary directory. Existing MigratorCliTests assume the normal bin path; ran those through the normal unit output separately rather than changing their implementation for this task.
- Ruling: separate guest-recovery work appeared concurrently as migration 007. Preserve it; document its current purpose/quota schema separately and verify 006 baseline before 007. Do not broaden or rewrite 006 to absorb that unrelated feature.
- No Atlas/shared-preview migration was executed. The coordinated backup/stop/migrate/start release procedure remains required.


Final evidence: isolated solution build passed with 0 warnings/errors; combined tests passed 58 unit + 59 Web + 51 integration, with all 63 unit tests (including the five CLI path tests) separately passing from normal output: 173 distinct .NET tests. npm test:ui passed 46 tests; explicit build:css passed. Scoped diff whitespace and documentation links passed. Independent source review found no actionable defects. The first full Web integration run exposed three incomplete test fixtures; valid required fields were added and all HTTP cases subsequently passed.
