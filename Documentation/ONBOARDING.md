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
