@echo off
REM Test Local AI Server
REM This batch script tests connection and invokes Qwen3:8b model

echo.
echo ??????????????????????????????????????????????????????????
echo ?   JIFAS Assistant - Local AI (Qwen3) Test Script      ?
echo ?   Server: http://10.0.12.54:11434                     ?
echo ?   Model: qwen3:8b                                      ?
echo ??????????????????????????????????????????????????????????
echo.

REM Test 1: Check server availability
echo ?? [Test 1] Checking server availability...
curl -s http://10.0.12.54:11434/api/tags | find "qwen3" >nul
if %ERRORLEVEL% EQU 0 (
    echo ? [Test 1] Server is available and Qwen3 model found
) else (
    echo ? [Test 1] Failed - Server not reachable or model not found
    goto END
)

echo.
echo ?? [Test 2] Testing simple prompt...
echo Prompt: "Apa itu JIFAS?"
echo.

REM Test 2: Invoke with simple prompt
curl -X POST http://10.0.12.54:11434/api/generate ^
  -H "Content-Type: application/json" ^
  -d "{\"model\":\"qwen3:8b\",\"prompt\":\"Apa itu JIFAS? Jelaskan dalam 1-2 kalimat.\",\"stream\":false,\"temperature\":0.7}" ^
  2>nul | python -m json.tool 2>nul

echo.
echo ?? [Test 3] Testing JIFAS knowledge...
echo Prompt: "Modul apa saja yang ada di JIFAS?"
echo.

REM Test 3: Knowledge test
curl -X POST http://10.0.12.54:11434/api/generate ^
  -H "Content-Type: application/json" ^
  -d "{\"model\":\"qwen3:8b\",\"prompt\":\"Modul-modul apa saja yang ada di JIFAS? Jelaskan singkat.\",\"stream\":false,\"temperature\":0.7}" ^
  2>nul | python -m json.tool 2>nul

echo.
echo ? Tests completed!
echo.

:END
pause
