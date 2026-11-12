# Operational settings

The Settings workspace (`/staff/settings`) is available to Admin and Manager. Admin can save; Manager has a read-only view with alert recipients hidden.

Migration `019_SystemSettings` creates the validated singleton collection. It is applied only by the Migrator CLI. No settings document is seeded: until the first save, current `ReservationPolicy` deployment values remain authoritative.

Saved settings configure online-request availability, duration-policy approval, staff-review days/hours/alert recipients, venue contacts, and policy publication. Versioned saves reject concurrent edits and accept retries of the same command. Reservation services and the notification worker load the effective settings within each scope. Existing reservation review deadlines and allocations are not rewritten. Contact changes appear on the public Contact page. Published booking, privacy, and venue policies appear at `/policies`; the Privacy link redirects to the published privacy section when available.

Operating hours, session increments, capacity limits, review deadlines and cancellation cutoffs remain enforced application rules. Settings displays these values; changing them requires coordinated changes to scheduling, validation, existing records, and customer interfaces. Policy prose does not override enforcement.

Automatic approval and a staff-assisted reservation switch are future work. The current staff-review workflow and staff booking permissions remain in force.

HTTPS certificates remain deployment configuration, never MongoDB settings. The Security section reports the current connection scheme and links to official Kestrel certificate configuration. Use a hosting/proxy certificate manager or Kestrel certificate configuration, keep passwords/private keys in the deployment secret store, and verify the deployed domain through HTTPS after installing or renewing a certificate. Saving venue settings does not install, renew, or validate a TLS certificate.
