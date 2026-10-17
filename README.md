# EDE Personal Medicine Import — AI Assistant

Conversational, AI-first permit service. There is no applicant form. Customers talk to the assistant, attach documents in chat, and confirm a summary before submission.

## Run

```bash
export OPENAI_API_KEY=sk-...   # GPT-4o extraction + optional Whisper
export DOTNET_ROOT=$HOME/.dotnet
export PATH="$HOME/.dotnet:$PATH"
dotnet test
dotnet run --project src/EdePermit.Api
```

Open:

- Applicant chat: http://127.0.0.1:5088/
- Officer portal: http://127.0.0.1:5088/officer.html  
  Demo password: `EdeOfficer!2026`

## Required flow

AI Chat → collect all information in one checklist → extract → identify gaps → validate medicines against `data/ALL_REG_PACKS.xlsx` → apply business rules in code → ask only for missing/invalid items → summary → submit → officer dashboard.

## Medicine source of truth

`data/ALL_REG_PACKS.xlsx` (`Pack Name`, `Act Ing`, `Disp Mode`, `Form`, `Sub Form`, `Strength`, `Alt ID`).

The model must not invent pack names, ingredients, forms, or dispensing modes. Unmatched medicines are **No record found**, dispensing mode **N/A**, and officer review (BR-09).

## Architecture

- GPT-4o extracts and phrases chat only (`OpenAI:Model=gpt-4o`).
- `BusinessRulesEngine` applies BR-01–BR-21 in C#.
- LiteDB stores conversations, requests, validation results, status, documents, and audit history (`data/ede-permit.db`).
- Voice: browser speech recognition, plus Whisper when an API key is configured.
- Officer portal: queue, full chat, extracted JSON, documents, register matches, rule findings, decisions, analytics.

Replace the Excel file and restart the API to refresh the register.
