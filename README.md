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
- Browser persistence via `localStorage`
- JSON export for backup and reporting

## Data note

The initial portfolio was extracted from the supplied contracts and proposals. Records based only on proposals are explicitly identified in their source notes. Placeholder dates, progress values, and operational KPI actuals must be validated by the project owners before formal reporting.

The application is client-side only. Data remains in the current browser profile; use **Export data** to create backups.
