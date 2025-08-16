#include "pch.h"
#include "EngineAPI.h"
#include <string>

static std::string g_lastError;
static bool g_initialized = false;

extern "C" {

    ENGINE_API bool Engine_Initialize(const char* configPath)
    {
        try {
            // TODO: Inizializzazione del motore
            g_initialized = true;
            g_lastError = "Engine initialized successfully";
            return true;
        }
        catch (const std::exception& e) {
            g_lastError = e.what();
            return false;
        }
    }

    ENGINE_API bool Engine_GenerateSchedule()
    {
        if (!g_initialized) {
            g_lastError = "Engine not initialized";
            return false;
        }

        try {
            // TODO: Implementare la generazione dell'orario
            // Per ora ritorna true per test
            g_lastError = "Schedule generated successfully";
            return true;
        }
        catch (const std::exception& e) {
            g_lastError = e.what();
            return false;
        }
    }

    ENGINE_API const char* Engine_GetLastError()
    {
        return g_lastError.c_str();
    }

    ENGINE_API void Engine_Cleanup()
    {
        // TODO: Pulizia risorse
        g_initialized = false;
        g_lastError.clear();
    }
}