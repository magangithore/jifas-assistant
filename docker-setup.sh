#!/bin/bash

# JIFAS Assistant - Docker Setup Script
# Script ini membantu setup dan menjalankan JIFAS AI Assistant di Docker

set -e

echo "=========================================="
echo "JIFAS AI Assistant - Docker Setup"
echo "=========================================="

# Load environment variables
if [ -f ".env.docker" ]; then
    export $(cat .env.docker | grep -v '^#' | xargs)
    echo "? Environment variables loaded from .env.docker"
else
    echo "? .env.docker not found. Please create it from .env.docker template"
    exit 1
fi

# Check if Docker is installed
if ! command -v docker &> /dev/null; then
    echo "? Docker is not installed. Please install Docker first."
    exit 1
fi

echo "? Docker detected"

# Check if docker-compose is available
if ! command -v docker-compose &> /dev/null && ! docker compose version &> /dev/null; then
    echo "? docker-compose is not available"
    exit 1
fi

echo "? Docker Compose detected"

# Build images
echo ""
echo "Building Docker images..."
docker-compose build

# Start services
echo ""
echo "Starting services..."
docker-compose up -d

# Wait for services to be ready
echo ""
echo "Waiting for services to be ready..."
sleep 15

# Check health
echo ""
echo "Checking service health..."

# Check SQL Server
echo -n "SQL Server: "
if docker-compose exec -T sqlserver /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P "$SQL_SA_PASSWORD" -Q "SELECT 1" > /dev/null 2>&1; then
    echo "? Ready"
else
    echo "? Not ready yet"
fi

# Check Qdrant
echo -n "Qdrant: "
if curl -s http://localhost:6333/health > /dev/null 2>&1; then
    echo "? Ready"
else
    echo "? Not ready yet"
fi

# Check API
echo -n "JIFAS API: "
if curl -s http://localhost:5000/health | grep -q "Healthy" 2>/dev/null; then
    echo "? Ready"
else
    echo "? Starting up..."
fi

echo ""
echo "=========================================="
echo "? Setup Complete!"
echo "=========================================="
echo ""
echo "Service URLs:"
echo "  • JIFAS API: http://localhost:5000"
echo "  • API Docs: http://localhost:5000/api-docs"
echo "  • Health Check: http://localhost:5000/health"
echo "  • Qdrant: http://localhost:6333"
echo "  • SQL Server: localhost:1433"
echo "  • pgAdmin: http://localhost:5050"
echo ""
echo "Default Credentials:"
echo "  • SQL Server: sa / $SQL_SA_PASSWORD"
echo "  • pgAdmin: admin@jababeka.com / $PGADMIN_PASSWORD"
echo ""
echo "Common Commands:"
echo "  • View logs: docker-compose logs -f jifas-api"
echo "  • Stop services: docker-compose down"
echo "  • Remove volumes: docker-compose down -v"
echo ""
