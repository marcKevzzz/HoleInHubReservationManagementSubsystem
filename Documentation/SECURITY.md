# Security Standards and Best Practices

This document outlines the security architecture and guidelines for the Hole In Hub Reservation Management Subsystem.

---

## 1. Credentials and Secret Management

- **No Secrets in Git**: Connection strings, secrets, and credentials must never be committed to Git.
- **Local Development**: Use `dotnet user-secrets` or local environment variables:
  ```powershell
  dotnet user-secrets set "MongoDb:ConnectionString" "mongodb://localhost:27017/?replicaSet=rs0" --project HoleInHub.Web/HoleInHub.Web.csproj
  ```
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
