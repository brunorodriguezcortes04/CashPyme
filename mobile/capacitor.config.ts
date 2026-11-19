import type { CapacitorConfig } from '@capacitor/cli';

/**
 * Con CAP_BACKEND_LOCAL=1 (build "emulador", ver README) la app habla con el backend local por
 * http. Solo para probar: el build de producción usa la API desplegada con https.
 */
const backendLocal = process.env['CAP_BACKEND_LOCAL'] === '1';

/** App nativa (Android e iOS) que envuelve el build de Angular. */
const config: CapacitorConfig = {
  appId: 'cl.cashpyme.app',
  appName: 'CashPyme',
  webDir: 'dist/mobile/browser',
  // La app es siempre oscura: íconos claros en las barras del sistema aunque el teléfono esté en
  // modo claro (por defecto siguen el tema del sistema).
  plugins: { SystemBars: { style: 'DARK' } },
  ...(backendLocal ? { server: { cleartext: true }, android: { allowMixedContent: true } } : {})
};

export default config;
