# EDE Contract Portfolio

A self-contained project and contract management dashboard for the Emirates Drug Establishment.

## Run

Open `index.html` in a modern browser, or serve the repository:

```bash
python3 -m http.server 8000
```

Then visit `http://localhost:8000`.

## SharePoint (team sharing)

Edits are **not** stored inside SharePoint automatically. The browser saves a private copy on the machine that made the change.

To share updates with the team:

1. Make your project edits as usual.
2. Click **Save HTML for SharePoint**. This downloads `DT_Projects_Dashboard_SharePoint.html` with the current project data baked into the file.
3. Upload that file to SharePoint and **replace** the previous HTML file (same name, overwrite).
4. Ask teammates to open the new file (or refresh). They will see the file version, not an old copy from their browser.

If someone still sees old data, they should click **Reset to file data**. That discards their private browser copy and reloads the HTML they opened.

Contract file attachments stay in the originating browser and are not included in the SharePoint HTML.

## Features

- Portfolio Dashboard, Project Register, and Delivery Tracking
- Search, filters, KPI cards, and chart drill-downs
- Add, edit, duplicate, and delete project records
- JSON and CSV export, JSON import
- Print / PDF
- Save HTML for SharePoint so teammates see the same project data

## Data note

The portfolio was built from DT project records, contracts, and proposals. Placeholder dates, progress values, and operational KPI actuals must be validated by the project owners before formal reporting.
