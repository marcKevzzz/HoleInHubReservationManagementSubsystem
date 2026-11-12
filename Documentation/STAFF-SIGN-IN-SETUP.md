# Staff sign-in setup

## Current local diagnosis (12 November 2026)

The development instance runs at `http://localhost:5275`. `/staff/sign-in` returns 503 because Staff PIN access is not configured. Admin uses the separate email/password sign-in at `/account/sign-in`.

## Normal Staff and Manager PIN access

1. Create a Staff or Manager through **Team & access**. Complete the account's email verification and password setup.
2. Configure a dedicated HTTPS staff hostname and its server TLS certificate. The staff HTTPS listener must request and validate desktop client certificates. Keep public customer access on its own listener/hostname. The app reads the client certificate from the TLS connection; a generic forwarded header is not accepted as desktop proof.
3. Enroll the work desktop's client certificate and configure these deployment secrets/settings:

| Key | Value |
| --- | --- |
| `StaffPin__Enabled` | `true` |
| `StaffPin__Host` | Staff hostname only, without scheme or port |
| `StaffPin__Pepper` | A securely generated random secret, at least 32 characters |
| `StaffPin__DesktopFingerprints__0` | Approved desktop certificate's 64-character SHA-256 fingerprint |

Add indexed fingerprint keys for additional desktops. The deployment must include migrations 016 and 017 (current database migrations extend through 019).

4. Restart the Web application so its PIN-access configuration reloads.
5. On the enrolled desktop, open `https://<staff-host>/staff/pin/setup`, enter the verified work email and account password, and choose a six-digit PIN.
6. Use `https://<staff-host>/staff/sign-in` with the work email and PIN.

For local HTTPS, the existing `https` launch profile listens at `https://localhost:7077` and `http://localhost:5275`. Starting that profile alone does not enroll a desktop or enable PIN access.

## Development presentation alternative

The existing Development-only presentation mode permits a configured Staff demo account with an existing PIN on the configured HTTPS host. It asks only for the PIN. Set `StaffPin__PresentationAccount` alongside the enabled flag, hostname and secret pepper. PIN setup through `/staff/pin/setup` is disabled in presentation mode, so prepare the demo account and PIN before switching modes. Manager presentation has additional isolated-local-database restrictions; follow the implementation specification rather than enabling it against the normal database.

## Troubleshooting

- **503 / Staff sign-in unavailable:** `StaffPin:Enabled` is false or missing.
- **403 / Work desktop required:** HTTP, wrong staff hostname, missing/unapproved/expired desktop certificate, or a presentation restriction.
- **PIN rejected:** Check verified email, Staff/Manager role, existing PIN, current security stamp and attempt lockout.
- **Admin account:** Use `/account/sign-in`; Admin accounts do not use the Staff PIN flow.

See [registered-desktop implementation](specs/2026-11-07-rbac-staff-authentication.md#registered-desktop-pin) and [Microsoft certificate authentication](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/certauth?view=aspnetcore-10.0).
