using Backend.Dominio;

namespace Backend.Exceptions;

public class RangoReporteRequeridoException()
    : ApiException(StatusCodes.Status400BadRequest, "Indica la fecha desde y la fecha hasta del reporte.");

public class RangoReporteMuyLargoException()
    : ApiException(
        StatusCodes.Status400BadRequest,
        $"El reporte puede abarcar hasta {ReglasReporte.MaximoDias} días. Acorta el rango de fechas.");
