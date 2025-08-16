using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SchoolScheduler.Common.Models
{
    // Builder pattern per creare vincoli in modo fluente
    public class ConstraintBuilder
    {
        private readonly List<Constraint> _constraints = new List<Constraint>();

        // Metodo per docenti
        public TeacherConstraintBuilder ForTeacher(string teacherName)
        {
            return new TeacherConstraintBuilder(teacherName, _constraints);
        }

        // Metodo per classi
        public ClassConstraintBuilder ForClass(string className)
        {
            return new ClassConstraintBuilder(className, _constraints);
        }

        // Ottieni tutti i vincoli costruiti
        public List<Constraint> Build()
        {
            return _constraints.ToList();
        }
    }

    // Builder specifico per vincoli docente
    public class TeacherConstraintBuilder
    {
        private readonly string _teacherName;
        private readonly List<Constraint> _constraints;

        public TeacherConstraintBuilder(string teacherName, List<Constraint> constraints)
        {
            _teacherName = teacherName;
            _constraints = constraints;
        }

        public TeacherConstraintBuilder NotAvailable(DayOfWeek day, int hour)
        {
            var existing = _constraints.OfType<TeacherAvailabilityConstraint>()
                .FirstOrDefault(c => c.TeacherName == _teacherName);

            if (existing == null)
            {
                existing = new TeacherAvailabilityConstraint
                {
                    TeacherName = _teacherName,
                    Name = $"Disponibilità {_teacherName}"
                };
                _constraints.Add(existing);
            }

            existing.UnavailableSlots.Add(new TimeSlot { Day = day, Hour = hour });
            return this;
        }

        public TeacherConstraintBuilder MaxDailyHours(int hours, ConstraintPriority priority = ConstraintPriority.High)
        {
            _constraints.Add(new TeacherMaxDailyHoursConstraint
            {
                TeacherName = _teacherName,
                MaxHours = hours,
                Priority = priority,
                Name = $"Max ore giornaliere {_teacherName}"
            });
            return this;
        }

        public TeacherConstraintBuilder MaxWeeklyGaps(int gaps, ConstraintPriority priority = ConstraintPriority.Medium)
        {
            _constraints.Add(new TeacherMaxWeeklyGapsConstraint
            {
                TeacherName = _teacherName,
                MaxGaps = gaps,
                Priority = priority,
                Name = $"Max ore buche {_teacherName}"
            });
            return this;
        }

        public TeacherConstraintBuilder DayOff(DayOfWeek day, ConstraintPriority priority = ConstraintPriority.High)
        {
            _constraints.Add(new TeacherDayOffConstraint
            {
                TeacherName = _teacherName,
                DayOff = day,
                Priority = priority,
                Name = $"Giorno libero {_teacherName}"
            });
            return this;
        }

        // Torna al builder principale
        public ConstraintBuilder And()
        {
            return new ConstraintBuilder { /* pass back constraints */ };
        }
    }

    // Builder specifico per vincoli classe (da implementare)
    public class ClassConstraintBuilder
    {
        private readonly string _className;
        private readonly List<Constraint> _constraints;

        public ClassConstraintBuilder(string className, List<Constraint> constraints)
        {
            _className = className;
            _constraints = constraints;
        }

        // TODO: Implementare metodi per vincoli delle classi
    }
}
