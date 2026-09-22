# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users

Dueños y dueñas de pymes sin formación financiera. Llevan la caja del negocio por su cuenta, dedican poco tiempo a revisarla y necesitan claridad simple, sin jerga contable. Su trabajo con la app: saber cuánta caja tienen hoy y anticipar cuánta tendrán en las próximas semanas para decidir con tranquilidad.

## Product Purpose

CashPyme es gestión financiera para PyMEs. Muestra el estado de la caja y su evolución futura para que el dueño deje de adivinar y tome decisiones antes de que la situación sea urgente. Éxito: el usuario entiende su caja de un vistazo y actúa a tiempo.

## Positioning

El foco es anticipar, no solo registrar: proyección de caja a 30/60/90 días con escenarios, en lugar de un resumen de lo que ya pasó.

## Operating Context

Interfaz en español. El copy y los formatos actuales sugieren Chile (pesos con punto como separador de miles, "SpA" en placeholders); país y moneda no están confirmados.

## Capabilities and Constraints

- Stack existente: frontend Angular (`frontend/`, rutas `/`, `/login`, `/registro`) y backend ASP.NET Core .NET 10 con SQLite (`backend/`).
- Implementado hoy: landing pública, registro (nombre del negocio, correo, contraseña) e inicio de sesión con JWT.
- No implementado aún: el dashboard, la proyección real, las alertas, los movimientos y la conexión de cuentas/documentos. Lo que la landing muestra de ellos es maqueta.
- Sin decidir: país y moneda definitivos, planes y precios, integraciones (bancos, facturación), cómo entran los datos al sistema.

## Evidence on Hand

Todo el contenido de marketing y de la vista previa del dashboard es maqueta. No existen clientes, testimonios, métricas ni integraciones reales.

- No presentar como hecho: "más de 500 pymes", los tres testimonios (Camila Torres, Metas Reyes, Daniela Fuentes), las cifras del dashboard ($4.820.000, +12,43 %, etc.) ni la "conexión de cuentas".
- Existe `Claude outputs/CashPyme_Backlog.xlsx` (backlog del producto), no revisado durante init.

## Product Principles

- Anticipar antes que registrar: la proyección de caja es el centro del producto.
- Lenguaje llano: un dueño sin formación financiera debe entender cada cifra sin traducirla.
- Claridad de un vistazo: lo importante primero, sin sobrecargar de datos.
- Honestidad con los datos: no inventar cifras, clientes ni pruebas sociales como si fueran reales.

## Accessibility & Inclusion

No se estableció un estándar específico. Los formularios existentes ya usan etiquetas, `aria-invalid` y mensajes con `role="alert"`; mantener ese nivel como mínimo.
