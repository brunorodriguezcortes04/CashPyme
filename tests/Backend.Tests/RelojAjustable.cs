namespace Backend.Tests;

/// <summary>Reloj que la prueba mueve a mano, para reglas que dependen del paso del tiempo.</summary>
public sealed class RelojAjustable(DateTimeOffset inicio) : TimeProvider
{
    private DateTimeOffset _ahora = inicio;

    public override DateTimeOffset GetUtcNow() => _ahora;

    public void Avanzar(TimeSpan cuanto) => _ahora += cuanto;
}
