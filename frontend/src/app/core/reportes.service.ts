import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ArchivoDescargado, descargarArchivo } from './descargas';

@Injectable({ providedIn: 'root' })
export class ReportesService {
  private readonly http = inject(HttpClient);

  /**
   * Reporte PDF del período (saldo, movimientos y cuentas pendientes). Lo arma entero el
   * backend; acá solo se descarga. Las fechas son obligatorias y el backend las valida.
   */
  descargarPdf(desde: string, hasta: string): Observable<ArchivoDescargado> {
    return descargarArchivo(
      this.http,
      '/api/reportes/pdf',
      new HttpParams().set('desde', desde).set('hasta', hasta),
      'reporte-caja.pdf'
    );
  }
}
