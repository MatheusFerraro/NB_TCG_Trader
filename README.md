# NB TCG Trader

A localized trading-card marketplace and digital collection organizer: bulk-import
a spreadsheet of cards into a visual digital "binder", mark cards for sale, and
connect with local buyers.

> See [CLAUDE.md](CLAUDE.md) for the full project context and [BACKLOG.md](BACKLOG.md)
> for the roadmap.

## Tech stack

- **Backend:** ASP.NET Core Web API (.NET 9, C#) — modular monolith, vertical slices
- **Database:** PostgreSQL (EF Core + Npgsql)
- **Frontend:** React + TypeScript (Vite)
- **Tests:** xUnit + Shouldly

## Repository layout

```
src/NbTcgTrader.Api/     # ASP.NET Core Web API (feature slices under Features/)
tests/NbTcgTrader.Tests/ # xUnit + Shouldly test project
web/                     # React + TypeScript frontend (Vite)
```

## Prerequisites

- [.NET SDK 9.0](https://dotnet.microsoft.com/download) (pinned via `global.json`)
- [Node.js 20+](https://nodejs.org/) and npm

## Run instructions

### Backend API

```bash
dotnet restore
dotnet run --project src/NbTcgTrader.Api
```

The API serves a health check at `/health` and, in Development, the OpenAPI
document at `/openapi/v1.json`.

### Tests

```bash
dotnet test
```

### Frontend

```bash
cd web
npm install
npm run dev
```

The Vite dev server prints its local URL (default `http://localhost:5173`).
