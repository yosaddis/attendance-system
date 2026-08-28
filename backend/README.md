# Attendance API

## Local development

1. Copy `.env.example` to `.env` and fill in `JWT_KEY`,
   `TEMPLATE_ENCRYPTION_KEY` (32 random bytes, base64-encoded — generate
   with `openssl rand -base64 32`), `OPERATOR_EMAIL`, `OPERATOR_PASSWORD`.
2. `docker compose up --build`
3. API is available at `http://localhost:8080`, Swagger UI at
   `http://localhost:8080/swagger` in Development.
4. Log in as the seeded Operator via `POST /api/auth/login` to start
   creating tenants and stations.

## Running tests

`dotnet test backend/tests/AttendanceApi.Tests`

Tests use an in-memory SQLite database — no Docker or Postgres required.
