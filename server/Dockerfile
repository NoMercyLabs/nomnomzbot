# SPDX-License-Identifier: AGPL-3.0-or-later
# Copyright (C) NoMercy Labs. All rights reserved.

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
EXPOSE 5000
EXPOSE 5001

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["Directory.Build.props", "."]
COPY ["Directory.Packages.props", "."]
COPY ["src/NomNomzBot.Domain/NomNomzBot.Domain.csproj", "src/NomNomzBot.Domain/"]
COPY ["src/NomNomzBot.Application/NomNomzBot.Application.csproj", "src/NomNomzBot.Application/"]
COPY ["src/NomNomzBot.Infrastructure/NomNomzBot.Infrastructure.csproj", "src/NomNomzBot.Infrastructure/"]
COPY ["src/NomNomzBot.Api/NomNomzBot.Api.csproj", "src/NomNomzBot.Api/"]

RUN dotnet restore "src/NomNomzBot.Api/NomNomzBot.Api.csproj"

COPY . .
WORKDIR "/src/src/NomNomzBot.Api"
RUN dotnet build "NomNomzBot.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "NomNomzBot.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
# The aspnet base image ships a pre-created non-root 'app' user (UID 1654). Own /app so the app can
# still write its rolling logs under logs/, then drop to it — an RCE in the app or a dependency no
# longer lands as container root (§10).
# Runtime data (logs, SQLite for lite, OS-vault key files) lives in one folder; the default is the running user's
# profile data dir (~/.local/share on Linux), which the non-root 'app' user may not have writable, so point it at
# a writable, mountable dir.
ENV NOMNOMZ_DATA_DIR=/app/data
RUN mkdir -p /app/data && chown -R app:app /app
USER app
ENTRYPOINT ["dotnet", "NomNomzBot.Api.dll"]
