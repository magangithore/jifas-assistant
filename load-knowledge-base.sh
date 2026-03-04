#!/bin/bash

# Knowledge Base Loader Script
# Script untuk load Knowledge Base files ke SQL Server

echo "================================"
echo "JIFAS Knowledge Base Loader"
echo "================================"
echo ""

# Check if we're in the correct directory
if [ ! -f "Jifas.Assistant.csproj" ]; then
    echo "Error: Please run this script from Jifas.Assistant directory"
    exit 1
fi

echo "Current Directory: $(pwd)"
echo ""

# Build project jika belum
echo "Building project..."
dotnet build

if [ $? -ne 0 ]; then
    echo "Build failed!"
    exit 1
fi

echo ""
echo "Build successful!"
echo ""
echo "To load Knowledge Base:"
echo "  1. Run the API: dotnet run"
echo "  2. Call endpoint: POST http://localhost:5000/api/knowledgebase/load"
echo ""
echo "Or use curl:"
echo "  curl -X POST http://localhost:5000/api/knowledgebase/load"
echo ""
