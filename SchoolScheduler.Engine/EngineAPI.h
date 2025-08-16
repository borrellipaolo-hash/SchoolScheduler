#pragma once

#ifdef SCHOOLSCHEDULERENGINE_EXPORTS
#define ENGINE_API __declspec(dllexport)
#else
#define ENGINE_API __declspec(dllimport)
#endif

extern "C" {
    ENGINE_API bool Engine_Initialize(const char* configPath);
    ENGINE_API bool Engine_GenerateSchedule();
    ENGINE_API const char* Engine_GetLastError();
    ENGINE_API void Engine_Cleanup();

    class EngineAPI
    {
    };
}