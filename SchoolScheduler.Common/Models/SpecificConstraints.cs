namespace SchoolScheduler.Common.Models
{
    // Vincolo: Docente non disponibile in certi slot
    public class TeacherAvailabilityConstraint : Constraint
    {
        public string TeacherName { get; set; } = string.Empty;
        public List<TimeSlot> UnavailableSlots { get; set; } = new List<TimeSlot>();

        public TeacherAvailabilityConstraint()
        {
            Type = ConstraintType.TeacherAvailability;
            Priority = ConstraintPriority.Mandatory;
        }

        public override bool Validate(ScheduleContext context)
        {
            var teacherSlots = context.CurrentSchedule
                .Where(s => s.TeacherName == TeacherName)
                .ToList();

            foreach (var slot in teacherSlots)
            {
                if (UnavailableSlots.Any(u => u.Day == slot.Day && u.Hour == slot.Hour))
                    return false;
            }

            return true;
        }

        public override string GetHumanReadableDescription()
        {
            return $"{TeacherName} non disponibile: {string.Join(", ", UnavailableSlots.Select(s => $"{s.Day} {s.Hour}°ora"))}";
        }
    }

    // Vincolo: Massimo ore giornaliere per docente
    public class TeacherMaxDailyHoursConstraint : Constraint
    {
        public string TeacherName { get; set; } = string.Empty;
        public int MaxHours { get; set; }

        public TeacherMaxDailyHoursConstraint()
        {
            Type = ConstraintType.TeacherMaxDailyHours;
            Priority = ConstraintPriority.High;
        }

        public override bool Validate(ScheduleContext context)
        {
            var dailyHours = context.CurrentSchedule
                .Where(s => s.TeacherName == TeacherName)
                .GroupBy(s => s.Day)
                .Select(g => g.Count())
                .ToList();

            return !dailyHours.Any(h => h > MaxHours);
        }

        public override string GetHumanReadableDescription()
        {
            return $"{TeacherName}: massimo {MaxHours} ore al giorno";
        }
    }

    // Vincolo: Ore buche massime settimanali per docente
    public class TeacherMaxWeeklyGapsConstraint : Constraint
    {
        public string TeacherName { get; set; } = string.Empty;
        public int MaxGaps { get; set; }

        public TeacherMaxWeeklyGapsConstraint()
        {
            Type = ConstraintType.TeacherMaxWeeklyGaps;
            Priority = ConstraintPriority.Medium;
        }

        public override bool Validate(ScheduleContext context)
        {
            int totalGaps = 0;
            var teacherSlots = context.CurrentSchedule
                .Where(s => s.TeacherName == TeacherName)
                .GroupBy(s => s.Day)
                .ToList();

            foreach (var dayGroup in teacherSlots)
            {
                var hours = dayGroup.Select(s => s.Hour).OrderBy(h => h).ToList();
                if (hours.Count <= 1) continue;

                for (int i = 1; i < hours.Count; i++)
                {
                    totalGaps += (hours[i] - hours[i - 1] - 1);
                }
            }

            return totalGaps <= MaxGaps;
        }

        public override string GetHumanReadableDescription()
        {
            return $"{TeacherName}: massimo {MaxGaps} ore buche settimanali";
        }
    }

    // Vincolo: Giorno libero per docente
    public class TeacherDayOffConstraint : Constraint
    {
        public string TeacherName { get; set; } = string.Empty;
        public DayOfWeek DayOff { get; set; }

        public TeacherDayOffConstraint()
        {
            Type = ConstraintType.TeacherDayOff;
            Priority = ConstraintPriority.High;
        }

        public override bool Validate(ScheduleContext context)
        {
            return !context.CurrentSchedule.Any(s =>
                s.TeacherName == TeacherName && s.Day == DayOff);
        }

        public override string GetHumanReadableDescription()
        {
            return $"{TeacherName}: giorno libero {DayOff}";
        }
    }

    // Struttura per rappresentare uno slot temporale
    public class TimeSlot
    {
        public DayOfWeek Day { get; set; }
        public int Hour { get; set; }

        public override string ToString() => $"{Day} {Hour}°ora";
    }
}