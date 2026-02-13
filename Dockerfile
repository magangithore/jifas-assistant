# JIFAS AI Assistant - Production Dockerfile
# Simple approach: copy already published app
# Runtime stage only - no build inside container

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# Install curl for health checks
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

# Copy pre-published app (built on host)
COPY ./publish-context/ .

# Configuration
ENV ASPNETCORE_URLS=http://+:8888
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8888

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 \
    CMD curl -f http://localhost:8888/api/chatbot/health || exit 1

# Run
ENTRYPOINT ["dotnet", "Jifas.Assistant.dll"]


