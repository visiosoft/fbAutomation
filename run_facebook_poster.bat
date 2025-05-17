@echo off
cd /d "%~dp0"
echo Starting Facebook Auto Poster at %date% %time%
dotnet run
echo Facebook Auto Poster completed at %date% %time%
pause 