FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj files
COPY ["Jifas.Assistant/Jifas.Assistant.csproj", "Jifas.Assistant/"]

# Restore dependencies
RUN dotnet restore "Jifas.Assistant/Jifas.Assistant.csproj"

# Copy source code
COPY . .

# Build application
WORKDIR "/src/Jifas.Assistant"
RUN dotnet build "Jifas.Assistant.csproj" -c Release -o /app/build

# Publish
FROM build AS publish
RUN dotnet publish "Jifas.Assistant.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Install curl for health checks
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

# Copy published app
COPY --from=publish /app/publish .

# Create logs directory
RUN mkdir -p /app/Logs && chmod 777 /app/Logs

# Expose ports
EXPOSE 80 443

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=40s --retries=3 \
    CMD curl -f http://localhost/health || exit 1

# Set environment
ENV ASPNETCORE_URLS=http://+:80
ENV DOTNET_EnableDiagnostics=0
ENV DOTNET_ReadyToRun=0

# Run application
ENTRYPOINT ["dotnet", "Jifas.Assistant.dll"]
