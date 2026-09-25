#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
#if MAUI
using Microsoft.Maui.Storage;
#endif

namespace DocumentationOfWorkingHours.Services
{
    // Modelle
    public class Day
    {
        public DateTime Date { get; set; }
        public string? Notes { get; set; }

        public Day() { }
        public Day(DateTime date, string? notes = null)
        {
            Date = date;
            Notes = notes;
        }
    }

    public class Week
    {
        public List<Day> Days { get; set; } = new();

        public Week() { }
        public Week(IEnumerable<Day> days) => Days = new List<Day>(days);
    }

    public class Month
    {
        public string Name { get; set; } = string.Empty;
        public List<Week> Weeks { get; set; } = new();

        public Month() { }
        public Month(string name, IEnumerable<Week>? weeks = null)
        {
            Name = name;
            Weeks = weeks is null ? new List<Week>() : new List<Week>(weeks);
        }
    }

    public class Calendar
    {
        public List<Month> Months { get; set; } = new();

        public Calendar() { }
        public Calendar(IEnumerable<Month> months) => Months = new List<Month>(months);
    }

    // Persistenz
    public static class CalendarStore
    {
        private static JsonSerializerOptions Options => new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        // Default filename used for calendar storage. Uses build configuration suffix so Debug/Release use different files.
        private static string DefaultFileName
        {
            get
            {
#if DEBUG
                return "calendar.debug.json";
#else
                return "calendar.json";
#endif
            }
        }

        public static string GetDefaultPath(string? fileName = null)
        {
#if MAUI
            return Path.Combine(FileSystem.AppDataDirectory, fileName ?? DefaultFileName);
#else
#pragma warning disable CA2000 // Dispose objects before losing scope - simple path creation
            var folder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var appFolder = Path.Combine(folder, "DocumentationOfWorkingHours");
            if (!Directory.Exists(appFolder)) Directory.CreateDirectory(appFolder);
            return Path.Combine(appFolder, fileName ?? DefaultFileName);
#pragma warning restore CA2000
#endif
        }

        public static async Task SaveAsync(Calendar calendar, string path, CancellationToken ct = default)
        {
            var json = JsonSerializer.Serialize(calendar, Options);
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(path, json, ct).ConfigureAwait(false);
        }

        public static async Task<Calendar?> LoadAsync(string path, CancellationToken ct = default)
        {
            if (!File.Exists(path)) return null;
            var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            try
            {
                return JsonSerializer.Deserialize<Calendar>(json, Options);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
