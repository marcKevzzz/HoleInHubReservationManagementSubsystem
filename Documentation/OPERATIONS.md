# Operations and Production Runbook

This runbook guides operators through configuring, monitoring, and maintaining the Hole In Hub Reservation Management Subsystem in staging and production.

---

## 1. Environment Variables Configuration

| Variable | Required | Default | Purpose |
| :--- | :--- | :--- | :--- |
| `ASPNETCORE_ENVIRONMENT` | Yes | `Production` | Controls error display, security headers, HSTS |
| `MongoDb__ConnectionString` | Yes | - | MongoDB URI (must specify replicaSet parameter) |
| `MongoDb__DatabaseName` | Yes | `holeinhub_prod` | Target production database name |
| `MongoDb__ReplicaSetName` | No | `rs0` | Replica set identifier |
| `MongoDb__ServerSelectionTimeoutMs` | No | `5000` | Timeout for cluster node discovery |

---

## 2. Health Check Probes

The application provides two endpoints for load balancers and orchestrators (e.g. Kubernetes, AWS ECS, Azure App Service):

### 1. Liveness Probe: `GET /health/live`
- **Purpose**: Checks if the ASP.NET Core process is running and accepting HTTP connections.
- **Expected Status**: `200 OK`
- **Action on Failure**: Container restart.

### 2. Readiness Probe: `GET /health/ready`
- **Purpose**: Verifies that MongoDB is reachable via ping command AND baseline migrations have been applied.
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
