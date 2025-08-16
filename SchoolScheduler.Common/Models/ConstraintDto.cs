using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;

namespace SchoolScheduler.Common.Models
{
    // Data Transfer Object per serializzazione vincoli
    public class ConstraintDto
    {
        public string ConstraintType { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public ConstraintPriority Priority { get; set; }
        public bool IsActive { get; set; } = true;
        public Dictionary<string, object> Properties { get; set; } = new Dictionary<string, object>();

        // Conversione da Constraint a DTO
        public static ConstraintDto FromConstraint(Constraint constraint)
        {
            var dto = new ConstraintDto
            {
                ConstraintType = constraint.GetType().Name,
                Name = constraint.Name,
                Description = constraint.Description,
                Priority = constraint.Priority,
                IsActive = constraint.IsActive
            };

            // Salva proprietà specifiche in base al tipo
            switch (constraint)
            {
                case ClassDailyHoursConstraint cdhc:
                    dto.Properties["ClassName"] = cdhc.ClassName;
                    dto.Properties["Day"] = cdhc.Day.ToString();
                    dto.Properties["Hours"] = cdhc.Hours;
                    break;

                case ClassWeeklyDistributionConstraint cwdc:
                    dto.Properties["ClassName"] = cwdc.ClassName;
                    dto.Properties["DailyHours"] = JsonSerializer.Serialize(cwdc.DailyHours);
                    break;

                case ClassStartTimeConstraint cstc:
                    dto.Properties["ClassName"] = cstc.ClassName;
                    dto.Properties["StartHour"] = cstc.StartHour;
                    if (cstc.SpecificDay.HasValue)
                        dto.Properties["SpecificDay"] = cstc.SpecificDay.Value.ToString();
                    break;

                case TeacherAvailabilityConstraint tac:
                    dto.Properties["TeacherName"] = tac.TeacherName;
                    dto.Properties["UnavailableSlots"] = JsonSerializer.Serialize(tac.UnavailableSlots);
                    break;

                case TeacherMaxDailyHoursConstraint tmdhc:
                    dto.Properties["TeacherName"] = tmdhc.TeacherName;
                    dto.Properties["MaxHours"] = tmdhc.MaxHours;
                    break;

                case TeacherMaxWeeklyGapsConstraint tmwgc:
                    dto.Properties["TeacherName"] = tmwgc.TeacherName;
                    dto.Properties["MaxGaps"] = tmwgc.MaxGaps;
                    break;

                case TeacherDayOffConstraint tdoc:
                    dto.Properties["TeacherName"] = tdoc.TeacherName;
                    dto.Properties["DayOff"] = tdoc.DayOff.ToString();
                    break;
            }

            return dto;
        }

        // Conversione da DTO a Constraint
        public Constraint ToConstraint()
        {
            Constraint constraint = ConstraintType switch
            {
                nameof(ClassDailyHoursConstraint) => CreateClassDailyHoursConstraint(),
                nameof(ClassWeeklyDistributionConstraint) => CreateClassWeeklyDistributionConstraint(),
                nameof(ClassStartTimeConstraint) => CreateClassStartTimeConstraint(),
                nameof(TeacherAvailabilityConstraint) => CreateTeacherAvailabilityConstraint(),
                nameof(TeacherMaxDailyHoursConstraint) => CreateTeacherMaxDailyHoursConstraint(),
                nameof(TeacherMaxWeeklyGapsConstraint) => CreateTeacherMaxWeeklyGapsConstraint(),
                nameof(TeacherDayOffConstraint) => CreateTeacherDayOffConstraint(),
                _ => throw new NotSupportedException($"Constraint type {ConstraintType} not supported")
            };

            constraint.Name = Name;
            constraint.Description = Description;
            constraint.Priority = Priority;
            constraint.IsActive = IsActive;

            return constraint;
        }

        private ClassDailyHoursConstraint CreateClassDailyHoursConstraint()
        {
            var constraint = new ClassDailyHoursConstraint();

            if (Properties.TryGetValue("ClassName", out var className))
                constraint.ClassName = className.ToString() ?? string.Empty;

            if (Properties.TryGetValue("Day", out var day))
            {
                if (Enum.TryParse<DayOfWeek>(day.ToString(), out var dayOfWeek))
                    constraint.Day = dayOfWeek;
            }

            if (Properties.TryGetValue("Hours", out var hours))
            {
                if (hours is JsonElement je)
                    constraint.Hours = je.GetInt32();
                else
                    constraint.Hours = Convert.ToInt32(hours);
            }

            return constraint;
        }

        private ClassWeeklyDistributionConstraint CreateClassWeeklyDistributionConstraint()
        {
            var constraint = new ClassWeeklyDistributionConstraint();

            if (Properties.TryGetValue("ClassName", out var className))
                constraint.ClassName = className.ToString() ?? string.Empty;

            if (Properties.TryGetValue("DailyHours", out var dailyHours))
            {
                var dict = JsonSerializer.Deserialize<Dictionary<DayOfWeek, int>>(
                    dailyHours.ToString() ?? "{}");
                constraint.DailyHours = dict ?? new Dictionary<DayOfWeek, int>();
            }

            return constraint;
        }

        private ClassStartTimeConstraint CreateClassStartTimeConstraint()
        {
            var constraint = new ClassStartTimeConstraint();

            if (Properties.TryGetValue("ClassName", out var className))
                constraint.ClassName = className.ToString() ?? string.Empty;

            if (Properties.TryGetValue("StartHour", out var startHour))
            {
                if (startHour is JsonElement je)
                    constraint.StartHour = je.GetInt32();
                else
                    constraint.StartHour = Convert.ToInt32(startHour);
            }

            if (Properties.TryGetValue("SpecificDay", out var specificDay))
            {
                if (Enum.TryParse<DayOfWeek>(specificDay.ToString(), out var day))
                    constraint.SpecificDay = day;
            }

            return constraint;
        }

        private TeacherAvailabilityConstraint CreateTeacherAvailabilityConstraint()
        {
            var constraint = new TeacherAvailabilityConstraint();

            if (Properties.TryGetValue("TeacherName", out var teacherName))
                constraint.TeacherName = teacherName.ToString() ?? string.Empty;

            if (Properties.TryGetValue("UnavailableSlots", out var slotsJson))
            {
                var slots = JsonSerializer.Deserialize<List<TimeSlot>>(slotsJson.ToString() ?? "[]");
                constraint.UnavailableSlots = slots ?? new List<TimeSlot>();
            }

            return constraint;
        }

        private TeacherMaxDailyHoursConstraint CreateTeacherMaxDailyHoursConstraint()
        {
            var constraint = new TeacherMaxDailyHoursConstraint();

            if (Properties.TryGetValue("TeacherName", out var teacherName))
                constraint.TeacherName = teacherName.ToString() ?? string.Empty;

            if (Properties.TryGetValue("MaxHours", out var maxHours))
            {
                if (maxHours is JsonElement je)
                    constraint.MaxHours = je.GetInt32();
                else
                    constraint.MaxHours = Convert.ToInt32(maxHours);
            }

            return constraint;
        }

        private TeacherMaxWeeklyGapsConstraint CreateTeacherMaxWeeklyGapsConstraint()
        {
            var constraint = new TeacherMaxWeeklyGapsConstraint();

            if (Properties.TryGetValue("TeacherName", out var teacherName))
                constraint.TeacherName = teacherName.ToString() ?? string.Empty;

            if (Properties.TryGetValue("MaxGaps", out var maxGaps))
            {
                if (maxGaps is JsonElement je)
                    constraint.MaxGaps = je.GetInt32();
                else
                    constraint.MaxGaps = Convert.ToInt32(maxGaps);
            }

            return constraint;
        }

        private TeacherDayOffConstraint CreateTeacherDayOffConstraint()
        {
            var constraint = new TeacherDayOffConstraint();

            if (Properties.TryGetValue("TeacherName", out var teacherName))
                constraint.TeacherName = teacherName.ToString() ?? string.Empty;

            if (Properties.TryGetValue("DayOff", out var dayOff))
            {
                if (Enum.TryParse<DayOfWeek>(dayOff.ToString(), out var day))
                    constraint.DayOff = day;
            }

            return constraint;
        }
    }

