# Architecture reference

Reviewed against the working tree on 2026-11-04. See [feature status](CURRENT-SYSTEM.md), [database structure](DATABASE.md) and [requirements](PROJECT.md). Source presence does not establish deployment or production readiness.

```mermaid
flowchart LR
    Web[Web: MVC, Razor, Identity, HTTP and SMTP worker] --> Business[Business: account and booking services, rules, health]
    Business --> Data[Data: MongoDB persistence and transactions]
    Migrator[Explicit Migrator CLI] --> Data
    Data --> Mongo[(MongoDB replica set)]
```

## Layer responsibilities

Web owns controllers, ViewModels, Razor, authentication cookies, anti-forgery, rate limiting, Data Protection, SMTP delivery and local assets. It calls Business rather than MongoDB directly. Business owns application validation, account/verification services, reservation orchestration and health checks. Data owns typed records, narrow persistence operations, indexes, transaction guards, outbox storage and migration leases. Migrator registers the runner separately; Web never runs migrations on startup.

## Booking consistency

Guests save a protected draft and verify an email code before any hold. Verified account holders submit directly. Final submission rechecks rules and availability. A MongoDB transaction contends on catalog/resource/date guards, checks overlaps/idempotency, consumes guest proof when applicable, inserts the reservation and queues its receipt. Whole-shop covers every active bay. Intervals are half-open UTC ranges; guard dates use Asia/Manila.

Reservations embed resource IDs, contact snapshots and history. No separate customer, allocation or coaching-session collection exists. Staff/Admin review supports approval, rejection, coach confirmation and cancellation decisions. Saved-deadline expiry competes with approval/payment under persisted guards. The notification worker processes Pending expiry/alerts and durable outbox delivery; SMTP is at least once, so interrupted sends can duplicate.

## Access and configuration

Accounts use normalized-email uniqueness, verification, password recovery and optional Google subjects. Account routes enforce ownership; guest access uses bearer-token hashes. Public signup creates Customer roles only. Account and guest verification share a service with separate purpose scopes. Preserve Data Protection keys across restarts and share them across deployment instances.

`/health/live` checks the HTTP process. `/health/ready` checks MongoDB and migration IDs 001–007. Readiness does not verify SMTP, Google credentials, full index integrity or a live POS connection. The Web service-unavailable filter handles database availability failures.

Whole-shop checkout is an explicitly enabled Development/Demo simulation. Live POS, real collection, receipts, operational reports, chatbot, general rescheduling and attendance workflows remain unimplemented requirements. [October diagram exports](diagrams/README.md) retain target-design context; use the diagrams here and in CURRENT-SYSTEM for current source behavior.


Migration 006 hardens the existing collection types and records actual CustomerAccountId separately from the legacy guest/account ownership scope. New history entries have EventType and ReservationStatus; existing display descriptions remain readable. EmailVerificationPersistence serves both verification purposes while retaining the historical physical collection. See DATABASE.md for exact BSON mappings and migration rollout limitations.
