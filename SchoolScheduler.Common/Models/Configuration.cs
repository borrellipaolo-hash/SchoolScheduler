using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;

namespace SchoolScheduler.Common.Models
{
    public class ScheduleConfiguration
    {
        // Configurazioni generali
        public int SchoolDays { get; set; } = 5;  // 5 o 6 giorni
        public DayOfWeek FirstDayOfWeek { get; set; } = DayOfWeek.Monday;
        public DayOfWeek? ExcludedDay { get; set; } = DayOfWeek.Saturday;  // Se 5 giorni, quale escluso

        // Orario
        public TimeSpan DefaultStartTime { get; set; } = new TimeSpan(8, 0, 0);  // 8:00
        public int LessonDurationMinutes { get; set; } = 60;
        public int MaxDailyHours { get; set; } = 6;  // Massimo ore al giorno per una classe
        public int MinDailyHours { get; set; } = 4;  // Minimo ore al giorno per una classe

        // Intervalli (per futura implementazione)
        public List<BreakConfiguration> Breaks { get; set; } = new List<BreakConfiguration>();

        // Override per classe specifica
        public Dictionary<string, ClassConfiguration> ClassOverrides { get; set; } = new Dictionary<string, ClassConfiguration>();

        // Metodi per salvare/caricare configurazione
        public void SaveToFile(string filePath)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter() }
            };
            var json = JsonSerializer.Serialize(this, options);
            File.WriteAllText(filePath, json);
        }

        public static ScheduleConfiguration LoadFromFile(string filePath)
        {
            if (!File.Exists(filePath))
                return new ScheduleConfiguration();

            var json = File.ReadAllText(filePath);
            var options = new JsonSerializerOptions
            {
                Converters = { new JsonStringEnumConverter() }
            };
            return JsonSerializer.Deserialize<ScheduleConfiguration>(json, options) ?? new ScheduleConfiguration();
        }

        public List<DayOfWeek> GetActiveDays()
        {
            var days = new List<DayOfWeek>();
            var allDays = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                                  DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday };

            foreach (var day in allDays)
            {
                if (SchoolDays == 6 || day != ExcludedDay)
                    days.Add(day);
            }

            return days;
        }
    }

    public class ClassConfiguration
    {
        public string ClassName { get; set; } = string.Empty;
        public TimeSpan? StartTime { get; set; }  // Override orario inizio
        public Dictionary<DayOfWeek, int> DailyHours { get; set; } = new Dictionary<DayOfWeek, int>();
    }

    public class BreakConfiguration
    {
        public int AfterHour { get; set; }  // Dopo quale ora
        public int DurationMinutes { get; set; }  // Durata in minuti
        public string Description { get; set; } = string.Empty;  // "Ricreazione", "Mensa", etc.
    }
}