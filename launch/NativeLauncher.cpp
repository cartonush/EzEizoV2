#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cwchar>
#include <string>
#include <vector>

static void ShowFailure(const std::wstring& problem, const std::wstring& target)
{
    const std::wstring message = problem + L"\n\nקובץ היישום:\n" + target +
        L"\n\nודא שכל תיקיית EzEizo חולצה לשולחן העבודה, ונסה לפתוח את הקובץ הזה ישירות מתוך program.";
    MessageBoxW(nullptr, message.c_str(), L"EzEizo - שגיאת הפעלה", MB_OK | MB_ICONERROR | MB_RTLREADING);
}

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int)
{
    std::vector<wchar_t> buffer(32768);
    const DWORD count = GetModuleFileNameW(nullptr, buffer.data(), static_cast<DWORD>(buffer.size()));
    if (count == 0 || count >= buffer.size())
    {
        MessageBoxW(nullptr, L"לא ניתן לזהות את מיקום התוכנה.", L"EzEizo", MB_OK | MB_ICONERROR);
        return 1;
    }
    std::wstring launcher(buffer.data(), count);
    const auto slash = launcher.find_last_of(L"\\/");
    if (slash == std::wstring::npos) return 1;
    const bool mechanic = _wcsicmp(launcher.c_str() + slash + 1, L"Mechanic.exe") == 0;
    const std::wstring folder = launcher.substr(0, slash) + L"\\program";
    const std::wstring target = folder + (mechanic ? L"\\EzEizo-Native-Mechanic.exe" : L"\\EzEizo-Native-Metis.exe");
    if (GetFileAttributesW(target.c_str()) == INVALID_FILE_ATTRIBUTES)
    {
        ShowFailure(L"קובץ התוכנה לא נמצא בתיקיית program.", target);
        return 1;
    }
    STARTUPINFOW startup{};
    startup.cb = sizeof(startup);
    PROCESS_INFORMATION process{};
    const std::wstring command = L"\"" + target + L"\"";
    std::vector<wchar_t> mutableCommand(command.begin(), command.end());
    mutableCommand.push_back(L'\0');
    if (!CreateProcessW(target.c_str(), mutableCommand.data(), nullptr, nullptr, FALSE, 0,
                        nullptr, folder.c_str(), &startup, &process))
    {
        const DWORD code = GetLastError();
        ShowFailure(L"Windows לא הצליח להפעיל את התוכנה. קוד שגיאה: " + std::to_wstring(code), target);
        return 1;
    }
    CloseHandle(process.hThread);
    const DWORD state = WaitForSingleObject(process.hProcess, 3000);
    if (state == WAIT_OBJECT_0)
    {
        DWORD code = 0;
        GetExitCodeProcess(process.hProcess, &code);
        ShowFailure(L"התוכנה נסגרה מיד לאחר ההפעלה. קוד יציאה: " + std::to_wstring(code), target);
        CloseHandle(process.hProcess);
        return 1;
    }
    if (state == WAIT_FAILED)
    {
        const DWORD code = GetLastError();
        ShowFailure(L"לא ניתן לבדוק את מצב התוכנה. קוד שגיאה: " + std::to_wstring(code), target);
        CloseHandle(process.hProcess);
        return 1;
    }
    CloseHandle(process.hProcess);
    return 0;
}
