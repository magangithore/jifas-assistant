@echo off
REM ============================================
REM JIFAS Knowledge Base Seeding Script
REM ============================================

echo.
echo ============================================
echo JIFAS KB Seeding Automation
echo ============================================
echo.

REM Check if app is running
echo [1/4] Checking if app is running on port 5180...
timeout /t 2 /nobreak

REM Try to seed
echo.
echo [2/4] Seeding knowledge base files...
powershell -NoProfile -Command ^
  "$json = '{\"folderPath\": \"./knowledge-base\"}'; " ^
  "$response = Invoke-WebRequest -Uri 'http://localhost:5180/api/kb/admin/seed' " ^
  "-Method POST " ^
  "-ContentType 'application/json' " ^
  "-Body $json " ^
  "-ErrorAction SilentlyContinue; " ^
  "if ($response) { " ^
  "  $content = $response.Content | ConvertFrom-Json; " ^
  "  Write-Host 'Seeding Results:' -ForegroundColor Green; " ^
  "  $content.results | Format-Table -Property fileName, success, message | Out-String | Write-Host; " ^
  "  Write-Host ('Total: ' + $content.results.Count + ' files processed') -ForegroundColor Green " ^
  "} else { " ^
  "  Write-Host 'ERROR: App not responding on http://localhost:5180' -ForegroundColor Red; " ^
  "  Write-Host 'Make sure to: 1. Run: dotnet run' -ForegroundColor Yellow; " ^
  "  Write-Host '             2. Wait for app to start' -ForegroundColor Yellow; " ^
  "  Write-Host '             3. Then run this script again' -ForegroundColor Yellow " ^
  "}"

echo.
echo [3/4] Verifying data in database...
sqlcmd -S localhost -d JIFAS_Assistant -Q "SELECT COUNT(*) as [Total Documents] FROM KnowledgeBaseDocuments" 2>nul || (
  echo ERROR: Cannot connect to database
  echo Make sure SQL Server is running and database JIFAS_Assistant exists
)

echo.
echo [4/4] Done!
echo.
echo ============================================
echo Next Steps:
echo 1. Check results above
echo 2. Verify in SQL Server: SELECT * FROM KnowledgeBaseDocuments
echo 3. Test search: GET http://localhost:5180/api/kb/search?query=invoice
echo ============================================
echo.
pause
