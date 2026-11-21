#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shellapi.h>
#include <cwchar>
#include <string>
#include <vector>

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int)
{
    std::vector<wchar_t> buffer(32768);
    const DWORD count = GetModuleFileNameW(nullptr, buffer.data(), static_cast<DWORD>(buffer.size()));
    if (count == 0 || count >= buffer.size()) return 1;
    std::wstring launcher(buffer.data(), count);
    const auto slash = launcher.find_last_of(L"\\/");
    if (slash == std::wstring::npos) return 1;
    const bool mechanic = _wcsicmp(launcher.c_str() + slash + 1, L"Mechanic.exe") == 0;
    const std::wstring folder = launcher.substr(0, slash) + L"\\program";
    const std::wstring target = folder + (mechanic ? L"\\EzEizo-Native-Mechanic.exe" : L"\\EzEizo-Native-Metis.exe");
    const auto result = reinterpret_cast<INT_PTR>(
        ShellExecuteW(nullptr, L"open", target.c_str(), nullptr, folder.c_str(), SW_SHOWNORMAL));
    if (result <= 32)
    {
        MessageBoxW(nullptr,
            L"לא ניתן לפתוח את EzEizo. ודא שהתיקייה program נמצאת לצד קובץ ההפעלה.",
            L"EzEizo", MB_OK | MB_ICONERROR | MB_RTLREADING);
        return 1;
    }
    return 0;
}
