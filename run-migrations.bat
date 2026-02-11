@echo off
REM Script untuk menjalankan EF Core migrations di Windows
REM Usage: run-migrations.bat

echo.
echo JIFAS Assistant - Entity Framework Core Migrations
echo ==================================================
echo.

REM Check if running in Docker or locally
if "%ASPNETCORE_ENVIRONMENT%"=="Docker" (
    echo Running in Docker environment...
    REM Migrations sudah dijalankan dalam Program.cs
    echo Migrations will run automatically on application startup
) else (
    echo Running locally...
    
    where dotnet >nul 2>&1
    if errorlevel 1 (
        echo Error: dotnet CLI is not installed
        exit /b 1
    )
    
    cd Jifas.Assistant
    
    echo Creating database if not exists...
    call dotnet ef database update
    
    echo.
    echo [OK] Migrations completed successfully!
)
