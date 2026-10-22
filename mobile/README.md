# CashPyme móvil

App para Android e iOS hecha con **Ionic + Angular** y empaquetada con **Capacitor**. Usa los
mismos endpoints de la API que la web (`/auth/login`, `/dashboard/caja`, `/movimientos`) y no
calcula nada: el saldo y los movimientos son los mismos que se ven en la web.

Pantallas:

- **Login**: misma cuenta que la web. El token se guarda en el teléfono (Capacitor Preferences).
- **Resumen**: saldo total en caja, ingresos y egresos del mes y los últimos movimientos. Tirar
  hacia abajo actualiza.

## Ver la app mientras se desarrolla

Con el backend corriendo en `http://localhost:5131` (igual que para la web):

```bash
cd mobile
npm install
npm start            # http://localhost:8100
```

- **En el navegador del PC**: abre `http://localhost:8100` y activa la vista de celular de las
  herramientas de desarrollo (F12 → ícono de dispositivo).
- **En tu propio celular, sin instalar nada**: `npm run start:red`, conecta el celular a la
  misma red WiFi y abre `http://<IP-del-PC>:8100` (la IP sale con `ipconfig`). Si Windows pregunta
  por el firewall, permite el acceso en redes privadas.

`/api` va al backend local por `proxy.conf.json`.

## App nativa

El build de producción llama a la API desplegada (`src/environments/environment.ts`); el backend
acepta los orígenes de la app (`https://localhost` en Android, `capacitor://localhost` en iOS) en
`Cors:AllowedOrigins`.

- **Android**: instala [Android Studio](https://developer.android.com/studio) (trae el SDK, el
  emulador y Java). Luego `npm run android`: compila, copia el build a `android/` y abre el
  proyecto en Android Studio, desde donde se corre en un emulador o en un teléfono por USB.
- **iOS**: necesita un Mac con Xcode. En el Mac, `npm run ios` abre el proyecto `ios/` en Xcode.

**Gradle JDK**: el proyecto Android que genera Capacitor usa Gradle 8.14, que no corre con Java 25
(el que trae Android Studio desde 2026). En Android Studio: Settings → Build, Execution,
Deployment → Build Tools → Gradle → **Gradle JDK** → un JDK 21 ("Download JDK…" si no hay).

**Emulador**: necesita aceleración por hardware. En Android Studio, Settings → Languages &
Frameworks → Android SDK → SDK Tools → "Android Emulator hypervisor driver (installer)".

### Probar contra el backend local

`npm run android:emulador` compila con `src/environments/environment.emulador.ts`
(`http://localhost:5131/api`, http permitido solo en ese build) y abre la app en el emulador o
teléfono conectado. Para que el `localhost` del dispositivo llegue al backend del PC:

```bash
adb reverse tcp:5131 tcp:5131
```

## Pruebas

```bash
npm test
```
