# EDE Contract Portfolio

A self-contained project and contract management dashboard for the Emirates Drug Establishment.

## Open the dashboard

Do **not** open the GitHub Raw link in the browser. GitHub serves HTML as plain text, so the dashboard will not run there.

1. Download `DT_Projects_Dashboard_SharePoint.html`.
2. Right-click the file → **Open with Google Chrome** (or drag it into Chrome).
3. SharePoint usually downloads HTML instead of running it. Download the file, then open it in Chrome.

If GitHub Pages is enabled for this repository, the live page is:

https://sherine257.github.io/ede-dashboard/

## Run locally

Open `index.html` in a modern browser, or serve the repository:

```bash
python3 -m http.server 8000
```

Then visit `http://localhost:8000`.

## SharePoint (team sharing)

Edits are saved in the local browser. To share updates with the team, use **Export** / **Import**, or replace the SharePoint HTML file with your latest copy.

Contract file attachments stay in the originating browser and are not included in JSON exports.

## Features

- Portfolio Dashboard, Project Register, and Delivery Tracking
- Search, filters, KPI cards, and chart drill-downs
- Add, edit, duplicate, and delete project records
- JSON and CSV export, JSON import
- Print / PDF

## Data note

The portfolio was built from DT project records, contracts, and proposals. Placeholder dates, progress values, and operational KPI actuals must be validated by the project owners before formal reporting.
