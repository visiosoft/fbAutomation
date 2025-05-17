@echo off
echo Facebook Auto Poster Installation
echo ================================
echo.

REM Check if running as administrator
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo Please run this installer as Administrator
    echo Right-click on install.bat and select "Run as administrator"
    pause
    exit /b 1
)

REM Create installation directory
set INSTALL_DIR=%ProgramFiles%\FacebookAutoPoster
echo Creating installation directory at %INSTALL_DIR%
if not exist "%INSTALL_DIR%" mkdir "%INSTALL_DIR%"

REM Copy files
echo Copying files...
xcopy /Y /E ".\*" "%INSTALL_DIR%\"

REM Create desktop shortcut
echo Creating desktop shortcut...
powershell -Command "$WS = New-Object -ComObject WScript.Shell; $SC = $WS.CreateShortcut([Environment]::GetFolderPath('Desktop') + '\Facebook Auto Poster.lnk'); $SC.TargetPath = '%INSTALL_DIR%\FacebookAutoPoster.exe'; $SC.WorkingDirectory = '%INSTALL_DIR%'; $SC.Description = 'Facebook Auto Poster'; $SC.Save()"

REM Verify the executable exists
if not exist "%INSTALL_DIR%\FacebookAutoPoster.exe" (
    echo ERROR: FacebookAutoPoster.exe not found in installation directory!
    echo Please make sure the executable is included in the installation package.
    pause
    exit /b 1
)

echo.
echo Installation completed successfully!
echo You can now run Facebook Auto Poster from your desktop shortcut
echo.
pause 