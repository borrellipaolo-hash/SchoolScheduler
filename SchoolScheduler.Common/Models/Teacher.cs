using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace SchoolScheduler.Common.Models
{
    public class Teacher
    {
        public int Id { get; set; }
        public string Surname { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string FullName => $"{Surname} {Name}".Trim();
        public HashSet<string> ClassCodes { get; set; } = new HashSet<string>(); // CdC multipli
        public int TotalWeeklyHours { get; set; }
        public List<Activity> Activities { get; set; } = new List<Activity>();
    }
}
