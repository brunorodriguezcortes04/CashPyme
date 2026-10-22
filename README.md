# CashPyme

Gestión financiera para PyMEs.

## Estructura

- `backend/` — API en ASP.NET Core (C#, .NET 10)
- `frontend/` — Aplicación Angular
- `mobile/` — App móvil (Ionic + Capacitor, Android e iOS). Ver `mobile/README.md`
- `database/` — Esquema de la base y migraciones
- `tests/Backend.Tests/` — Pruebas del backend (xUnit)

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

1. `cashpyme_modelo_datos_v2.sql` — tablas, restricciones, índices y datos base (roles y pantallas).
2. `cashpyme_supabase_seguridad.sql` — cierra la API pública de Supabase (RLS).

La base no tiene lógica: ni funciones, ni procedimientos, ni triggers, ni vistas. Saldos, estados,
validaciones, auditoría, alertas y proyección de caja viven en la API (`backend/Dominio` y
`backend/Services`). Lo que sí queda en la base son las restricciones declarativas (CHECK, FK,
UNIQUE), que protegen los datos aunque alguien escriba sin pasar por la API.

Una base que ya existía se pone al día corriendo las migraciones pendientes de `database/migraciones/`
en orden.

La API .NET no crea tablas (sin `EnsureCreated` ni migraciones de EF). Cada persona configura su
cadena de conexión (Session pooler de Supabase → **Connect**) con user secrets, nunca en el repo:

```bash
cd backend
dotnet user-secrets set "ConnectionStrings:Default" "Host=aws-0-REGION.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.PROJECT_REF;Password=...;SSL Mode=Require;Trust Server Certificate=true"
dotnet user-secrets set "Jwt:Key" "<clave aleatoria de 32+ caracteres>"
```

En producción usar las variables de entorno `ConnectionStrings__Default` y `Jwt__Key`.

## Pruebas

```bash
dotnet test                                    # backend: dominio + integración
dotnet test --filter "Tipo!=Integracion"       # backend: solo dominio (segundos)
cd frontend && npx ng test --watch=false       # frontend
```

Las pruebas de integración usan la misma cadena de conexión que la API (user secrets). Crean un
esquema propio `prueba_api_…` en esa base, instalan `cashpyme_modelo_datos_v2.sql` y lo borran al
terminar: nunca tocan `public`. Sin cadena de conexión se saltan. Contra Supabase tardan unos minutos.

## Correos electrónicos (Resend)

La API envía estos correos:

| Correo | Cuándo |
| --- | --- |
| Confirma tu correo | Al registrarse, o con "Reenviar enlace de confirmación" en **Mi cuenta** |
| Restablece tu contraseña | Desde "¿Olvidaste tu contraseña?" en el login (enlace de un solo uso, 60 min) |
| Tu contraseña cambió | Después de cambiar o restablecer la contraseña (aviso de seguridad) |
| Alertas | Cuando `fn_generar_alertas()` crea alertas nuevas (vencimientos, saldo bajo, caja negativa proyectada) |
| Resumen semanal | Los lunes desde las 08:00 (hora de la empresa), a Administradores y Contadores |

### Configuración

1. Ejecutar `database/migraciones/008_notificaciones_email.sql` en el SQL Editor de Supabase (una sola vez).
2. Crear una cuenta en [resend.com](https://resend.com), generar una API key y guardarla con user secrets:

```bash
cd backend
dotnet user-secrets set "Email:ResendApiKey" "re_xxxxxxxx"
```

Sin API key **no se envía nada**: cada correo se escribe en la consola de `dotnet run`, con su enlace.
Sirve para desarrollar sin cuenta de Resend.

**Remitente:** mientras no haya un dominio verificado en Resend, el remitente es
`CashPyme <onboarding@resend.dev>` y Resend **solo entrega a la dirección dueña de la cuenta de Resend**.
Para enviar a cualquier persona hay que verificar un dominio propio en Resend (registros DNS) y cambiar
`Email:From` en `appsettings.json` (por ejemplo `CashPyme <notificaciones@tudominio.cl>`).

### Alertas y resumen semanal

Los envía una tarea de fondo dentro de la API (`NotificacionesWorker`), cada `Notificaciones:IntervaloMinutos`.
Viene **apagada** (`Notificaciones:Habilitado = false`) porque el equipo comparte la misma base: si cada
integrante la enciende, cada uno enviaría las alertas. Se enciende en un solo lugar:

```bash
dotnet user-secrets set "Notificaciones:Habilitado" "true"
```

Para probar sin esperar al worker ni al lunes (solo en Development):

```bash
curl -X POST "http://localhost:5131/api/dev/notificaciones?idEmpresa=1"
```

En producción: variables de entorno `Email__ResendApiKey`, `Email__From`, `App__FrontendUrl`
(URL pública del frontend, se usa en los enlaces) y `Notificaciones__Habilitado`.
