# JIFAS AI Assistant - Dockerfile Production
# Image ini hanya berisi runtime aplikasi yang sudah dipublish.
#
# Restore NuGet di dalam Docker Linux bisa gagal di jaringan kantor karena SSL inspection.
# Karena itu aplikasi dipublish dulu di host Windows, lalu folder ./publish disalin ke image.
# Jalankan scripts/Start-DockerStack.ps1 supaya proses publish, build, dan start selalu konsisten.

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# CA lokal kantor dibutuhkan saat HTTPS keluar diperiksa oleh endpoint security.
COPY ./certs/*.crt /usr/local/share/ca-certificates/

# curl dipakai healthcheck, ca-certificates dibutuhkan agar HTTPS ke Jira/Atlassian
# dari dalam container bisa memvalidasi root certificate dengan benar.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl ca-certificates \
    && update-ca-certificates \
    && rm -rf /var/lib/apt/lists/*

COPY ./publish .

# Razor Pages (runtime-compiled): salin direktori Pages dari source ke container
COPY ./Jifas.Assistant/Pages /app/Pages/

RUN mkdir -p /app/logs && chmod -R 755 /app/logs

ENV ASPNETCORE_URLS=http://+:8888
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_RUNNING_IN_CONTAINER=true

EXPOSE 8888

HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 \
    CMD curl -f http://localhost:8888/health || exit 1

ENTRYPOINT ["dotnet", "Jifas.Assistant.dll"]
