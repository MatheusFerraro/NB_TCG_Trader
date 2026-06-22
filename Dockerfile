# syntax=docker/dockerfile:1

# ---- Build stage -----------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Pin the SDK feature band to match local dev (rollForward: latestFeature lets
# the image's current 9.0.x SDK satisfy global.json).
COPY global.json ./

# Restore as its own layer for caching. Only the API project ships in the image;
# the test project is excluded on purpose.
# NOTE: if root-level build inputs are introduced (Directory.Build.props/.targets,
# Directory.Packages.props, NuGet.config), copy them here BEFORE restore so the
# container restore matches local/CI. None exist yet, and COPY errors on missing
# sources, so they are not listed pre-emptively.
COPY src/NbTcgTrader.Api/NbTcgTrader.Api.csproj src/NbTcgTrader.Api/
RUN dotnet restore src/NbTcgTrader.Api/NbTcgTrader.Api.csproj

COPY src/ src/
RUN dotnet publish src/NbTcgTrader.Api/NbTcgTrader.Api.csproj \
    -c Release -o /app/publish --no-restore

# ---- Runtime stage ---------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Run as the image's built-in non-root user (security, CLAUDE.md §15).
USER app

# HTTP only inside the container; HTTPS/HSTS terminate at the host/ingress.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "NbTcgTrader.Api.dll"]
