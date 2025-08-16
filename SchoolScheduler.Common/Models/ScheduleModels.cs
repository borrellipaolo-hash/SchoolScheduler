using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SchoolScheduler.Common.Models
{
    // Rappresenta l'orario completo generato
    public class GeneratedSchedule
    {
        public List<ScheduleSlot> Slots { get; set; } = new List<ScheduleSlot>();
        public DateTime GeneratedAt { get; set; } = DateTime.Now;
        public TimeSpan GenerationTime { get; set; }
        public ScheduleStatistics Statistics { get; set; } = new ScheduleStatistics();
        public bool IsValid { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();

        // Helper per ottenere l'orario di una classe
        public List<ScheduleSlot> GetClassSchedule(string className)
        {
            return Slots.Where(s => s.ClassName == className)
                       .OrderBy(s => s.Day)
                       .ThenBy(s => s.Hour)
                       .ToList();
        }

        // Helper per ottenere l'orario di un docente
        public List<ScheduleSlot> GetTeacherSchedule(string teacherName)
        {
            return Slots.Where(s => s.TeacherName == teacherName)
                       .OrderBy(s => s.Day)
                       .ThenBy(s => s.Hour)
                       .ToList();
        }

        // Crea una matrice orario per una classe
        public ScheduleSlot[,] GetClassMatrix(string className, ScheduleConfiguration config)
        {
            var days = config.GetActiveDays();
            var matrix = new ScheduleSlot[config.MaxDailyHours, days.Count];

            var classSlots = GetClassSchedule(className);
            foreach (var slot in classSlots)
            {
                int dayIndex = days.IndexOf(slot.Day);
                if (dayIndex >= 0 && slot.Hour > 0 && slot.Hour <= config.MaxDailyHours)
                {
                    matrix[slot.Hour - 1, dayIndex] = slot;
                }
            }

            return matrix;
        }
    }

    // Statistiche sull'orario generato
    public class ScheduleStatistics
    {
        public int TotalSlots { get; set; }
        public int TotalTeacherGaps { get; set; }  // Totale ore buche docenti
        public Dictionary<string, int> TeacherGaps { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, int> TeacherDailyMax { get; set; } = new Dictionary<string, int>();
        public int ConstraintsSatisfied { get; set; }
        public int ConstraintsViolated { get; set; }
        public List<string> ViolatedConstraints { get; set; } = new List<string>();
        public double OptimizationScore { get; set; }  // 0-100

        public void CalculateStatistics(GeneratedSchedule schedule, List<Teacher> teachers)
        {
            TotalSlots = schedule.Slots.Count;

            // Calcola ore buche per ogni docente
            foreach (var teacher in teachers)
            {
                var teacherSlots = schedule.GetTeacherSchedule(teacher.FullName);
                int gaps = CalculateTeacherGaps(teacherSlots);
                TeacherGaps[teacher.FullName] = gaps;
                TotalTeacherGaps += gaps;

                // Calcola max ore giornaliere
                var maxDaily = teacherSlots.GroupBy(s => s.Day)
                                          .Select(g => g.Count())
                                          .DefaultIfEmpty(0)
                                          .Max();
                TeacherDailyMax[teacher.FullName] = maxDaily;
            }

            // Calcola score di ottimizzazione (esempio semplificato)
            if (teachers.Count > 0)
            {
                double avgGaps = (double)TotalTeacherGaps / teachers.Count;
                OptimizationScore = Math.Max(0, 100 - (avgGaps * 10)); // Penalizza 10 punti per ora buca media
            }
        }

        private int CalculateTeacherGaps(List<ScheduleSlot> teacherSlots)
        {
            if (teacherSlots.Count <= 1) return 0;

            int totalGaps = 0;
            var byDay = teacherSlots.GroupBy(s => s.Day);

            foreach (var dayGroup in byDay)
            {
                var hours = dayGroup.Select(s => s.Hour).OrderBy(h => h).ToList();
                for (int i = 1; i < hours.Count; i++)
                {
                    // Se tra due ore consecutive c'è un buco
                    totalGaps += (hours[i] - hours[i - 1] - 1);
                }
            }

            return totalGaps;
        }
    }
}
