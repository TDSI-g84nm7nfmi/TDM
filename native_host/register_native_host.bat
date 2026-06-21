@echo off
chcp 65001 >nul
:: TDM Native Host 安装/卸载脚本
:: 用法：register_native_host.bat [install|uninstall] [INSTALL_DIR]
::
:: 支持浏览器：Google Chrome / Microsoft Edge / Brave / Vivaldi / Opera / Tabbit
:: 注册到 HKCU（当前用户），无需管理员权限

setlocal

set "ACTION=%~1"
if "%ACTION%"=="" set "ACTION=install"
set "INSTALL_DIR=%~2"
if "%INSTALL_DIR%"=="" set "INSTALL_DIR=%~dp0.."

set "NH_DIR=%INSTALL_DIR%\native_host"
set "PY=%SystemRoot%\py.exe"
if not exist "%PY%" set "PY=python.exe"

:: 写 native host 启动脚本（用 .bat 包裹 python，路径加引号处理空格）
> "%NH_DIR%\native_host.bat" echo @echo off
>> "%NH_DIR%\native_host.bat" echo chcp 65001 ^>nul
>> "%NH_DIR%\native_host.bat" echo "%PY%" "%NH_DIR%\native_host.py" %%*

:: 写 manifest（绝对路径，处理空格转义）
set "NH_BAT=%NH_DIR%\native_host.bat"
set "NH_JSON=%NH_DIR%\com.tdm.app.json"
set "NH_JSON_ESC=%NH_JSON: =\\ %"
set "NH_BAT_ESC=%NH_BAT: =\\ %"

> "%NH_JSON%" echo {
>> "%NH_JSON%" echo   "name": "com.tdm.app",
>> "%NH_JSON%" echo   "description": "TDM Native Messaging Host",
>> "%NH_JSON%" echo   "path": "%NH_BAT_ESC%",
>> "%NH_JSON%" echo   "type": "stdio",
>> "%NH_JSON%" echo   "allowed_extensions": ["chrome-extension://*/*"]
>> "%NH_JSON%" echo }

if /i "%ACTION%"=="install" (
    echo.
    echo [TDM] 正在注册 Native Host 到浏览器...
    echo.
    :: Chrome
    reg add "HKCU\Software\Google\Chrome\NativeMessagingHosts\com.tdm.app" /ve /t REG_SZ /d "%NH_JSON%" /f >nul 2>&1 && echo   [√] Google Chrome
    :: Edge (Chromium)
    reg add "HKCU\Software\Microsoft\Edge\NativeMessagingHosts\com.tdm.app" /ve /t REG_SZ /d "%NH_JSON%" /f >nul 2>&1 && echo   [√] Microsoft Edge
    :: Brave
    reg add "HKCU\Software\BraveSoftware\Brave-Browser\NativeMessagingHosts\com.tdm.app" /ve /t REG_SZ /d "%NH_JSON%" /f >nul 2>&1 && echo   [√] Brave
    :: Vivaldi
    reg add "HKCU\Software\Vivaldi\Vivaldi\NativeMessagingHosts\com.tdm.app" /ve /t REG_SZ /d "%NH_JSON%" /f >nul 2>&1 && echo   [√] Vivaldi
    :: Opera
    reg add "HKCU\Software\OperaSoftware\NativeMessagingHosts\com.tdm.app" /ve /t REG_SZ /d "%NH_JSON%" /f >nul 2>&1 && echo   [√] Opera
    :: Tabbit
    reg add "HKCU\Software\Tabbit\TabbitBrowser\NativeMessagingHosts\com.tdm.app" /ve /t REG_SZ /d "%NH_JSON%" /f >nul 2>&1 && echo   [√] Tabbit
    :: 360 极速浏览器
    reg add "HKCU\Software\360Chrome\Chrome\NativeMessagingHosts\com.tdm.app" /ve /t REG_SZ /d "%NH_JSON%" /f >nul 2>&1 && echo   [√] 360 极速浏览器
    :: QQ 浏览器
    reg add "HKCU\Software\Tencent\QQBrowser\NativeMessagingHosts\com.tdm.app" /ve /t REG_SZ /d "%NH_JSON%" /f >nul 2>&1 && echo   [√] QQ 浏览器
    :: CentBrowser (百分浏览器)
    reg add "HKCU\Software\CentBrowser\Chrome\NativeMessagingHosts\com.tdm.app" /ve /t REG_SZ /d "%NH_JSON%" /f >nul 2>&1 && echo   [√] 百分浏览器
    echo.
    echo [TDM] 完成！manifest: %NH_JSON%
    echo.
) else (
    echo [TDM] 正在移除 Native Host 注册...
    reg delete "HKCU\Software\Google\Chrome\NativeMessagingHosts\com.tdm.app" /f >nul 2>&1
    reg delete "HKCU\Software\Microsoft\Edge\NativeMessagingHosts\com.tdm.app" /f >nul 2>&1
    reg delete "HKCU\Software\BraveSoftware\Brave-Browser\NativeMessagingHosts\com.tdm.app" /f >nul 2>&1
    reg delete "HKCU\Software\Vivaldi\Vivaldi\NativeMessagingHosts\com.tdm.app" /f >nul 2>&1
    reg delete "HKCU\Software\OperaSoftware\NativeMessagingHosts\com.tdm.app" /f >nul 2>&1
    reg delete "HKCU\Software\Tabbit\TabbitBrowser\NativeMessagingHosts\com.tdm.app" /f >nul 2>&1
    reg delete "HKCU\Software\360Chrome\Chrome\NativeMessagingHosts\com.tdm.app" /f >nul 2>&1
    reg delete "HKCU\Software\Tencent\QQBrowser\NativeMessagingHosts\com.tdm.app" /f >nul 2>&1
    reg delete "HKCU\Software\CentBrowser\Chrome\NativeMessagingHosts\com.tdm.app" /f >nul 2>&1
    echo [TDM] 注册已移除
)

endlocal
