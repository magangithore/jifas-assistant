#!/usr/bin/env pwsh

# Knowledge Base Loader Script
# Script untuk load Knowledge Base files ke SQL Server

Write-Host ""
Write-Host "================================" -ForegroundColor Cyan
Write-Host "JIFAS Knowledge Base Loader" -ForegroundColor Cyan
Write-Host "================================" -ForegroundColor Cyan
Write-Host ""

# Get current directory
$currentDir = Get-Location
Write-Host "Current Directory: $currentDir" -ForegroundColor Yellow
Write-Host ""

# Check if Jifas.Assistant folder exists
if (-not (Test-Path "Jifas.Assistant")) {
    Write-Host "Error: Please run this script from the project root directory" -ForegroundColor Red
    exit 1
}

Write-Host "Starting API..." -ForegroundColor Green
Write-Host "Prerequisites:" -ForegroundColor Yellow
Write-Host "  ? SQL Server running with JIFAS_Assistant database" -ForegroundColor Gray
Write-Host "  ? Ollama running at http://10.0.12.54:11434" -ForegroundColor Gray
Write-Host ""

Write-Host "To load Knowledge Base:" -ForegroundColor Green
Write-Host "  1. Option A - Via API (Recommended):" -ForegroundColor Yellow
Write-Host "     dotnet run" -ForegroundColor Cyan
Write-Host "     Then: curl -X POST http://localhost:5000/api/knowledgebase/load" -ForegroundColor Cyan
Write-Host ""
Write-Host "  2. Option B - Direct Database Insert:" -ForegroundColor Yellow
Write-Host "     Create a console app with IKnowledgeBaseLoaderService" -ForegroundColor Cyan
Write-Host ""

Write-Host "Expected Output:" -ForegroundColor Green
Write-Host "  ? Scans all .txt files in Jifas.Assistant/KnowledgeBase/" -ForegroundColor Gray
Write-Host "  ? Chunks each document into paragraphs" -ForegroundColor Gray
Write-Host "  ? Generates embeddings via Ollama (1024-dimensional)" -ForegroundColor Gray
Write-Host "  ? Inserts chunks into KnowledgeBaseDocuments table" -ForegroundColor Gray
Write-Host ""

Write-Host "After Loading:" -ForegroundColor Green
Write-Host "  • Total files processed" -ForegroundColor Gray
Write-Host "  • Total chunks inserted" -ForegroundColor Gray
Write-Host "  • Ready for RAG queries" -ForegroundColor Gray
Write-Host ""

Write-Host "For full details, see: KNOWLEDGE_BASE_LOADER_COMPLETE.md" -ForegroundColor Yellow
Write-Host ""
