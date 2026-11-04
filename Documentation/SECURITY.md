# Security Standards and Best Practices

This document outlines the security architecture and guidelines for the Hole In Hub Reservation Management Subsystem.

---

## 1. Credentials and Secret Management

- **No Secrets in Git**: Connection strings, secrets, and credentials must never be committed to Git.
- **Local Development**: Follow [configuration scope and secrets](ONBOARDING.md#configuration-scope-and-secrets) for the supported setup. Both executables load environment variables; `.env` is not auto-loaded. Web already has a project UserSecretsId and loads its local user secrets in Development; Migrator does not load them. Each collaborator stores their own values outside Git. Local user secrets and Windows user environment variables are unencrypted; rotate credentials exposed in chat, logs, or source control.

- **Production**: Injected via container environment variables or cloud secret stores (Azure Key Vault, AWS Secrets Manager).

---

## 2. Web Security & Request Protection

1. **Anti-Forgery Validation**:
   All state-changing POST/PUT/DELETE forms require ASP.NET Core anti-forgery tokens (`@Html.AntiForgeryToken()`).
2. **Correlation Tracking**:
   The `X-Correlation-ID` header is inspected or generated on every request and stamped onto logs to allow end-to-end tracing without logging PII.
3. **Safe Error Handling**:
   In production environments, detailed stack traces are hidden from users and logged internally with correlation identifiers.
4. **Least-Privilege Database Accounts**:
   Production MongoDB user accounts should have `readWrite` access strictly scoped to the `holeinhub_prod` database, with administrative commands disabled.


## Current account and guest boundaries (2026-11-04)

Public signup creates Customer only. Staff/Admin review uses server-side role authorization; no public role-promotion endpoint exists. Account reservation/profile/transaction access is scoped to the signed-in owner. Guest status requires an unguessable bearer link checked through its hash; references alone grant no access. Protect those links and redact query strings in proxy/access logs.

Account/guest codes expire after ten minutes with five attempts and a one-minute resend cooldown, alongside HTTP rate limits. Purpose scopes remain separate; account codes do not allocate capacity. Guest proof is consumed with booking in the allocation transaction. Drafts and sensitive email payloads use Data Protection; protect and persist/share the key ring. TTL cleanup never substitutes for expiry checks.

Google identities use unique provider subjects, with no automatic same-email account linking. Tokens are not persisted. Consult onboarding for authoritative-email handling and exact callback setup. Password recovery does not send reset mail to unverified accounts.

Whole-shop payments are simulated only under explicit Development/Demo configuration; the code is not a live payment integration or financial ledger. Security controls described here are implementation/reference boundaries, not evidence of a penetration test or production certification.

Guest recovery adds a separate `guest-recovery:` challenge namespace and explicit `GuestRecovery` purpose. A successful verification atomically consumes its proof. Existing booking/account challenges cannot grant recovery access. Per-email delivery quotas, challenge replacement and outbox enqueue commit in one MongoDB transaction under a persisted email guard. Unknown valid emails receive the same challenge flow; booking existence is revealed only after verification.

Recovered history/detail access requires matching verified email plus a `guest:` owner and non-empty persisted `GuestSessionHash` (`EmailVerificationId` in C#). It does not join TTL-deleted challenge records or infer ownership from an absent CustomerAccountId. A protected guest cookie grants thirty minutes of email-scoped access, separate from Identity. All private pages are no-store/no-referrer; cancellation/checkout recheck authorization and retain existing cutoffs/environment gates. Guest history contains no bearer token URLs. Leaving guest access clears only guest cookies; it cannot revoke a copied cookie before its absolute expiry or original emailed reservation links.
