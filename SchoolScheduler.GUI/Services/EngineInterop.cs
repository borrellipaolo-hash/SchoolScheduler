using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using SchoolScheduler.Common.Interfaces;

namespace SchoolScheduler.GUI.Services
{
    public class EngineInterop : IScheduleEngine, IDisposable
    {
        // Import delle funzioni dalla DLL C++
        [DllImport("SchoolScheduler.Engine.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern bool Engine_Initialize([MarshalAs(UnmanagedType.LPStr)] string configPath);

        [DllImport("SchoolScheduler.Engine.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern bool Engine_GenerateSchedule();

        [DllImport("SchoolScheduler.Engine.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr Engine_GetLastError();

        [DllImport("SchoolScheduler.Engine.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void Engine_Cleanup();

        public bool InitializeEngine(string configPath)
        {
            try
            {
                return Engine_Initialize(configPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error initializing engine: {ex.Message}");
                return false;
            }
        }

        public bool GenerateSchedule()
        {
            return Engine_GenerateSchedule();
        }

        public string GetLastError()
        {
            IntPtr errorPtr = Engine_GetLastError();
            return Marshal.PtrToStringAnsi(errorPtr) ?? "Unknown error";
        }

        public void Cleanup()
        {
            Engine_Cleanup();
        }

        public void Dispose()
        {
            Cleanup();
        }
    }
}
