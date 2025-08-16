using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SchoolScheduler.Common.Interfaces
{
    public interface IScheduleEngine
    {
        bool InitializeEngine(string configPath);
        bool GenerateSchedule();
        string GetLastError();
        void Cleanup();
    }
}
