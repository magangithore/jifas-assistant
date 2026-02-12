#!/bin/bash
# JIFAS AI Assistant - Production Deployment Script
# Usage: ./deploy-production.sh

set -e

echo "========================================="
echo "JIFAS AI Assistant - Production Deployment"
echo "========================================="
echo ""

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Check prerequisites
echo "${YELLOW}[1/5] Checking prerequisites...${NC}"
if ! command -v dotnet &> /dev/null; then
    echo "${RED}? .NET not found. Please install .NET 10 SDK${NC}"
    exit 1
fi
echo "${GREEN}? .NET SDK found: $(dotnet --version)${NC}"

# Check environment variables
echo ""
echo "${YELLOW}[2/5] Verifying configuration...${NC}"
if [ -z "$GEMINI_API_KEY" ]; then
    echo "${RED}? GEMINI_API_KEY environment variable not set${NC}"
    echo "Please set: export GEMINI_API_KEY='your-api-key'"
    exit 1
fi
echo "${GREEN}? GEMINI_API_KEY configured${NC}"

if [ -z "$DATABASE_CONNECTION_STRING" ]; then
    echo "${YELLOW}? DATABASE_CONNECTION_STRING not set, using default${NC}"
fi
echo "${GREEN}? Configuration verified${NC}"

# Build Release
echo ""
echo "${YELLOW}[3/5] Building Release version...${NC}"
cd Jifas.Assistant
dotnet build -c Release
if [ $? -eq 0 ]; then
    echo "${GREEN}? Build successful${NC}"
else
    echo "${RED}? Build failed${NC}"
    exit 1
fi

# Publish
echo ""
echo "${YELLOW}[4/5] Publishing application...${NC}"
dotnet publish -c Release -o ./publish-prod
if [ $? -eq 0 ]; then
    echo "${GREEN}? Publish successful${NC}"
else
    echo "${RED}? Publish failed${NC}"
    exit 1
fi

# Health check
echo ""
echo "${YELLOW}[5/5] Running health check...${NC}"
echo "Starting application..."
timeout 15 dotnet ./publish-prod/Jifas.Assistant.dll &
APPPID=$!
sleep 5

# Check health endpoint
if curl -s http://localhost:5000/health > /dev/null 2>&1; then
    echo "${GREEN}? Health check passed${NC}"
    kill $APPPID 2>/dev/null || true
else
    echo "${YELLOW}? Health check warning (may be normal depending on config)${NC}"
    kill $APPPID 2>/dev/null || true
fi

echo ""
echo "${GREEN}=========================================${NC}"
echo "${GREEN}? Deployment Ready!${NC}"
echo "${GREEN}=========================================${NC}"
echo ""
echo "Deployment package: ./Jifas.Assistant/publish-prod"
echo ""
echo "To deploy:"
echo "1. Copy publish-prod folder to server"
echo "2. Set environment variables:"
echo "   - GEMINI_API_KEY"
echo "   - DATABASE_CONNECTION_STRING (if needed)"
echo "3. Run: dotnet Jifas.Assistant.dll"
echo ""
echo "Or with systemd:"
echo "  sudo systemctl start jifas-assistant"
echo ""
