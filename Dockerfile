# JIFAS AI Assistant - Production Dockerfile
# Runtime-only build (no compilation in Docker to avoid SSL issues)
# Pre-compiled application is provided in ./publish folder

# ===== RUNTIME STAGE =====
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# Install curl for health checks
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

# Copy pre-published application
COPY ./publish .

# Create logs directory
RUN mkdir -p /app/logs && chmod -R 755 /app/logs

# Configuration
ENV ASPNETCORE_URLS=http://+:8888
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_RUNNING_IN_CONTAINER=true

EXPOSE 8888

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 \
    CMD curl -f http://localhost:8888/health || exit 1

# Run application
ENTRYPOINT ["dotnet", "Jifas.Assistant.dll"]




