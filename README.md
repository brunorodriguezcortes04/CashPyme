# CashPyme

Gestión financiera para PyMEs.

## Estructura

- `backend/` — API en ASP.NET Core (C#, .NET 10)
- `frontend/` — Aplicación Angular

## Arranque en desarrollo

### Backend

```bash
cd backend
dotnet run
```

La API queda disponible en `https://localhost:7016` (y `http://localhost:5131`).

### Frontend

```bash
cd frontend
npm start
```

La app queda disponible en `http://localhost:4200`. Las llamadas a `/api/*` se
redirigen automáticamente al backend (ver `frontend/proxy.conf.json`).
