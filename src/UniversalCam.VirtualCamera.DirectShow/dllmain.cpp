#include "pch.h"
#include "UCamCaptureFilter.h"

// Forward declarations
extern "C" STDAPI DllRegisterServer();
extern "C" STDAPI DllUnregisterServer();

// {12345678-1234-1234-1234-123456789012}
DEFINE_GUID(CLSID_UCamCaptureFilter,
            0x12345678, 0x1234, 0x1234, 0x12, 0x34, 0x12, 0x34, 0x56, 0x78, 0x90, 0x12);

// {ABCDEF01-2345-6789-ABCD-EF0123456789}
DEFINE_GUID(LIBID_UniversalCamVCam,
            0xABCDEF01, 0x2345, 0x6789, 0xAB, 0xCD, 0xEF, 0x01, 0x23, 0x45, 0x67, 0x89);

// Global filter data
CFactoryTemplate g_Templates[] =
    {
        {L"UniversalCam Virtual Camera",
         &CLSID_UCamCaptureFilter,
         UCamCaptureFilter::CreateInstance,
         NULL,
         &sudFilterReg}};

int g_cTemplates = sizeof(g_Templates) / sizeof(g_Templates[0]);

STDAPI DllRegisterServer()
{
    return AMovieDllRegisterServer2(TRUE);
}

STDAPI DllUnregisterServer()
{
    return AMovieDllRegisterServer2(FALSE);
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID lpReserved)
{
    switch (ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH:
    case DLL_THREAD_ATTACH:
    case DLL_THREAD_DETACH:
    case DLL_PROCESS_DETACH:
        break;
    }
    return TRUE;
}
