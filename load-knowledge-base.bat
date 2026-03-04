@echo off
REM Knowledge Base Loader Script untuk Windows
REM Script untuk load Knowledge Base files ke SQL Server

echo.
echo ================================
echo JIFAS Knowledge Base Loader
echo ================================
echo.

REM Check if we're in the correct directory
if not exist "Jifas.Assistant.csproj" (
    echo Error: Please run this script from Jifas.Assistant directory
    exit /b 1
)

echo Current Directory: %CD%
echo.

REM Build project
echo Building project...
dotnet build

if errorlevel 1 (
    echo Build failed!
    exit /b 1
)

echo.
echo Build successful!
echo.
echo To load Knowledge Base:
echo   1. Run the API: dotnet run
echo   2. Call endpoint: POST http://localhost:5000/api/knowledgebase/load
echo.
echo Or use curl:
echo   curl -X POST http://localhost:5000/api/knowledgebase/load
echo.
pause
