using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace SchoolScheduler.Common.Models
{
    public class SchoolClass
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;  // Es. 3AA, 4BS
        public int TotalWeeklyHours { get; set; }
        public List<Activity> Activities { get; set; } = new List<Activity>();

        // Per gestire classi articolate
        public bool HasArticulation { get; set; }
        public List<ArticulationGroup> ArticulationGroups { get; set; } = new List<ArticulationGroup>();
    }

    public class ArticulationGroup
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public List<Activity> Activities { get; set; } = new List<Activity>();
    }
}
