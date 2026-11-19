import { Pipe, PipeTransform } from '@angular/core';

const formato = new Intl.NumberFormat('es-CL', { style: 'currency', currency: 'CLP', maximumFractionDigits: 0 });

/** Pesos chilenos con punto de miles: $445.000. Solo formato, igual que en la web. */
@Pipe({ name: 'clp' })
export class ClpPipe implements PipeTransform {
  transform(monto: number | null | undefined): string {
    return monto === null || monto === undefined ? '' : formato.format(monto);
  }
}
