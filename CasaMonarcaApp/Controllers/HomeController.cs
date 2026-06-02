using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using CasaMonarcaApp.Models;
using ClosedXML.Excel;

namespace CasaMonarcaApp.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    
    // Explicitly initializing the list immediately to guarantee it is NEVER null
    private static readonly List<VolunteerLog> _volunteerLogs = new List<VolunteerLog>();

    public HomeController(ILogger<HomeController> logger)
    {
        _logger = logger;
    }

    public IActionResult Index()
    {
        return View();
    }

[HttpPost]
    public IActionResult SubmitRegistration(VolunteerLog newLog)
    {
        if (newLog == null)
        {
            TempData["ErrorMessage"] = "Error processing submission. Object was empty.";
            return RedirectToAction("Index");
        }

        // NEW LOGIC: Check if the times actually bound correctly from HTML before trying to save
        if (!newLog.TimeOfEntry.HasValue || !newLog.TimeOfLeaving.HasValue)
        {
            TempData["ErrorMessage"] = "Please ensure both Time of Entry and Time of Leaving are filled out correctly.";
            return RedirectToAction("Index");
        }

        // Add to our list safely
        _volunteerLogs.Add(newLog);

        TempData["SuccessMessage"] = $"Thank you, {newLog.FullName}! Your {newLog.HoursVolunteered:F2} hours have been registered.";
        return RedirectToAction("Index");
    }

    // Keep this exactly the same
    public static List<VolunteerLog> GetLogs() 
    {
        return _volunteerLogs ?? new List<VolunteerLog>();
    }

    // Keep this exactly the same
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    // Displays the Coordinator Dashboard page
// Displays the Coordinator Dashboard page with real dynamic data
 // Displays the Coordinator Dashboard page with real dynamic data
    public IActionResult Dashboard()
    {
        var logs = GetLogs();

        // 1. Get the last 7 calendar days starting from today backward
        var last7Days = Enumerable.Range(0, 7)
            .Select(i => DateTime.Today.AddDays(-i))
            .OrderBy(d => d)
            .ToList();

        // 2. Format dates as labels for the charts (e.g., "Jun 02")
        var chartLabels = last7Days.Select(d => d.ToString("MMM dd")).ToList();

        // 3. Calculate actual total hours worked per day from real logs
        var hoursPerDay = last7Days.Select(date => 
            logs.Where(l => l.Date.Date == date.Date).Sum(l => l.HoursVolunteered)
        ).ToList();

        // 4. Calculate actual distinct headcount per day from real logs
        var attendancePerDay = last7Days.Select(date => 
            logs.Where(l => l.Date.Date == date.Date).Select(l => l.IdentificationNumber).Distinct().Count()
        ).ToList();

        // FIXED: Using standard built-in System.Text.Json instead of Newtonsoft
        ViewBag.ChartLabels = System.Text.Json.JsonSerializer.Serialize(chartLabels);
        ViewBag.HoursData = System.Text.Json.JsonSerializer.Serialize(hoursPerDay);
        ViewBag.AttendanceData = System.Text.Json.JsonSerializer.Serialize(attendancePerDay);

        return View(logs);
    }
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }


    // Generates and downloads a real Excel file containing all logged data
    public IActionResult ExportToExcel()
    {
        var data = GetLogs();

        // Create a blank Excel Workbook layout
        using (var workbook = new XLWorkbook())
        {
            var worksheet = workbook.Worksheets.Add("Casa Monarca Logs");

            // 1. Create styled Header Row columns
            worksheet.Cell(1, 1).Value = "Full Name";
            worksheet.Cell(1, 2).Value = "Identification Number";
            worksheet.Cell(1, 3).Value = "Date Recorded";
            worksheet.Cell(1, 4).Value = "Time of Entry";
            worksheet.Cell(1, 5).Value = "Time of Leaving";
            worksheet.Cell(1, 6).Value = "Total Hours";

            // Make headers bold for a clean look
            worksheet.Row(1).Style.Font.Bold = true;

            // 2. Populate the worksheet rows with your real data log list
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

            // Auto-adjust layout sizes so columns widen perfectly to match text lengths
            worksheet.Columns().AdjustToContents();

            // 3. Compile the workbook into a memory stream data packet to download
            using (var stream = new MemoryStream())
            {
                workbook.SaveAs(stream);
                var content = stream.ToArray();

                // Stream the file back to the browser download manager
                return File(
                    content, 
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", 
                    "CasaMonarca_VolunteerHistory.xlsx"
                );
            }
        }
    }
}
