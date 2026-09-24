import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';

/** Deja el RUT como 12345678-9: sin puntos, con guión y dígito verificador en mayúscula. */
export function normalizarRut(rut: string): string {
  const limpio = rut.replace(/[^0-9kK]/g, '').toUpperCase();
  if (limpio.length <= 1) {
    return limpio;
  }

  const cuerpo = limpio.slice(0, -1);
  const dv = limpio.slice(-1);
  return `${cuerpo}-${dv}`;
}

function calcularDigitoVerificador(cuerpo: string): string {
  let suma = 0;
  let multiplo = 2;

  for (let i = cuerpo.length - 1; i >= 0; i--) {
    suma += Number(cuerpo[i]) * multiplo;
    multiplo = multiplo === 7 ? 2 : multiplo + 1;
  }

  const resto = 11 - (suma % 11);
  if (resto === 11) return '0';
  if (resto === 10) return 'K';
  return String(resto);
}

/** Valida el dígito verificador. El campo es opcional: vacío se considera válido. */
export function validadorRut(): ValidatorFn {
  return (control: AbstractControl): ValidationErrors | null => {
    const valor = (control.value ?? '').trim();
    if (!valor) {
      return null;
    }

    const normalizado = normalizarRut(valor);
    const [cuerpo, dv] = normalizado.split('-');

    if (!cuerpo || !dv || !/^\d{7,8}$/.test(cuerpo)) {
      return { rut: true };
    }

    return calcularDigitoVerificador(cuerpo) === dv ? null : { rut: true };
  };
}
