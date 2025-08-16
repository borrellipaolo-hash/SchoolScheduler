using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SchoolScheduler.Common.Models
{
    public class Activity
    {
        public int Id { get; set; }
        public string TeacherFullName { get; set; } = string.Empty;
        public string TeacherSurname { get; set; } = string.Empty;
        public string TeacherName { get; set; } = string.Empty;
        public string ClassCode { get; set; } = string.Empty;  // CdC
        public string ClassName { get; set; } = string.Empty;   // Classe
        public string Subject { get; set; } = string.Empty;     // Materia
        public int WeeklyHours { get; set; }                    // Ore
        public string ArticulationGroup { get; set; } = string.Empty;  // Gruppo (A1, A2, etc.)
        public bool IsArticulated => !string.IsNullOrWhiteSpace(ArticulationGroup);

        // Per identificare attività duplicate (stesso docente, stessa classe, stessa ora)
        public string GetKey() => $"{TeacherFullName}_{ClassName}_{Subject}";
    }
}
