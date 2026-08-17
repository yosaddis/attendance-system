# Attendance Web Portal

## Local development

1. Copy `.env.local.example` to `.env.local` and point `BACKEND_API_URL`
   at a running instance of the backend API.
2. `npm install`
3. `npm run dev` — the portal runs at `http://localhost:3000`.

## Running tests

`npm test`

Server Actions and middleware are tested directly (no browser needed) by
mocking `next/headers`, `next/navigation`, and `next/cache`, and by
stubbing `global.fetch`. Component tests use React Testing Library.

## Manual verification checklist (run once per significant change)

This cannot be automated — it requires a live backend and a real browser.

1. Start the backend (`docker compose up` from `backend/`, or `dotnet
   run`), seed an Operator, and use it to create a tenant, a station, and
   log in as that operator to create a `TenantAdmin` user for the tenant
   (or seed one directly via the database for local testing).
2. Start the portal, visit `http://localhost:3000/login`, sign in as the
   `TenantAdmin` user.
3. On **Shifts**, create a shift; confirm it appears in the table.
4. On **Employees**, create an employee, assign the shift from Step 3;
   confirm the employee list shows the shift name, not "—".
5. On **Attendance**, change the date picker to a date with no punches;
   confirm an empty table (not an error). Use the backend's punch
   ingestion endpoint directly (or the desktop agent) to record a punch
   for today, then confirm it appears after reloading `/attendance`.
6. Resize the browser to a phone width (~375px) and repeat steps 3-5;
   confirm the nav wraps sensibly and tables scroll horizontally instead
   of breaking the page layout.
7. Click **Log out**; confirm redirect to `/login` and that navigating
   directly to `/attendance` afterward also redirects to `/login`.
