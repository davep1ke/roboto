# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, on just the project file, so dependency layers cache across code-only changes.
COPY Roboto/Roboto.csproj Roboto/
RUN dotnet restore Roboto/Roboto.csproj

COPY Roboto/ Roboto/

# /version (mod_standard) needs to know which commit it's actually running - passed in from the
# GHA workflow's real checkout (github.sha) rather than relying on git being available/accurate
# inside this build stage. .git is deliberately never COPYed into the build context (see
# .dockerignore), so Roboto.csproj's own git-rev-parse fallback (for local dev builds) can't see
# anything here anyway - GIT_COMMIT staying "unknown" is the correct, honest result for a manual
# `docker compose build` with no ARG passed.
ARG GIT_COMMIT=unknown
RUN dotnet publish Roboto/Roboto.csproj -c Release --no-restore -o /app -p:GitCommit=$GIT_COMMIT

# Default runtime image is Debian-based - deliberately not the -alpine variant. musl + native deps
# (SkiaSharp, ScottPlot's renderer for /statgraph) is a known source of pain there.
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app

# The base runtime image ships neither fontconfig nor any font files at all - SkiaSharp (ScottPlot's
# renderer, phase 6's /statgraph) silently draws no text when it can't find a font, rather than
# erroring, so every title/axis label/legend on a real chart would come back blank in production even
# though the same code produces a fully-labelled image everywhere it's dev-tested (a normal desktop
# Linux always has system fonts already installed). fonts-dejavu-core is small, Debian's own default
# sans-serif, and covers the plain ASCII this bot's stat names/labels ever use.
RUN apt-get update && apt-get install -y --no-install-recommends fontconfig fonts-dejavu-core \
    && rm -rf /var/lib/apt/lists/*

# $APP_UID is a non-root user baked into the official .NET images for exactly this purpose.
RUN mkdir -p /data && chown $APP_UID /data
USER $APP_UID

COPY --from=build /app .

ENTRYPOINT ["dotnet", "Roboto.dll"]
