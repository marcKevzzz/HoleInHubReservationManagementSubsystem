# Operations and Production Runbook

Synchronized 2026-11-04 for operating the current implementation. This runbook is not a production-readiness certification. Whole-shop checkout remains a Development/Demo simulation; live POS and operational policies remain incomplete. See [current feature status](CURRENT-SYSTEM.md).

---

## 1. Environment Variables Configuration

| Variable | Required | Default | Purpose |
| :--- | :--- | :--- | :--- |
| `ASPNETCORE_ENVIRONMENT` | Yes | `Production` | Controls error display, security headers, HSTS |
| `MongoDb__ConnectionString` | Yes | - | MongoDB URI; Atlas uses its SRV URI and discovers the replica set |
| `MongoDb__DatabaseName` | Yes | Explicitly select | Target database; do not rely on development defaults |
| `MongoDb__ReplicaSetName` | No | Unset | Optional explicit replica set; leave unset for Atlas |
| `MongoDb__ServerSelectionTimeoutMs` | No | `5000` | Timeout for cluster node discovery |

---

## 2. Health Check Probes

The application provides two endpoints for load balancers and orchestrators (e.g. Kubernetes, AWS ECS, Azure App Service):

### 1. Liveness Probe: `GET /health/live`
- **Purpose**: Checks if the ASP.NET Core process is running and accepting HTTP connections.
- **Expected Status**: `200 OK`
- **Action on Failure**: Container restart.

### 2. Readiness Probe: `GET /health/ready`
- **Purpose**: Verifies that MongoDB is reachable via ping command AND all migration IDs 001–007 are recorded, including 005 with Google disabled. This does not verify index integrity, SMTP, OAuth or POS.
- **Expected Status**: `200 OK`
- **Action on Failure**: Remove pod from load balancer pool (do not restart immediately).

---

## 3. Database Backup and Disaster Recovery

### Creating a Snapshot Backup:
```bash
mongodump --uri="mongodb://username:password@cluster-host:27017/holeinhub_prod?replicaSet=rs0" --gzip --archive=/backups/holeinhub_$(date +%Y%m%d_%H%M%S).archive
```

### Restoring from Backup:
```bash
mongorestore --uri="mongodb://username:password@cluster-host:27017/?replicaSet=rs0" --nsInclude="holeinhub_prod.*" --gzip --archive=/backups/holeinhub_target.archive
```

---

## 4. Pre-Deployment Release Checklist

1. [ ] Confirm staging rehearsal migration succeeded cleanly.
2. [ ] Validate database backup taken within the last 1 hour.
3. [ ] Run `dotnet run --project HoleInHub.Migrator -- migrate`.
4. [ ] Deploy application container / artifact.
5. [ ] Verify `/health/ready` returns `200 OK`.
6. [ ] Verify home page and static assets render without console errors.

## 5. Failed Notification Recovery

Notifications stop automatic delivery after five attempts. Monitor the `outbox` collection for `{ Failed: true, SentUtc: null }`; the worker also marks an exhausted claim failed after its two-minute lease expires. Restore SMTP delivery before replaying a failed event. Operator tooling can call `OutboxPersistence.ReplayFailedAsync(eventId, DateTime.UtcNow, ct)` to reset only that failed, unsent event for another bounded delivery batch. Until operator tooling is deployed, use an authenticated MongoDB maintenance session against the intended database:

```javascript
db.outbox.find({ Failed: true, SentUtc: null }, { _id: 1, Recipient: 1, Attempts: 1 });
db.outbox.updateOne(
  { _id: "<reviewed-event-id>", Failed: true, SentUtc: null },
  { $set: { Failed: false, Attempts: 0, DueUtc: new Date(), LeaseUntilUtc: null, LeaseToken: null } }
);
```

Replay individual reviewed events: SMTP delivery is at least once, so a process failure after sending may produce a duplicate. Expired account links require a fresh verification/reset request; reservation alerts are checked against current reservation status before delivery.

## Local Configuration and Readiness

Follow [Developer Onboarding](ONBOARDING.md#configuration-scope-and-secrets) for terminal scope, persistent Windows settings, collaborators, and Mailpit. Both applications currently load environment variables; neither auto-loads `.env`, and Migrator does not load Web user secrets. Restart the launching terminal/IDE/agent after changing persistent user variables. Keep migration credentials separate from application credentials. Before diagnosing sign-in or availability, require `/health/ready` to return HTTP 200 and `Healthy`; preserve an existing configured process when starting a preview.


## Account, SMTP and booking configuration

Use [ONBOARDING.md](ONBOARDING.md) for the supported settings and launch environment. Configure SMTP host/port/from, TLS and credentials as appropriate, and a trusted AccountEmail public base URL for generated links. Production SMTP requires TLS. Configure ReservationPolicy approval, durations, review coverage and staff recipients before accepting requests. Google is optional and disabled by default; enable only after migration 005 and correct client/callback configuration.

Persist and protect ASP.NET Data Protection keys; share them across replicas so cookies, drafts and encrypted queued emails remain readable. Preserve an existing preview's environment before restart. Never use shared/Atlas data for integration tests. Disable simulated payments outside explicit Development/Demo evaluation; no live money collection is implemented.

A healthy database probe does not prove worker/email delivery. Monitor Failed/unsent outbox records and verify an actual delivery. The Web worker processes Pending deadlines and alerts as well as delivery; application downtime delays processing, so deadlines are not a hard real-time guarantee.


## Schema 006 deployment and retention

Follow [MIGRATIONS.md](MIGRATIONS.md#006_schemahardening-existing-feature-validation) for the coordinated upgrade: backup, stop old Web/worker instances, explicit migration, then matching new Web build. Preflight failures require deliberate data repair, not deletion of booking history. Do not run old binaries against additive-field writes.

Schema 006 retains verification ExpiresUtc TTL; the separate migration 007 adds TTL for guest recovery quotas. No new booking, history or outbox purge is enabled. Obtain approved retention periods before deleting sent-email payloads or historical contacts; retain failed/unresolved messages for investigation. Monitor per-booking history growth. Never delete allocation guards or migration state as housekeeping without a separate correctness/recovery procedure.
