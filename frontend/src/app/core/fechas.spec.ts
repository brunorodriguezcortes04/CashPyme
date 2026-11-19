import { aIso, enDias, hoy } from './fechas';

describe('fechas', () => {
  beforeEach(() => vi.useFakeTimers({ toFake: ['Date'] }));

  afterEach(() => vi.useRealTimers());

  it('escribe la fecha como AAAA-MM-DD, con ceros a la izquierda', () => {
    expect(aIso(new Date(2026, 2, 5))).toBe('2026-03-05');
  });

  // toISOString() pasaría a UTC: a esta hora, en Chile, ya daría el día 6.
  it('hoy es el día local, aunque en UTC ya sea el siguiente', () => {
    vi.setSystemTime(new Date(2026, 0, 5, 23, 30));

    expect(hoy()).toBe('2026-01-05');
  });

  it('suma días pasando de mes y de año', () => {
    vi.setSystemTime(new Date(2026, 11, 20, 10, 0));

    expect(enDias(0)).toBe('2026-12-20');
    expect(enDias(11)).toBe('2026-12-31');
    expect(enDias(45)).toBe('2027-02-03');
  });
});
