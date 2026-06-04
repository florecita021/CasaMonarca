using Microsoft.AspNetCore.Mvc;
using CasaMonarcaApp.Models;
using System.Diagnostics;
using System.Text;
using Newtonsoft.Json;
using ClosedXML.Excel;

namespace CasaMonarcaApp.Controllers;

// 🎯 Creamos el ViewModel AQUÍ, afuera de la clase para que no cause errores de compilación
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
        // 1. Traemos los datos limpios de Supabase
        var logs = GetLogsFromDatabase();

        // 2. Procesamos datos para las gráficas existentes de los últimos 7 días
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

        // 🔥 3. NUEVO: Agrupamos por voluntario usando la propiedad nativa HoursVolunteered
        var rankingVoluntarios = logs
            .GroupBy(l => l.FullName)
            .Select(grupo => new RankingVoluntarioViewModel
            {
                NombreCompleto = grupo.Key,
                HorasTotales = Math.Round(grupo.Sum(l => l.HoursVolunteered), 2)
            })
            .OrderByDescending(r => r.HorasTotales) // De mayor a menor
            .ToList();

        // Pasamos el ranking procesado a la vista mediante el ViewBag
        ViewBag.RankingVoluntarios = rankingVoluntarios;

        return View(logs);
    }
}