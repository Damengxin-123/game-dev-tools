@echo off
chcp 65001 >nul
setlocal
rem 使用 PowerShell 7 调用项目启动入口，支持中文及含空格的目录。
if not exist "C:\Program Files\PowerShell\7\pwsh.exe" (
    echo 未找到 PowerShell 7，请先安装后重试。
    pause
    exit /b 1
)
"C:\Program Files\PowerShell\7\pwsh.exe" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0启动.ps1"
if errorlevel 1 (
    echo.
    echo 软件启动失败，请查看上方错误信息。
    pause
    exit /b 1
)
endlocal
exit /b 0
