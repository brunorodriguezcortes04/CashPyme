using System.ComponentModel.DataAnnotations;

namespace Backend.Dominio;

/// <summary>
/// Tipo de cuenta con los textos de su pantalla. Igual que TipoMovimientoResponse para ingresos
/// y egresos: cuentas por cobrar y por pagar son UNA sola pantalla en el front, y todo lo que
/// cambia entre las dos (textos, qué terceros sirven, con qué flujo se pagan) viene de acá.
/// </summary>
public record TipoCuentaDocumentoResponse(
    string Valor,
    string Titulo,
    string Subtitulo,
    string TituloNueva,
    string TituloEditar,
    string BotonRegistrar,
    string TituloListado,
    string MensajeVacio,
    string MensajeRegistrada,
    string MensajeAnulada,
    string PreguntaAnular,
    string EfectoAnular,
    string Tercero,
    string SeleccionarTercero,
    string SinTerceros,
    string AyudaNota,
    string EjemploDescripcion,
    string ColumnaAplicado,
    string Accion,
    string TituloAplicar,
    string AplicadosPrevios,
    string MensajeAplicado,
    string MensajeQuitado,
    string Flujo,
    string SeleccionarMovimiento,
    string SinMovimientos,
    string RutaMovimientos,
    string NombrePantallaMovimientos,
    /// <summary>Ruta del backoffice de esta pantalla (la misma que publica la tabla pantalla).</summary>
    string Ruta,
    /// <summary>Con cuántos días de plazo el formulario propone la fecha de vencimiento.</summary>
    int DiasVencimientoPorDefecto);

/// <summary>
/// Una cuenta por cobrar y una por pagar son la misma tabla (documento_financiero) con distinto
/// tipo_cuenta, igual que un ingreso y un egreso comparten movimiento_financiero.
/// Debe coincidir con ck_doc_tipo_cuenta en la base.
/// </summary>
public static class TiposCuentaDocumento
{
    public const string PorCobrar = "por_cobrar";
    public const string PorPagar = "por_pagar";

    /// <summary>Plazo habitual de una factura en Chile: deja el formulario listo para el caso normal.</summary>
    public const int PlazoHabitualDias = 30;

