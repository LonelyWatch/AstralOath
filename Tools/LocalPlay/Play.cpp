#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <string>

static int ShowError(const wchar_t* message)
{
    MessageBoxW(nullptr, message, L"AstralOath 本机试玩", MB_OK | MB_ICONERROR);
    return 1;
}

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR arguments, int)
{
    wchar_t modulePath[32768];
    const DWORD length = GetModuleFileNameW(nullptr, modulePath, ARRAYSIZE(modulePath));
    if (!length || length >= ARRAYSIZE(modulePath))
        return ShowError(L"无法读取启动器的安装位置。");

    const std::wstring launcher(modulePath, length);
    const std::wstring clientDirectory = launcher.substr(0, launcher.rfind(L'\\')) + L"\\client";
    const std::wstring player = clientDirectory + L"\\AstralOath.exe";
    std::wstring command = L"\"" + player + L"\"";
    // wWinMain already receives the original command line after argv[0], including its quoting.
    if (arguments && *arguments)
        command.append(L" ").append(arguments);
    if (!SetEnvironmentVariableW(L"ASTRAL_FOUNDATION_URL", L"http://localhost:7728"))
        return ShowError(L"无法设置本次游戏连接使用的本机地址。");

    STARTUPINFOW startup{};
    startup.cb = sizeof(startup);
    PROCESS_INFORMATION child{};
    if (!CreateProcessW(player.c_str(), command.data(), nullptr, nullptr, FALSE, CREATE_NO_WINDOW,
                        nullptr, clientDirectory.c_str(), &startup, &child))
        return ShowError(L"游戏未能启动。请确认试玩包已完整解压，启动器与 client 文件夹位于同一目录。");
    CloseHandle(child.hThread);
    CloseHandle(child.hProcess);
    return 0;
}
