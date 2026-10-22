/**
 * Build de producción (la app instalada en el teléfono): no hay proxy, así que la API va con su
 * URL completa. El backend tiene que aceptar el origen de Capacitor en Cors:AllowedOrigins.
 */
export const environment = {
  apiUrl: 'https://cashpyme-backend.onrender.com/api'
};