    // Helper class per gestire salvataggio/caricamento
    public static class ConstraintsPersistence
    {
        public static void SaveConstraints(List<Constraint> constraints, string filePath)
        {
            var dtos = constraints.Select(c => ConstraintDto.FromConstraint(c)).ToList();
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter() }
            };
            var json = JsonSerializer.Serialize(dtos, options);
            File.WriteAllText(filePath, json);
        }

        public static List<Constraint> LoadConstraints(string filePath)
        {
            if (!File.Exists(filePath))
                return new List<Constraint>();

            try
            {
                var json = File.ReadAllText(filePath);
                var options = new JsonSerializerOptions
                {
                    Converters = { new JsonStringEnumConverter() }
                };
                var dtos = JsonSerializer.Deserialize<List<ConstraintDto>>(json, options);

                if (dtos == null)
                    return new List<Constraint>();

                var constraints = new List<Constraint>();
                foreach (var dto in dtos)
                {
                    try
                    {
                        constraints.Add(dto.ToConstraint());
                    }
                    catch (Exception ex)
                    {
                        // Log errore ma continua con gli altri vincoli
                        Console.WriteLine($"Errore caricamento vincolo {dto.Name}: {ex.Message}");
                    }
                }

                return constraints;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Errore caricamento file vincoli: {ex.Message}");
                return new List<Constraint>();
            }
        }
    }
}
