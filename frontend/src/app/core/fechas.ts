/**
 * Fecha de calendario (sin hora) como la usan los <input type="date"> y la API: AAAA-MM-DD.
 * Se arma con el día local y no con toISOString(), que pasa a UTC y de noche en Chile ya
 * devuelve el día siguiente.
 */
export function aIso(fecha: Date): string {
  const mes = String(fecha.getMonth() + 1).padStart(2, '0');
  const dia = String(fecha.getDate()).padStart(2, '0');
  return `${fecha.getFullYear()}-${mes}-${dia}`;
}

/** El día de hoy según el reloj y la zona horaria del navegador. */
export function hoy(): string {
  return aIso(new Date());
}

/** El día que cae la cantidad de días indicada después de hoy. */
export function enDias(dias: number): string {
  const fecha = new Date();
  fecha.setDate(fecha.getDate() + dias);
  return aIso(fecha);
}
