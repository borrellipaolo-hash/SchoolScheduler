using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Linq.Expressions;

namespace SchoolScheduler.Common.Models
{
    // Enumerazioni per priorità e tipo di vincolo
    public enum ConstraintPriority
    {
        Mandatory = 1000,   // Inviolabile
        High = 100,         // Molto importante
        Medium = 10,        // Importante
        Low = 1,           // Preferibile
        Wish = 0           // Desiderata
    }

    public enum ConstraintType
    {
        TeacherAvailability,
        TeacherMaxDailyHours,
        TeacherMinDailyHours,
        TeacherMaxWeeklyGaps,
        TeacherMinWeeklyGaps,
        TeacherDayOff,
        TeacherEntryTime,
        TeacherExitTime,
        ClassMaxConsecutive,
        ClassDailyHours,
        RoomAvailability,
        SubjectDistribution,
        Custom
    }

    // Classe base per tutti i vincoli
    public abstract class Constraint
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public ConstraintType Type { get; set; }
        public ConstraintPriority Priority { get; set; } = ConstraintPriority.Medium;
        public bool IsActive { get; set; } = true;

        // Metodo astratto per validare il vincolo
        public abstract bool Validate(ScheduleContext context);

        // Metodo per ottenere una descrizione human-readable
        public abstract string GetHumanReadableDescription();
    }

    // Context per la validazione (conterrà l'orario in costruzione)
    public class ScheduleContext
    {
        public List<ScheduleSlot> CurrentSchedule { get; set; } = new List<ScheduleSlot>();
        public List<Teacher> Teachers { get; set; } = new List<Teacher>();
        public List<SchoolClass> Classes { get; set; } = new List<SchoolClass>();
        public ScheduleConfiguration Configuration { get; set; } = new ScheduleConfiguration();
    }

    // Rappresenta uno slot nell'orario
    public class ScheduleSlot
    {
        public DayOfWeek Day { get; set; }
        public int Hour { get; set; }  // 1, 2, 3, etc.
        public string ClassName { get; set; } = string.Empty;
        public string TeacherName { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string Room { get; set; } = string.Empty;
        public string ArticulationGroup { get; set; } = string.Empty;
    }
}