    public static readonly IReadOnlyList<TipoCuentaDocumentoResponse> Todos =
    [
        new(
            PorCobrar,
            Titulo: "Cuentas por cobrar",
            Subtitulo: "Lo que te deben tus clientes. Registra la factura con su vencimiento, aplícale los cobros que vayan entrando y corrígela con notas de crédito o débito si cambia.",
            TituloNueva: "Nueva cuenta por cobrar",
            TituloEditar: "Editar cuenta por cobrar",
            BotonRegistrar: "Registrar cuenta por cobrar",
            TituloListado: "Cuentas por cobrar registradas",
            MensajeVacio: "Todavía no registras ninguna cuenta por cobrar.",
            MensajeRegistrada: "Cuenta por cobrar registrada.",
            MensajeAnulada: "Cuenta por cobrar anulada.",
            PreguntaAnular: "¿Anular esta cuenta por cobrar?",
            EfectoAnular: "Deja de contar como plata por cobrar.",
            Tercero: "Cliente",
            SeleccionarTercero: "Selecciona un cliente",
            SinTerceros: "Todavía no tienes clientes registrados. Registra un cliente antes de cargar una cuenta por cobrar.",
            AyudaNota: "Una nota de crédito baja lo que el cliente debe; una de débito lo sube.",
            EjemploDescripcion: "Ej: Venta a 30 días",
            ColumnaAplicado: "Cobrado",
            Accion: "Cobrar",
            TituloAplicar: "Registrar cobro",
            AplicadosPrevios: "Cobros ya aplicados",
            MensajeAplicado: "Cobro registrado.",
            MensajeQuitado: "Cobro quitado.",
            Flujo: TiposMovimiento.Ingreso,
            SeleccionarMovimiento: "Selecciona el ingreso que cubre este documento",
            SinMovimientos: "No hay ingresos con monto libre. Registra primero el ingreso y vuelve acá a aplicarlo.",
            RutaMovimientos: "movimientos/ingreso",
            NombrePantallaMovimientos: "Ingresos",
            Ruta: "cuentas-por-cobrar",
            DiasVencimientoPorDefecto: PlazoHabitualDias),
        new(
            PorPagar,
            Titulo: "Cuentas por pagar",
            Subtitulo: "Lo que le debes a tus proveedores. Registra la factura con su vencimiento, aplícale los pagos que vayas haciendo y corrígela con notas de crédito o débito si cambia.",
            TituloNueva: "Nueva cuenta por pagar",
            TituloEditar: "Editar cuenta por pagar",
            BotonRegistrar: "Registrar cuenta por pagar",
            TituloListado: "Cuentas por pagar registradas",
            MensajeVacio: "Todavía no registras ninguna cuenta por pagar.",
            MensajeRegistrada: "Cuenta por pagar registrada.",
            MensajeAnulada: "Cuenta por pagar anulada.",
            PreguntaAnular: "¿Anular esta cuenta por pagar?",
            EfectoAnular: "Deja de contar como plata por pagar.",
            Tercero: "Proveedor",
            SeleccionarTercero: "Selecciona un proveedor",
            SinTerceros: "Todavía no tienes proveedores registrados. Registra un proveedor antes de cargar una cuenta por pagar.",
            AyudaNota: "Una nota de crédito baja lo que le debes al proveedor; una de débito lo sube.",
            EjemploDescripcion: "Ej: Compra a 30 días",
            ColumnaAplicado: "Pagado",
            Accion: "Pagar",
            TituloAplicar: "Registrar pago",
            AplicadosPrevios: "Pagos ya aplicados",
            MensajeAplicado: "Pago registrado.",
            MensajeQuitado: "Pago quitado.",
            Flujo: TiposMovimiento.Egreso,
            SeleccionarMovimiento: "Selecciona el egreso que cubre este documento",
            SinMovimientos: "No hay egresos con monto libre. Registra primero el egreso y vuelve acá a aplicarlo.",
            RutaMovimientos: "movimientos/egreso",
            NombrePantallaMovimientos: "Egresos",
            Ruta: "cuentas-por-pagar",
            DiasVencimientoPorDefecto: PlazoHabitualDias)
    ];

    public static bool EsValido(string? valor) => Todos.Any(t => t.Valor == valor);

    /// <summary>
    /// Una cuenta por cobrar termina siendo un ingreso de caja y una por pagar un egreso. Es lo
    /// mismo que calcula la columna generada tipo_flujo, y decide contra qué categorías y con qué
    /// movimientos se valida y se paga el documento.
    /// </summary>
    public static string FlujoDe(string tipoCuenta)
        => tipoCuenta == PorCobrar ? TiposMovimiento.Ingreso : TiposMovimiento.Egreso;

    /// <summary>
    /// Contra quién se puede registrar cada tipo de cuenta: se le cobra a un cliente y se le paga a
    /// un proveedor. Un tercero 'ambos' sirve para las dos. Es la única definición de la regla: la
    /// usan la validación al guardar y el listado de terceros que ofrece el formulario.
    /// </summary>
    public static IReadOnlyList<string> TiposTerceroQueSirven(string tipoCuenta)
        => tipoCuenta == PorCobrar ? ["cliente", "ambos"] : ["proveedor", "ambos"];

    public static TipoCuentaDocumentoResponse Pantalla(string tipoCuenta)
        => Todos.Single(t => t.Valor == tipoCuenta);
}

public sealed class TipoCuentaDocumentoValidoAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) => TiposCuentaDocumento.EsValido(value as string);

    public override string FormatErrorMessage(string name)
        => $"El tipo de cuenta debe ser uno de: {string.Join(", ", TiposCuentaDocumento.Todos.Select(o => o.Valor))}.";
}
