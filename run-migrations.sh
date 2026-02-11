#!/bin/bash

# Script untuk menjalankan EF Core migrations
# Usage: ./run-migrations.sh

set -e

echo "JIFAS Assistant - Entity Framework Core Migrations"
echo "=================================================="
echo ""

# Check if running in Docker or locally
if [ "$ASPNETCORE_ENVIRONMENT" == "Docker" ]; then
    echo "Running in Docker environment..."
    # Migrations sudah dijalankan dalam Program.cs
    echo "Migrations will run automatically on application startup"
else
    echo "Running locally..."
    
    if ! command -v dotnet &> /dev/null; then
        echo "Error: dotnet CLI is not installed"
        exit 1
    fi
    
    cd Jifas.Assistant
    
    echo "Creating database if not exists..."
    dotnet ef database update
    
    echo ""
    echo "? Migrations completed successfully!"
fi
