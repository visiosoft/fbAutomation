@echo off
echo Building Facebook Auto Poster
echo ===========================
echo.

REM Clean previous builds
echo Cleaning previous builds...
if exist "bin\Release\net8.0\publish" rmdir /S /Q "bin\Release\net8.0\publish"

REM Create local NuGet package directory
echo Setting up local NuGet package directory...
if not exist "packages" mkdir packages

REM Add local NuGet source
echo Adding local NuGet source...
dotnet nuget add source "%~dp0packages" --name local

REM Restore packages with retry
echo Restoring packages...
set MAX_RETRIES=3
set RETRY_COUNT=0

:RETRY_RESTORE
set /a RETRY_COUNT+=1
echo Attempt %RETRY_COUNT% of %MAX_RETRIES%
dotnet restore --source https://api.nuget.org/v3/index.json --source "%~dp0packages"
if %ERRORLEVEL% NEQ 0 (
    if %RETRY_COUNT% LSS %MAX_RETRIES% (
        echo Restore failed, retrying in 5 seconds...
        timeout /t 5 /nobreak
        goto RETRY_RESTORE
    ) else (
        echo ERROR: Failed to restore packages after %MAX_RETRIES% attempts
        echo Please check your internet connection and try again
        pause
        exit /b 1
    )
)

REM Build and publish with retry
echo Building and publishing...
set RETRY_COUNT=0

:RETRY_PUBLISH
set /a RETRY_COUNT+=1
echo Attempt %RETRY_COUNT% of %MAX_RETRIES%
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true /p:OutputType=Exe
if %ERRORLEVEL% NEQ 0 (
    if %RETRY_COUNT% LSS %MAX_RETRIES% (
        echo Build failed, retrying in 5 seconds...
        timeout /t 5 /nobreak
        goto RETRY_PUBLISH
    ) else (
        echo ERROR: Failed to build after %MAX_RETRIES% attempts
        pause
        exit /b 1
    )
)

REM Create release directory
echo Creating release package...
set RELEASE_DIR=FacebookAutoPoster-Release
if exist "%RELEASE_DIR%" rmdir /S /Q "%RELEASE_DIR%"
mkdir "%RELEASE_DIR%"

REM Copy files to release directory
echo Copying files...
xcopy /Y /E "bin\Release\net8.0\publish\*" "%RELEASE_DIR%\"
copy "README.md" "%RELEASE_DIR%\"
copy "install.bat" "%RELEASE_DIR%\"

REM Verify the executable exists
if not exist "%RELEASE_DIR%\FacebookAutoPoster.exe" (
    echo ERROR: FacebookAutoPoster.exe not found in release directory!
    echo Build failed.
    pause
    exit /b 1
)

REM Create sample files
echo Creating sample configuration files...
echo ProfileName,Username,Password,GroupUrl,PostText,IsAnonymous,ClosePreview > "%RELEASE_DIR%\posts.csv"
echo profile1,username1,password1,https://facebook.com/groups/group1,Your post text here,false,false >> "%RELEASE_DIR%\posts.csv"

echo # Format: account:host:port or account:host:port:username:password > "%RELEASE_DIR%\proxies.txt"
echo profile1:proxy1.example.com:8080 >> "%RELEASE_DIR%\proxies.txt"

REM Create ZIP file
echo Creating ZIP package...
powershell Compress-Archive -Path "%RELEASE_DIR%\*" -DestinationPath "FacebookAutoPoster.zip" -Force

REM Verify the ZIP file contains the executable
powershell -Command "if (-not (Test-Path 'FacebookAutoPoster.zip')) { Write-Error 'ZIP file not created!'; exit 1 }"
powershell -Command "if (-not (Get-ChildItem 'FacebookAutoPoster.zip' | Select-String -Pattern 'FacebookAutoPoster.exe')) { Write-Error 'Executable not found in ZIP file!'; exit 1 }"

echo.
echo Build completed successfully!
echo You can find the release package at FacebookAutoPoster.zip
echo.
pause 