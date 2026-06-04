using Microsoft.AspNetCore.Mvc;
using CasaMonarcaApp.Models;
using System.Diagnostics;
using System.Text;
using Newtonsoft.Json;
using ClosedXML.Excel;

namespace CasaMonarcaApp.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    // 🌐 URL de la API Web (Puerto 443 HTTPS - Jamás bloqueado por proveedores de internet)
    private const string SupabaseUrl = "https://mncapvylwfyddwemnvdm.supabase.co/rest/v1/volunteerlogs";
    private const string SupabaseApiKey = "sb_publishable_tzG61fb4uWBw1-A7yIDolQ_ghlsHwuy";

    public HomeController(ILogger<HomeController> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    public IActionResult Index()
    {
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> SubmitRegistration(VolunteerLog newLog, string verificationCode)
    {
        if (newLog == null)
        {
            TempData["ErrorMessage"] = "Error processing submission. Object was empty.";
            return RedirectToAction("Index");
        }

        // 🔐 VALIDACIÓN DEL CÓDIGO DE SEGURIDAD
        const string CodigoCorrecto = "ACM26";
        if (string.IsNullOrEmpty(verificationCode) || verificationCode.Trim() != CodigoCorrecto)
        {
            TempData["ErrorMessage"] = "Código de verificación incorrecto o no proporcionado. Las horas no fueron registradas.";
            return RedirectToAction("Index");
        }

        // Validación de tiempos
        if (!newLog.TimeOfEntry.HasValue || !newLog.TimeOfLeaving.HasValue)
        {
            TempData["ErrorMessage"] = "Please ensure both Time of Entry and Time of Leaving are filled out correctly.";
            return RedirectToAction("Index");
        }

        try
        {
            // Preparar los datos en el formato JSON exacto para Supabase
            var payload = new
            {
                fullname = newLog.FullName,
                identificationnumber = newLog.IdentificationNumber,
                date = newLog.Date.ToString("yyyy-MM-dd"),
                timeofentry = newLog.TimeOfEntry.Value.ToString(@"hh\:mm\:ss"),
                timeofleaving = newLog.TimeOfLeaving.Value.ToString(@"hh\:mm\:ss")
            };

            var json = JsonConvert.SerializeObject(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            // Crear la petición HTTP web segura
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("apikey", SupabaseApiKey);
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {SupabaseApiKey}");

            var response = await client.PostAsync(SupabaseUrl, content);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                throw new Exception($"Supabase API Error: {response.StatusCode} - {errorBody}");
            }

            TempData["SuccessMessage"] = $"Thank you, {newLog.FullName}! Your {newLog.HoursVolunteered:F2} hours have been registered safely.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving to Supabase via API");
            TempData["ErrorMessage"] = $"Error de red: {ex.Message}"; 
        }

        return RedirectToAction("Index");
    }

    public List<VolunteerLog> GetLogsFromDatabase()
    {
        var logs = new List<VolunteerLog>();

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("apikey", SupabaseApiKey);
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {SupabaseApiKey}");

            // 🔍 Solicitar los datos ordenados por fecha de forma descendente usando filtros URL nativos
            var response = client.GetAsync($"{SupabaseUrl}?select=*&order=date.desc").Result;

            if (response.IsSuccessStatusCode)
            {
                var jsonResult = response.Content.ReadAsStringAsync().Result;
                dynamic? rawLogs = JsonConvert.DeserializeObject(jsonResult);

                if (rawLogs != null)
                {
                    foreach (var item in rawLogs)
                    {
                        logs.Add(new VolunteerLog
                        {
                            FullName = (string?)item.fullname ?? "Unknown",
                            IdentificationNumber = (string?)item.identificationnumber ?? "N/A",
                            Date = item.date != null ? DateTime.Parse((string)item.date) : DateTime.Today,
                            TimeOfEntry = item.timeofentry != null ? TimeSpan.Parse((string)item.timeofentry) : TimeSpan.Zero,
                            TimeOfLeaving = item.timeofleaving != null ? TimeSpan.Parse((string)item.timeofleaving) : TimeSpan.Zero
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading from Supabase via API");
        }

        return logs;
    }

    public IActionResult Dashboard()
    {
        var logs = GetLogsFromDatabase();

        var last7Days = Enumerable.Range(0, 7)
            .Select(i => DateTime.Today.AddDays(-i))
            .OrderBy(d => d)
            .ToList();

        var chartLabels = last7Days.Select(d => d.ToString("MMM dd")).ToList();

        var hoursPerDay = last7Days.Select(date => 
            logs.Where(l => l.Date.Date == date.Date).Sum(l => l.HoursVolunteered)
        ).ToList();

        var attendancePerDay = last7Days.Select(date => 
            logs.Where(l => l.Date.Date == date.Date).Select(l => l.IdentificationNumber).Distinct().Count()
        ).ToList();

        ViewBag.ChartLabels = System.Text.Json.JsonSerializer.Serialize(chartLabels);
        ViewBag.HoursData = System.Text.Json.JsonSerializer.Serialize(hoursPerDay);
        ViewBag.AttendanceData = System.Text.Json.JsonSerializer.Serialize(attendancePerDay);

        return View(logs);
    }

    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }

    public IActionResult ExportToExcel()
    {
        var data = GetLogsFromDatabase();

        using (var workbook = new XLWorkbook())
        {
            var worksheet = workbook.Worksheets.Add("Casa Monarca Logs");

            worksheet.Cell(1, 1).Value = "Nombre Completo";
            worksheet.Cell(1, 2).Value = "Matricula";
            worksheet.Cell(1, 3).Value = "Fecha";
            worksheet.Cell(1, 4).Value = "Hora de Entrada";
            worksheet.Cell(1, 5).Value = "Hora de Salida";
            worksheet.Cell(1, 6).Value = "Horas Totales";

            worksheet.Row(1).Style.Font.Bold = true;

            int currentRow = 2;
            foreach (var log in data)
            {
                worksheet.Cell(currentRow, 1).Value = log.FullName;
                worksheet.Cell(currentRow, 2).Value = log.IdentificationNumber;
                worksheet.Cell(currentRow, 3).Value = log.Date.ToString("MM/dd/yyyy");
                worksheet.Cell(currentRow, 4).Value = log.TimeOfEntry?.ToString(@"hh\:mm") ?? "";
                worksheet.Cell(currentRow, 5).Value = log.TimeOfLeaving?.ToString(@"hh\:mm") ?? "";
                worksheet.Cell(currentRow, 6).Value = log.HoursVolunteered;
                currentRow++;
            }

            worksheet.Columns().AdjustToContents();

            using (var stream = new MemoryStream())
            {
                workbook.SaveAs(stream);
                var content = stream.ToArray();

                return File(
                    content, 
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", 
                    "CasaMonarca_VolunteerHistory.xlsx"
                );
            }
        }
    }
}