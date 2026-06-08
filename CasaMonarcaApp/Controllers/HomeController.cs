using Microsoft.AspNetCore.Mvc;
using CasaMonarcaApp.Models;
using System.Diagnostics;
using System.Text;
using Newtonsoft.Json;
using ClosedXML.Excel;

// 📄 Directivas para la generación de PDF con QuestPDF
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace CasaMonarcaApp.Controllers;

// 🎯 ViewModel para agrupar las horas del ranking
public class RankingVoluntarioViewModel
{
    public string NombreCompleto { get; set; } = string.Empty;
    public double HorasTotales { get; set; }
}

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

            TempData["SuccessMessage"] = $"Gracias, {newLog.FullName}! Tus {newLog.HoursVolunteered:F2} horas han sido registradas de forma segura.";
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
                var rawLogs = JsonConvert.DeserializeObject<List<dynamic>>(jsonResult);

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

        // Procesamos datos para las gráficas de los últimos 7 días
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

        // Agrupamos los datos por voluntario para el ranking de mayor a menor
        var rankingVoluntarios = logs
            .GroupBy(l => l.FullName)
            .Select(grupo => new RankingVoluntarioViewModel
            {
                NombreCompleto = grupo.Key,
                HorasTotales = Math.Round(grupo.Sum(l => l.HoursVolunteered), 2)
            })
            .OrderByDescending(r => r.HorasTotales)
            .ToList();

        ViewBag.RankingVoluntarios = rankingVoluntarios;

        return View(logs);
    }

    [HttpPost]
    public async Task<IActionResult> ResetDatabase(string securityCode)
    {
        const string CodigoAutorizado = "BorrarACM26";

        if (string.IsNullOrEmpty(securityCode) || securityCode.Trim() != CodigoAutorizado)
        {
            TempData["ErrorMessage"] = "Código de seguridad incorrecto. No se realizó ninguna acción.";
            return RedirectToAction("Dashboard");
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("apikey", SupabaseApiKey);
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {SupabaseApiKey}");

            // Eliminación masiva en Supabase indicando que limpie las filas con ID válido
            var response = await client.DeleteAsync($"{SupabaseUrl}?identificationnumber=not.is.null");

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                throw new Exception($"Supabase Delete Error: {response.StatusCode} - {errorBody}");
            }

            TempData["SuccessMessage"] = "¡La base de datos ha sido reiniciada con éxito para el nuevo ciclo anual!";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al reiniciar la base de datos en Supabase");
            TempData["ErrorMessage"] = $"Error al borrar los datos: {ex.Message}";
        }

        return RedirectToAction("Dashboard");
    }

[HttpGet]
public IActionResult DownloadCertificate(string name, double hours)
{
    // 🔐 Licencia Comunitaria Obligatoria de QuestPDF
    QuestPDF.Settings.License = LicenseType.Community;

    if (string.IsNullOrEmpty(name))
    {
        TempData["ErrorMessage"] = "No se pudo generar la constancia: Nombre inválido.";
        return RedirectToAction("Dashboard");
    }

    // Creación de la estructura del PDF usando el motor fluido corregido
    var pdfMetadata = Document.Create(container =>
    {
        container.Page(page =>
        {
            page.Margin(40);
            page.Size(PageSizes.Letter.Landscape()); // Formato horizontal tipo diploma
            page.Content().Border(3).BorderColor("#e65c00").Padding(30).Column(column =>
            {
                // 1. ENCABEZADO
                column.Item().Row(row =>
                {
                    row.RelativeItem().Column(stack =>
                    {
                        // CORRECCIÓN 2 y 3: Se cambia TextColor por FontColor
                        stack.Item().Text("CASA MONARCA").FontSize(28).Bold().FontColor("#e65c00").FontFamily("Arial");
                        stack.Item().Text("AYUDA HUMANITARIA AL MIGRANTE, A.B.P.").FontSize(10).LetterSpacing(0.1f).FontColor(Colors.Grey.Darken2);
                    });
                    
                    // CORRECCIÓN 4: Se cambia TextColor por FontColor
                    row.ConstantItem(120).Text("🏆 TOP VOLUNTARIO").FontSize(10).Bold().FontColor("#ff7a21").AlignRight();
                });

                column.Item().PaddingTop(20).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);

                // 2. CUERPO DEL DIPLOMA
                // CORRECCIÓN 5 y 6: Se cambia TextColor por FontColor
                column.Item().PaddingTop(40).Text("RECONOCIMIENTO").FontSize(36).Bold().FontColor(Colors.Black).AlignCenter();
                column.Item().PaddingTop(15).Text("Otorgado con profunda gratitud a:").FontSize(14).Italic().FontColor(Colors.Grey.Darken3).AlignCenter();
                
                // Nombre del Voluntario Destacado
                // CORRECCIÓN 7: Se cambia TextColor por FontColor
                column.Item().PaddingTop(20).Text(name.ToUpper()).FontSize(26).Bold().FontColor("#e65c00").AlignCenter();
                
                // Texto descriptivo de las horas de servicio social
                column.Item().PaddingTop(25).Text(text =>
                {
                    text.Span("Por su invaluable apoyo, dedicación y compromiso humanitario reflejado en la realización de un total acumulado de ").FontSize(13).LineHeight(1.5f);
                    // CORRECCIÓN 8: Se cambia TextColor por FontColor en el Span interno
                    text.Span($"{hours:F2} hrs ").FontSize(14).Bold().FontColor("#e65c00");
                    text.Span("de servicio social voluntario durante el presente ciclo operativo, contribuyendo activamente al bienestar de nuestra comunidad.").FontSize(13);
                });

                // 3. PIE DE PÁGINA Y FIRMAS
                column.Item().AlignBottom().Row(row =>
                {
                    row.RelativeItem().Column(firma =>
                    {
                        firma.Item().LineHorizontal(1).LineColor(Colors.Black);
                        firma.Item().PaddingTop(5).Text("Coordinación de Voluntariado").FontSize(11).Bold().AlignCenter();
                        // CORRECCIÓN 9: Se cambia TextColor por FontColor
                        firma.Item().Text("Casa Monarca A.B.P.").FontSize(9).FontColor(Colors.Grey.Darken1).AlignCenter();
                    });

                    row.ConstantItem(80); // Separador en blanco

                    row.RelativeItem().Column(fecha =>
                    {
                        fecha.Item().PaddingTop(15).Text($"Expedido el: {DateTime.Today:dd/MM/yyyy}").FontSize(11).AlignRight();
                        // CORRECCIÓN 10: Se cambia TextColor por FontColor
                        fecha.Item().Text("Monterrey, Nuevo León, México").FontSize(9).FontColor(Colors.Grey.Darken1).AlignRight();
                    });
                });
            });
        });
    });

    // Procesa el render y descarga el flujo de bytes binarios directos
    byte[] pdfBytes = pdfMetadata.GeneratePdf();
    string fileName = $"Constancia_{name.Replace(" ", "_")}.pdf";
    
    return File(pdfBytes, "application/pdf", fileName);
}
}