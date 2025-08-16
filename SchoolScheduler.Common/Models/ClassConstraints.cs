using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SchoolScheduler.Common.Models
{
    // Vincolo: Ore specifiche per una classe in un giorno
    public class ClassDailyHoursConstraint : Constraint
    {
        public string ClassName { get; set; } = string.Empty;
        public DayOfWeek Day { get; set; }
        public int Hours { get; set; }

        public ClassDailyHoursConstraint()
        {
            Type = ConstraintType.ClassDailyHours;
            Priority = ConstraintPriority.Mandatory; // Di solito è obbligatorio
        }

        public override bool Validate(ScheduleContext context)
        {
            var classSlots = context.CurrentSchedule
                .Where(s => s.ClassName == ClassName && s.Day == Day)
                .Count();

            return classSlots == Hours;
        }

        public override string GetHumanReadableDescription()
        {
            var dayName = Day switch
            {
                DayOfWeek.Monday => "Lunedì",
                DayOfWeek.Tuesday => "Martedì",
                DayOfWeek.Wednesday => "Mercoledì",
                DayOfWeek.Thursday => "Giovedì",
                DayOfWeek.Friday => "Venerdì",
                DayOfWeek.Saturday => "Sabato",
                _ => Day.ToString()
            };

            return $"Classe {ClassName}: {dayName} esattamente {Hours} ore";
        }
    }

    // Vincolo: Distribuzione settimanale ore per classe
    public class ClassWeeklyDistributionConstraint : Constraint
    {
        public string ClassName { get; set; } = string.Empty;
        public Dictionary<DayOfWeek, int> DailyHours { get; set; } = new Dictionary<DayOfWeek, int>();

        public ClassWeeklyDistributionConstraint()
        {
            Type = ConstraintType.Custom;
            Priority = ConstraintPriority.High;
        }

        public override bool Validate(ScheduleContext context)
        {
            foreach (var kvp in DailyHours)
            {
                var actualHours = context.CurrentSchedule
                    .Where(s => s.ClassName == ClassName && s.Day == kvp.Key)
                    .Count();

                if (actualHours != kvp.Value)
                    return false;
            }

            return true;
        }

        public override string GetHumanReadableDescription()
        {
            var parts = DailyHours.Select(kvp => $"{GetItalianDay(kvp.Key)}={kvp.Value}h");
            return $"Classe {ClassName}: {string.Join(", ", parts)}";
        }

        private string GetItalianDay(DayOfWeek day)
        {
            return day switch
            {
                DayOfWeek.Monday => "Lun",
                DayOfWeek.Tuesday => "Mar",
                DayOfWeek.Wednesday => "Mer",
                DayOfWeek.Thursday => "Gio",
                DayOfWeek.Friday => "Ven",
                DayOfWeek.Saturday => "Sab",
                _ => day.ToString().Substring(0, 3)
            };
        }
    }

    // Vincolo: Classe entra sempre alla prima ora (o a una specifica ora)
    public class ClassStartTimeConstraint : Constraint
    {
        public string ClassName { get; set; } = string.Empty;
        public int StartHour { get; set; } = 1; // 1 = prima ora, 2 = seconda ora, etc.
        public DayOfWeek? SpecificDay { get; set; } // null = tutti i giorni

        public ClassStartTimeConstraint()
        {
            Type = ConstraintType.Custom;
            Priority = ConstraintPriority.High;
        }

        public override bool Validate(ScheduleContext context)
        {
            var days = SpecificDay.HasValue
    ? new List<DayOfWeek> { SpecificDay.Value }
    : context.Configuration.GetActiveDays();

            foreach (var day in days)
            {
                var classSlots = context.CurrentSchedule
                    .Where(s => s.ClassName == ClassName && s.Day == day)
                    .OrderBy(s => s.Hour)
                    .ToList();

                if (classSlots.Any() && classSlots.First().Hour != StartHour)
                    return false;
            }

            return true;
        }

        public override string GetHumanReadableDescription()
        {
            var dayPart = SpecificDay.HasValue
                ? $" il {GetItalianDay(SpecificDay.Value)}"
                : " sempre";

            return $"Classe {ClassName}: entra alla {StartHour}° ora{dayPart}";
        }

        private string GetItalianDay(DayOfWeek day)
        {
            return day switch
            {
                DayOfWeek.Monday => "Lunedì",
                DayOfWeek.Tuesday => "Martedì",
                DayOfWeek.Wednesday => "Mercoledì",
                DayOfWeek.Thursday => "Giovedì",
                DayOfWeek.Friday => "Venerdì",
                DayOfWeek.Saturday => "Sabato",
                _ => day.ToString()
            };
        }
    }
}

