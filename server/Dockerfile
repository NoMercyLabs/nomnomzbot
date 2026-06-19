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
ENTRYPOINT ["dotnet", "NomNomzBot.Api.dll"]
