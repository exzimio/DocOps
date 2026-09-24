using System.Text;
using DocOps.Application.Reporting;
using DocOps.Domain.Identity;
using DocOps.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocOps.Api.Controllers;

/// <summary>
/// The operations dashboard.
///
/// Every figure returned here is a query against the same data the rest of the application works
/// on, restricted to what the caller may see. There is no store of precomputed dashboard numbers,
/// because a stored number is a number that can disagree with the thing it describes.
/// </summary>
[ApiController]
[Route("api/reports")]
[Authorize]
public sealed class ReportsController(IReportingService reportingService) : ControllerBase
{
    /// <summary>Indicators, series and breakdowns over the last <paramref name="days"/> days.</summary>
    [HttpGet("dashboard")]
    [HasPermission(Permissions.ReportsView)]
    [Produces("application/json")]
    [ProducesResponseType<DashboardDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<DashboardDto>> Dashboard(
        [FromQuery] int days = 30, CancellationToken cancellationToken = default) =>
        Ok(await reportingService.GetDashboardAsync(days, cancellationToken));

    /// <summary>The same figures as CSV. Exporting is a permission of its own.</summary>
    [HttpGet("dashboard.csv")]
    [HasPermission(Permissions.ReportsExport)]
    [Produces("text/csv")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Export([FromQuery] int days = 30, CancellationToken cancellationToken = default)
    {
        string csv = await reportingService.ExportCsvAsync(days, cancellationToken);

        // A BOM so that Excel opens it as UTF-8 instead of guessing the local codepage and turning
        // every accented department name into mojibake.
        byte[] bytes = [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv)];

        return File(bytes, "text/csv", $"docops-indicadores-{DateTime.UtcNow:yyyy-MM-dd}.csv");
    }
}
