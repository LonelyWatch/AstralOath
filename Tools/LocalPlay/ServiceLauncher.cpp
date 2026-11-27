#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <filesystem>
#include <fstream>
#include <string>

static std::wstring ReadFailure(const std::filesystem::path& path) {
    std::ifstream file(path, std::ios::binary);
    char buffer[4096]; file.read(buffer, sizeof(buffer));
    const int size = static_cast<int>(file.gcount());
    if (!size) return {};
    const int count = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, buffer, size, nullptr, 0);
    if (!count) return {};
    std::wstring text(count, L'\0');
    MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, buffer, size, text.data(), count);
    return text;
}

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int) {
    wchar_t location[32768];
    const DWORD size = GetModuleFileNameW(nullptr, location, 32768);
    if (!size || size >= 32768) return 1;
    const auto root = std::filesystem::path(location).parent_path();
    const auto ps = (root / L"../../.tools/powershell/pwsh.exe").lexically_normal().wstring();
    const auto script = (root / L"local/start-play.ps1").wstring();
    std::wstring command = L"\"" + ps + L"\" -NoLogo -NoProfile -NonInteractive -File \"" + script + L"\"";
    STARTUPINFOW startup{}; startup.cb = sizeof(startup);
    PROCESS_INFORMATION child{};
    DWORD code = 1;
    std::wstring failure;
    if (CreateProcessW(ps.c_str(), command.data(), nullptr, nullptr, FALSE, CREATE_NO_WINDOW,
                       nullptr, root.c_str(), &startup, &child)) {
        WaitForSingleObject(child.hProcess, INFINITE);
        GetExitCodeProcess(child.hProcess, &code);
        CloseHandle(child.hThread); CloseHandle(child.hProcess);
        if (code) failure = ReadFailure(root / L"state/startup-last-error.txt");
    } else {
        failure = L"启动脚本无法运行。Windows 错误：" + std::to_wstring(GetLastError());
    }
    if (code && !GetEnvironmentVariableW(L"ASTRAL_QA_NO_PAUSE", nullptr, 0)) {
        if (failure.empty()) failure = L"本机试玩未能启动。请查看下面的启动诊断。";
        failure += L"\n\n诊断文件：" + (root / L"state/startup-last.json").wstring();
        MessageBoxW(nullptr, failure.c_str(), L"AstralOath R41", MB_ICONERROR | MB_OK);
    }
    return static_cast<int>(code);
}
