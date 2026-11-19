using System.Collections.Concurrent;

namespace Backend.Security;

/// <summary>
/// Bloqueo progresivo del login por cuenta. El límite por IP (RateLimits.Login) no alcanza:
/// con muchas IPs se puede seguir probando contraseñas contra el mismo correo. Acá la clave es
/// el correo, así que da igual desde dónde lleguen los intentos.
///
/// Vive en memoria y no en la base: si la API se reinicia, la cuenta queda desbloqueada, y
/// eso es aceptable porque el límite por IP sigue en pie y un reinicio no lo provoca quien
/// ataca. A cambio no suma una escritura en la base por cada intento fallido.
///
/// Cuenta también los correos que no existen: si solo se bloquearan las cuentas reales, el
/// mensaje de bloqueo diría qué correos están registrados.
/// </summary>
public class IntentosLoginService(TimeProvider reloj)
{
    /// <summary>Fallos seguidos que se toleran antes del primer bloqueo.</summary>
    public const int FallosAntesDeBloquear = 5;

    /// <summary>Un fallo deja de contar si pasa este tiempo sin otro fallo.</summary>
    public static readonly TimeSpan OlvidoDeFallos = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Duración de cada bloqueo sucesivo: 1, 5 y 15 minutos, y de ahí en adelante 15. Con el
    /// tope en 15 un atacante prueba unas 4 contraseñas por hora y por cuenta, y la persona
    /// legítima que se equivocó no queda fuera más de un cuarto de hora.
    /// </summary>
    private static readonly TimeSpan[] Bloqueos =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15)
    ];

    /// <summary>
    /// Tope de cuentas recordadas, para que inventar correos sin fin no sea una forma de
    /// llenar la memoria del servidor. Al llegar se descartan las vencidas y, si no alcanza,
    /// todas: perder el registro solo desbloquea antes de tiempo, el límite por IP sigue.
    /// </summary>
    public const int MaximoCuentas = 50_000;

    private readonly ConcurrentDictionary<string, Registro> _registros = new(StringComparer.Ordinal);

    public int CuentasRecordadas => _registros.Count;

    /// <summary>Hasta cuándo está bloqueada la cuenta, o null si se puede intentar.</summary>
    public DateTimeOffset? BloqueadaHasta(string emailNormalizado)
    {
        if (!_registros.TryGetValue(emailNormalizado, out var registro))
        {
            return null;
        }

        lock (registro)
        {
            var ahora = reloj.GetUtcNow();
            return registro.BloqueadaHasta > ahora ? registro.BloqueadaHasta : null;
        }
    }

    public void RegistrarFallo(string emailNormalizado)
    {
        if (_registros.Count >= MaximoCuentas)
        {
            Podar();
        }

        var registro = _registros.GetOrAdd(emailNormalizado, _ => new Registro());
        lock (registro)
        {
            var ahora = reloj.GetUtcNow();
            if (ahora - registro.UltimoFallo > OlvidoDeFallos)
            {
                registro.Fallos = 0;
                registro.Bloqueos = 0;
            }

            registro.Fallos++;
            registro.UltimoFallo = ahora;

            if (registro.Fallos >= FallosAntesDeBloquear)
            {
                var duracion = Bloqueos[Math.Min(registro.Bloqueos, Bloqueos.Length - 1)];
                registro.BloqueadaHasta = ahora + duracion;
                registro.Bloqueos++;
                // Después de un bloqueo cada fallo vuelve a bloquear, con la duración siguiente.
                registro.Fallos = FallosAntesDeBloquear - 1;
            }
        }
    }

    public void RegistrarExito(string emailNormalizado) => _registros.TryRemove(emailNormalizado, out _);

    private void Podar()
    {
        var ahora = reloj.GetUtcNow();
        foreach (var (email, registro) in _registros)
        {
            bool vencido;
            lock (registro)
            {
                vencido = registro.BloqueadaHasta <= ahora && ahora - registro.UltimoFallo > OlvidoDeFallos;
            }
            if (vencido)
            {
                _registros.TryRemove(email, out _);
            }
        }

        if (_registros.Count >= MaximoCuentas)
        {
            _registros.Clear();
        }
    }

    private sealed class Registro
    {
        public int Fallos;
        public int Bloqueos;
        public DateTimeOffset UltimoFallo = DateTimeOffset.MinValue;
        public DateTimeOffset BloqueadaHasta = DateTimeOffset.MinValue;
    }
}
