# Fingerprint Attendance System — Project Spec

## Overview

A multi-tenant SaaS (rental model) attendance system using USB fingerprint
scanners (ZK4500, SecuGen Hamster Plus) connected to client Windows PCs.
Employees punch in/out via a lightweight desktop agent; admins/owners view
reports on a web portal (mobile-friendly, phone browser access).

Billing is manual for v1: no payment gateway integration. Each tenant has a
status flag (`active` / `grace` / `suspended`) toggled by the operator after
payment is collected out-of-band.

## Key constraints from planning

- USB fingerprint devices are shared with other software on the client's PC —
  the agent must acquire the device handle only during an active capture and
  release it immediately after. It must not hold an exclusive lock.
- A single client site may have multiple PCs ("stations"), each with its own
  device. Employees can punch in from any free station — not a dedicated
  kiosk. All stations at a site share the same tenant.
- **One device brand per site** (no mixed ZK4500 + SecuGen at the same
  tenant) — avoids cross-vendor fingerprint template incompatibility.
- Punch flow is **1:1 verify**: employee enters ID/PIN, then places finger to
  confirm against their stored template (not pure 1:N walk-up matching).
- Templates are enrolled once per employee and synced from the central
  server down to every station at that site (with local caching for offline
  matching).
- Agent must work offline: queue punches locally (SQLite) and sync to the
  backend when connectivity returns.
- Reporting is web-portal only — no WhatsApp/SMS digest for v1.
- Shift planning is required for late/early/hours calculations, with
  configurable punch modes per shift (2-punch: IN/OUT, or 4-punch: IN,
  break-out, break-in, OUT).

## Recommended tech stack

| Layer | Choice |
|---|---|
| Desktop agent | C# / .NET (WPF), SQLite for local offline queue |
| Backend API | ASP.NET Core Web API (or NestJS if preferred) |
| Database | PostgreSQL, `tenant_id` on every tenant-scoped table |
| Web portal | Next.js + Tailwind CSS + Recharts |
| Hosting | Docker Compose + Nginx (TLS via Certbot) on operator's VPS |
| Agent ↔ backend | REST over HTTPS, JSON, API key per station (tenant + station scoped) |

Rationale: using C#/.NET for both agent and backend keeps one language across
the two pieces that talk to each other most, and both ZKFinger and SecuGen
SDKs have first-party or well-documented .NET integration paths — far more
reliable than wrapping native fingerprint SDKs in Electron/Node.

## Data model (draft — refine during schema design)

- **tenants**: id, name, status (`active`/`grace`/`suspended`), device_vendor
  (`zk4500`/`secugen`), created_at
- **stations**: id, tenant_id, name, api_key, device_vendor, last_synced_at
- **employees**: id, tenant_id, employee_code/PIN, name, shift_id
- **fingerprint_templates**: id, employee_id, template_data (encrypted),
  vendor, enrolled_at
- **shifts**: id, tenant_id, name, start_time, end_time, grace_minutes,
  punch_mode (`2`/`4`), break_start, break_end, allowed_break_minutes
- **punches**: id, tenant_id, employee_id, station_id, punch_type
  (`IN`/`BREAK_OUT`/`BREAK_IN`/`OUT`), timestamp, synced_at
- **daily_attendance** (computed/materialized): employee_id, date, first_in,
  last_out, break_duration, total_hours, is_late, late_minutes, is_early_out,
  anomaly_flag (e.g. `missing_checkout`, `double_punch`)

## Core features by phase

**Phase 1 — MVP, single device type, single site**
- Desktop agent: device acquire/release, ID+fingerprint verify capture,
  local SQLite queue, background sync
- Backend: tenant/station/employee CRUD, punch ingestion API, manual
  status flag toggle (operator-only)
- Portal: login, daily attendance view, employee management, basic shift
  assignment

**Phase 2 — Multi-station sync + shift logic**
- Template sync from server to all stations at a site
- Shift-based late/early/hours calculation, configurable punch mode per shift
- Anomaly handling (missing checkout, double punch) with admin manual
  correction in portal

**Phase 3 — Reporting depth**
- Date-range reports, export to Excel/PDF
- Station health/sync status view
- Billing status banner (active/grace/suspended) driven by manual flag

**Phase 4 — Second device vendor + polish**
- SecuGen SDK integration (if not done in Phase 1)
- Agent auto-update mechanism
- PWA manifest for portal ("add to home screen")

## Open questions to resolve during build

- Fixed shift-per-employee vs. rostering/rotating shifts (start with fixed)
- Exact overnight-shift date attribution rule
- Retention/export policy for a tenant's data after cancellation
