# Developer Onboarding Guide

Welcome to the **Hole In Hub** engineering team! This guide walks you through setting up your local environment, building the solution, running tests, and starting local development from a clean checkout.

---

## 1. Prerequisites

Before cloning or opening the project, ensure you have installed:

| Tool | Minimum Version | Purpose |
| :--- | :--- | :--- |
| **.NET SDK** | 10.0+ | Compiles and runs web and console applications |
| **Node.js** | 20 LTS or 22 LTS | Compiles Tailwind CSS v4 and packages local GSAP |
| **Docker Desktop** | Latest | Runs local single-node MongoDB replica set |
| **Git** | 2.40+ | Version control |

---

## 2. Quickstart: Clean Checkout to Running App

### Step 1: Restore and Build
Run from the repository root:
```powershell
dotnet build HoleInHubReservationManagementSubsystem.slnx
```

### Step 2: Start Local MongoDB Replica Set
```powershell
docker compose -f docker-compose.mongo.yml up -d
```

### Step 3: Run Database Migrations
```powershell
dotnet run --project HoleInHub.Migrator -- migrate
```

### Step 4: Run the Web Application
```powershell
dotnet run --project HoleInHub.Web/HoleInHub.Web.csproj --launch-profile https
```
Navigate to: `https://localhost:7189` or `http://localhost:5260`.

---

## 3. Daily Development Workflow

### Sprint 1 database and email setup

Start Docker Desktop before running Compose. MongoDB and the Mailpit test inbox bind only to this computer. Keep the named MongoDB volume: `docker compose down -v` deletes local data.

Use these environment variables in the same PowerShell session for both Web and Migrator:

```powershell
$env:MongoDb__ConnectionString = 'mongodb://localhost:27017/?replicaSet=rs0'
$env:MongoDb__DatabaseName = 'holeinhub_dev'
$env:MongoDb__ReplicaSetName = 'rs0'
docker compose -f docker-compose.mongo.yml up -d
docker compose -f docker-compose.mongo.yml exec mongodb mongosh --quiet --eval 'db.hello().isWritablePrimary'
dotnet run --project HoleInHub.Migrator -- migrate
```

The primary check must return `true`. Migrations create the database, collections, indexes, and resource catalog; do not create collections manually. Connect MongoDB Compass to the same local URI and select `holeinhub_dev`. Open the development email inbox at <http://localhost:8025>. Mailpit captures email locally; it does not deliver to customers.

### MongoDB Atlas

1. Create an Atlas project and cluster, then create a database user restricted to the intended application database. Atlas login accounts and database users are separate.
2. Permit your current public IP for development and your deployed application's outbound IP for deployment. Avoid unrestricted network access. See the [Atlas connection guide](https://www.mongodb.com/docs/atlas/connect-to-database-deployment/).
3. Copy the .NET driver SRV URI from **Connect → Drivers**. Enter credentials privately through environment variables or a secret store; never commit or paste credentials into chat. URI-encode special characters in credentials.
4. Set `MongoDb__ConnectionString` to the SRV URI and `MongoDb__DatabaseName` to the chosen database. Leave `MongoDb:ReplicaSetName` unset so Atlas discovers its replica set from the SRV URI. The checked-in local connection URI already includes `replicaSet=rs0`; no separate override is needed. Remove any `MongoDb__ReplicaSetName=rs0` environment override when switching to Atlas. Apply these settings to both Web and Migrator.
5. Run the migration CLI explicitly with a separate migration credential authorized to create collections and indexes. Run Web with the restricted application credential. Never migrate automatically on startup.
6. Start Web and check `/health/ready`. Confirm the expected collections in Compass using the Atlas URI. Integration tests must continue using their isolated local test databases, never the shared Atlas database.

Atlas connection failures commonly indicate an IP access-list mismatch, invalid database credentials, a local replica-set override, or DNS/network restrictions. Never disable TLS to work around a failed connection.

### Operational approval

Public browsing is available before booking policy approval. Online submission must remain blocked until staff review coverage, notification recipients, and service duration/eligibility rules are configured and approved. Development test policies are not venue approval. Coaching and whole-shop submissions remain disabled in Sprint 1. Staff approvals, attendance, and changes belong to Sprint 2; this milestone alone is not an operational production launch.

### Continuous Tailwind Compilation
```powershell
cd HoleInHub.Web
npm run watch:css
```

### Running Tests
- **Unit and Smoke tests (fast, no database required)**:
  ```powershell
  dotnet test HoleInHubReservationManagementSubsystem.slnx --filter "Category!=Integration"
  ```
- **Integration tests (with Docker MongoDB running)**:
  ```powershell
  dotnet test HoleInHubReservationManagementSubsystem.slnx --filter "Category=Integration"
  ```
