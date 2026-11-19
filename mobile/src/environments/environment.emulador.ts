/**
 * Build para probar en un emulador o teléfono contra el backend local: `adb reverse tcp:5131
 * tcp:5131` hace que el localhost del dispositivo llegue al del PC (ver README).
 */
export const environment = {
  apiUrl: 'http://localhost:5131/api'
};
