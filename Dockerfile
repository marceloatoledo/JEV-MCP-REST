FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore in its own layer: only invalidated when package configuration changes.
COPY global.json Directory.Build.props Directory.Packages.props JevMcp.slnx ./
COPY src/JevMcp.Core/JevMcp.Core.csproj src/JevMcp.Core/
COPY src/JevMcp.Providers/JevMcp.Providers.csproj src/JevMcp.Providers/
COPY src/JevMcp.Tools/JevMcp.Tools.csproj src/JevMcp.Tools/
COPY src/JevMcp.Data/JevMcp.Data.csproj src/JevMcp.Data/
COPY src/JevMcp.App/JevMcp.App.csproj src/JevMcp.App/
COPY tests/JevMcp.Tests/JevMcp.Tests.csproj tests/JevMcp.Tests/
RUN dotnet restore JevMcp.slnx

COPY . .
RUN dotnet publish src/JevMcp.App/JevMcp.App.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
COPY docker-entrypoint.sh /app/docker-entrypoint.sh

# curl feeds HEALTHCHECK; setpriv (util-linux) drops root after fixing volume
# ownership. The application process does not stay privileged.
USER root
RUN sed -i 's/\r$//' /app/docker-entrypoint.sh \
    && chmod +x /app/docker-entrypoint.sh \
    && mkdir -p /app/data \
    && chown $APP_UID:$APP_UID /app/data \
    && apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_URLS=http://+:10022
EXPOSE 10022
HEALTHCHECK --interval=5s --timeout=3s --start-period=25s --retries=12 \
    CMD curl -fsS http://127.0.0.1:10022/health || exit 1
ENTRYPOINT ["/app/docker-entrypoint.sh"]

