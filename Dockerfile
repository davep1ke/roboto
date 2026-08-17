# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, on just the project file, so dependency layers cache across code-only changes.
COPY src/Roboto.Bot/Roboto.Bot.csproj src/Roboto.Bot/
RUN dotnet restore src/Roboto.Bot/Roboto.Bot.csproj

COPY src/Roboto.Bot/ src/Roboto.Bot/
RUN dotnet publish src/Roboto.Bot/Roboto.Bot.csproj -c Release --no-restore -o /app

# Default runtime image is Debian-based - deliberately not the -alpine variant. musl + native deps
# (e.g. SkiaSharp for the stats charts, planned for a later phase) is a known source of pain there.
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app

# $APP_UID is a non-root user baked into the official .NET images for exactly this purpose.
RUN mkdir -p /data && chown $APP_UID /data
USER $APP_UID

COPY --from=build /app .

ENTRYPOINT ["dotnet", "Roboto.Bot.dll"]
