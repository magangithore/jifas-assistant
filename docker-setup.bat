@echo off
REM JIFAS Assistant - Docker Setup Script for Windows
REM Script ini membantu setup dan menjalankan JIFAS AI Assistant di Docker

echo.
echo ==========================================
echo JIFAS AI Assistant - Docker Setup
echo ==========================================
echo.

REM Load environment variables from .env.docker
if exist ".env.docker" (
    for /f "delims== tokens=1,2" %%A in (.env.docker) do (
        if not "%%A"=="" (
            if not "%%A:~0,1%%"=="#" (
                set "%%A=%%B"
            )
        )
    )
    echo [OK] Environment variables loaded from .env.docker
) else (
    echo [ERROR] .env.docker not found. Please create it from .env.docker template
    exit /b 1
)

REM Check if Docker is installed
docker --version >nul 2>&1
if errorlevel 1 (
    echo [ERROR] Docker is not installed. Please install Docker first.
    exit /b 1
)
echo [OK] Docker detected

REM Check if docker-compose is available
docker-compose --version >nul 2>&1
if errorlevel 1 (
    docker compose version >nul 2>&1
    if errorlevel 1 (
        echo [ERROR] docker-compose is not available
        exit /b 1
    )
)
echo [OK] Docker Compose detected

REM Build images
echo.
echo Building Docker images...
docker-compose build
if errorlevel 1 (
    echo [ERROR] Failed to build Docker images
    exit /b 1
)

REM Start services
echo.
echo Starting services...
docker-compose up -d
if errorlevel 1 (
    echo [ERROR] Failed to start services
    exit /b 1
)

REM Wait for services
echo.
echo Waiting for services to be ready...
timeout /t 15 /nobreak

REM Check health
echo.
echo Checking service health...

echo Checking SQL Server...
docker-compose exec -T sqlserver sqlcmd -S localhost -U sa -P "%SQL_SA_PASSWORD%" -Q "SELECT 1" >nul 2>&1
if errorlevel 0 (
    echo [OK] SQL Server is ready
) else (
    echo [WARN] SQL Server is not ready yet
)

echo Checking Qdrant...
powershell -Command "try { $response = Invoke-WebRequest -Uri 'http://localhost:6333/health' -TimeoutSec 5; if ($response.StatusCode -eq 200) { Write-Host '[OK] Qdrant is ready' } else { Write-Host '[WARN] Qdrant health check failed' } } catch { Write-Host '[WARN] Qdrant is not ready yet' }"

echo Checking JIFAS API...
powershell -Command "try { $response = Invoke-WebRequest -Uri 'http://localhost:5000/health' -TimeoutSec 5; if ($response.StatusCode -eq 200) { Write-Host '[OK] JIFAS API is ready' } else { Write-Host '[WARN] JIFAS API health check failed' } } catch { Write-Host '[WARN] JIFAS API is starting up' }"

echo.
echo ==========================================
echo [OK] Setup Complete!
echo ==========================================
echo.
echo Service URLs:
echo   * JIFAS API: http://localhost:5000
echo   * API Docs: http://localhost:5000/api-docs
echo   * Health Check: http://localhost:5000/health
echo   * Qdrant: http://localhost:6333
echo   * SQL Server: localhost:1433
echo   * pgAdmin: http://localhost:5050
echo.
echo Default Credentials:
echo   * SQL Server: sa / %SQL_SA_PASSWORD%
echo   * pgAdmin: admin@jababeka.com / %PGADMIN_PASSWORD%
echo.
echo Common Commands:
echo   * View logs: docker-compose logs -f jifas-api
echo   * Stop services: docker-compose down
echo   * Remove volumes: docker-compose down -v
echo.
