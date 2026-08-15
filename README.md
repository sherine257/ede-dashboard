# EDE Contract Portfolio

A self-contained project and contract management dashboard for the Emirates Drug Establishment.

## Run

Open `index.html` in a modern browser, or serve the repository:

```bash
python3 -m http.server 8000
```

Then visit `http://localhost:8000`.

## Features

- Portfolio value, progress, status, and KPI health summaries
- Search and filters by status and vendor
- Contract detail views with scope, risks, milestones, and source notes
- Add, edit, duplicate, and delete project records
- Add and edit measurable KPIs for each project
- Track projects, open milestone tasks, coordinators, budgets, and contract references
- Upload contract documents to browser storage and download them from project details
- Browser persistence via `localStorage`
- JSON import/export for backup and handoff

## Data note

The initial portfolio was extracted from the supplied contracts and proposals. Records based only on proposals are explicitly identified in their source notes. Placeholder dates, progress values, and operational KPI actuals must be validated by the project owners before formal reporting.

The application is client-side only. Project data remains in the current browser profile, and uploaded contract files remain in that browser's IndexedDB. Use **Export data** to create a JSON backup; file binaries are not included in that export.

## Live team sharing

Static hosting can share the dashboard interface, but it cannot synchronize edits between users. For live collaboration, connect the application to a shared database and object store such as Supabase:

1. Host `index.html` on GitHub Pages, Azure Static Web Apps, or an internal web server.
2. Store project records in a secured shared database instead of `localStorage`.
3. Store contract files in private object storage with authenticated access.
4. Add organizational sign-in and role-based permissions.
5. Keep an audit log of edits, uploads, and management decisions.

Until a shared backend is configured, teammates can exchange JSON exports through **Export data** and **Import data**, but those updates are not real-time.
