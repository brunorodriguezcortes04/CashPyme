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

## Base de datos (Supabase / PostgreSQL)

El esquema vive en `database/` y se ejecuta a mano en el **SQL Editor** de Supabase, en este orden:

1. `cashpyme_modelo_datos_v2.sql` — tablas, vistas, funciones y roles base.
2. `cashpyme_supabase_seguridad.sql` — cierra la API pública de Supabase (RLS).

La API .NET no crea tablas (sin `EnsureCreated` ni migraciones de EF). Cada persona configura su
cadena de conexión (Session pooler de Supabase → **Connect**) con user secrets, nunca en el repo:

```bash
cd backend
dotnet user-secrets set "ConnectionStrings:Default" "Host=aws-0-REGION.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.PROJECT_REF;Password=...;SSL Mode=Require;Trust Server Certificate=true"
dotnet user-secrets set "Jwt:Key" "<clave aleatoria de 32+ caracteres>"
```

En producción usar las variables de entorno `ConnectionStrings__Default` y `Jwt__Key`.
