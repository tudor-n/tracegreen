# syntax=docker/dockerfile:1.7
# Stages:
#   dev     - SDK image running `dotnet watch`; compose bind-mounts ./api here and keeps build output in /src/artifacts
#   build   - publishes the API
#   runtime - slim ASP.NET runtime image used by compose.demo.yml (api + one-shot migrate)

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS restore
WORKDIR /src
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
COPY global.json Directory.Build.props Directory.Packages.props TraceGreen.slnx ./
COPY src/TraceGreen.Domain/TraceGreen.Domain.csproj src/TraceGreen.Domain/
COPY src/TraceGreen.Infrastructure/TraceGreen.Infrastructure.csproj src/TraceGreen.Infrastructure/
COPY src/TraceGreen.Api/TraceGreen.Api.csproj src/TraceGreen.Api/
COPY tests/TraceGreen.Tests/TraceGreen.Tests.csproj tests/TraceGreen.Tests/
RUN dotnet restore

FROM restore AS dev
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
ENV ASPNETCORE_URLS=http://0.0.0.0:8080 \
    ASPNETCORE_ENVIRONMENT=Development
COPY . .
# Pre-build so the first `dotnet run` against the bind mount is incremental (artifacts live in a volume seeded from this layer).
RUN dotnet build src/TraceGreen.Api -c Debug --no-restore
EXPOSE 8080
# `dotnet watch` is deliberately not used here: its polling watcher crashes on Windows bind mounts
# (duplicate directory entries). After editing C# run `docker compose restart api`; the rebuild is incremental.
CMD ["dotnet", "run", "--project", "src/TraceGreen.Api", "--", "--migrate", "--seed"]

FROM restore AS build
COPY . .
RUN dotnet publish src/TraceGreen.Api -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
ENV ASPNETCORE_URLS=http://0.0.0.0:8080 DOTNET_EnableDiagnostics=0
COPY --from=build /app/publish .
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "TraceGreen.Api.dll"]
