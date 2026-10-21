# SPDX-License-Identifier: AGPL-3.0-or-later
# Copyright (C) NoMercy Labs. All rights reserved.
#
# Multi-stage build — produces a single self-contained image:
#   dashboard-build : the Vue dashboard production bundle (Node; the image itself carries no Node)
#   restore         : .NET NuGet restore (separate stage so source changes don't bust the dep layer)
#   publish         : .NET publish
#   final           : ASP.NET runtime with the Vue bundle wired into wwwroot

# ---------------------------------------------------------------------------
# Stage 0 — Vue dashboard build
# ---------------------------------------------------------------------------
FROM node:22-bookworm-slim AS dashboard-build

WORKDIR /workspace/dashboard

# Package files first, so a source change does not bust the dependency layer.
# The npm download cache lives in a BuildKit cache mount and never enters an image layer.
COPY dashboard/package.json dashboard/package-lock.json ./
RUN --mount=type=cache,target=/root/.npm \
    npm ci --no-audit --no-fund

# The recap preview imports one widget file from the server tree, by its relative path
# (dashboard/src/views/dashboard/analytics/RecapPreview.vue), so that folder is copied to the same place.
COPY server/src/NomNomzBot.Infrastructure/Content/Widgets/Assets/ \
    /workspace/server/src/NomNomzBot.Infrastructure/Content/Widgets/Assets/

COPY dashboard/ ./

# Sign-in is a build-time switch (dashboard/src/lib/flags.ts). Off, the public pages show "Coming soon".
# This image is what dev and self-host users run, so it is built with sign-in open.
ENV VITE_SIGN_IN_OPEN=true

# build-only = `vite build` alone. The type check and the lint gate run in CI's dashboard job, not here,
# so a slow or red check cannot stop an image build that CI has already proven. Output: dist/.
RUN npm run build-only

# ---------------------------------------------------------------------------
# Stage 1 — .NET NuGet restore (cached layer; re-runs only on .csproj changes)
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS restore

WORKDIR /src

COPY server/Directory.Build.props    .
COPY server/Directory.Packages.props .
COPY server/src/NomNomzBot.Domain/NomNomzBot.Domain.csproj                         src/NomNomzBot.Domain/
COPY server/src/NomNomzBot.Application/NomNomzBot.Application.csproj               src/NomNomzBot.Application/
COPY server/src/NomNomzBot.Infrastructure/NomNomzBot.Infrastructure.csproj         src/NomNomzBot.Infrastructure/
COPY server/src/NomNomzBot.Migrations.Sqlite/NomNomzBot.Migrations.Sqlite.csproj   src/NomNomzBot.Migrations.Sqlite/
COPY server/src/NomNomzBot.Api/NomNomzBot.Api.csproj                               src/NomNomzBot.Api/

RUN dotnet restore src/NomNomzBot.Api/NomNomzBot.Api.csproj

# ---------------------------------------------------------------------------
# Stage 2 — .NET publish
# ---------------------------------------------------------------------------
FROM restore AS publish

ARG BUILD_CONFIGURATION=Release
# THIS is the Dockerfile CI builds (docker/build-push-action uses context "." with no `file:`, so the
# repo-root one wins — server/Dockerfile is the local-compose copy and is NOT what ships).
#
# GIT_SHA is stamped into the assembly's informational version so /health/version can answer the only
# question that matters after a deploy: WHICH COMMIT is this box running? Directory.Build.props pins
# <Version>0.1.0</Version>, which is identical in every image ever built, so without this the endpoint
# cannot distinguish a fresh deploy from a three-week-old one — and a green deploy job is not evidence.
ARG GIT_SHA=unknown

COPY server/ .

WORKDIR /src/src/NomNomzBot.Api
RUN dotnet publish NomNomzBot.Api.csproj \
    -c $BUILD_CONFIGURATION \
    -o /app/publish \
    /p:UseAppHost=false \
    /p:InformationalVersion="0.1.0+${GIT_SHA}"

# ---------------------------------------------------------------------------
# Stage 3 — Runtime image
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# esbuild native binary — the stage-B bundler for Vue/React widget compilation (a single static Go binary, no
# Node runtime). Version-pinned to the vendored @vue/compiler-sfc toolchain
# (server/src/NomNomzBot.Infrastructure/Content/Widgets/Vendor/README.md). Installed from the platform
# @esbuild package tarball onto PATH, so EsbuildWidgetBuildService's Widgets:EsbuildPath default ("esbuild")
# resolves. Without it, every vue/react widget compile fails with the "esbuild could not be started" install hint.
ARG ESBUILD_VERSION=0.28.1
ARG TARGETARCH
RUN set -eux; \
    case "${TARGETARCH:-amd64}" in \
      amd64) esbuild_pkg=linux-x64 ;; \
      arm64) esbuild_pkg=linux-arm64 ;; \
      *) echo "unsupported TARGETARCH: ${TARGETARCH}" >&2; exit 1 ;; \
    esac; \
    curl -fsSL "https://registry.npmjs.org/@esbuild/${esbuild_pkg}/-/${esbuild_pkg}-${ESBUILD_VERSION}.tgz" -o /tmp/esbuild.tgz; \
    tar -xzf /tmp/esbuild.tgz -C /tmp package/bin/esbuild; \
    install -m 0755 /tmp/package/bin/esbuild /usr/local/bin/esbuild; \
    rm -rf /tmp/esbuild.tgz /tmp/package; \
    esbuild --version

WORKDIR /app
EXPOSE 5000 5001

COPY --from=publish /app/publish .

# The Vue dashboard goes into wwwroot — the API's StaticFiles middleware serves it.
COPY --from=dashboard-build /workspace/dashboard/dist/ wwwroot/

ENV NOMNOMZ_DATA_DIR=/app/data
RUN mkdir -p /app/data && chown -R app:app /app
USER app
ENTRYPOINT ["dotnet", "NomNomzBot.Api.dll"]
