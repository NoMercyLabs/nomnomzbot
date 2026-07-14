# SPDX-License-Identifier: AGPL-3.0-or-later
# Copyright (C) NoMercy Labs. All rights reserved.
#
# Multi-stage build — produces a single self-contained image:
#   wasm-build  : Kotlin Multiplatform + Compose Wasm production bundle
#   restore     : .NET NuGet restore (separate stage so source changes don't bust the dep layer)
#   publish     : .NET publish
#   final       : ASP.NET runtime with Wasm wired into wwwroot

# ---------------------------------------------------------------------------
# Stage 0 — Kotlin/Compose Wasm frontend build
# ---------------------------------------------------------------------------
FROM eclipse-temurin:21-jdk-jammy AS wasm-build

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /workspace

# Copy Gradle wrapper + build files first so changes to Kotlin source don't
# bust the dependency-resolution layer. Toolchain archives (Node, Yarn,
# Binaryen/wasm-opt) and downloaded JARs land in ~/.gradle, which is
# persisted via BuildKit cache mount and never enters the image layer.
COPY app/gradle/             gradle/
COPY app/gradlew             gradlew
COPY app/gradle.properties   gradle.properties
COPY app/settings.gradle.kts settings.gradle.kts
COPY app/build.gradle.kts    build.gradle.kts
COPY app/composeApp/build.gradle.kts composeApp/build.gradle.kts

# Pre-warm: resolve Gradle/Kotlin dependencies before copying source so
# this layer is a cache hit whenever only Kotlin source changes.
RUN --mount=type=cache,target=/root/.gradle,sharing=locked \
    --mount=type=cache,target=/root/.konan \
    chmod +x gradlew \
    && ./gradlew :composeApp:wasmJsBrowserDistribution --dry-run --no-daemon -q 2>/dev/null || true

# Copy full source. Re-apply +x after COPY in case the checkout platform
# (e.g. Windows NTFS) stripped the execute bit — git index has 100755 but
# Docker copies the on-disk mode, which may differ on Windows hosts.
COPY app/ .
RUN chmod +x gradlew

# wasmJsBrowserDistribution = webpack + resource copy + index.html → dist/wasmJs/productionExecutable/
# --rerun-tasks is REQUIRED: the persistent /root/.gradle build-cache mount makes Kotlin/Wasm incremental
# up-to-date checks unreliable across CI builds — without it, a source change (e.g. only ShellScreen.kt)
# is silently skipped and the bundle ships stale. Forcing a full task re-run guarantees the wasm reflects
# the checked-out source; the dependency/konan caches still avoid re-downloading, so the cost is one clean
# compile + webpack pass.
RUN --mount=type=cache,target=/root/.gradle,sharing=locked \
    --mount=type=cache,target=/root/.konan \
    ./gradlew :composeApp:wasmJsBrowserDistribution --no-daemon --rerun-tasks

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

COPY server/ .

WORKDIR /src/src/NomNomzBot.Api
RUN dotnet publish NomNomzBot.Api.csproj \
    -c $BUILD_CONFIGURATION \
    -o /app/publish \
    /p:UseAppHost=false

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

# Wasm dashboard goes into wwwroot — the API's StaticFiles middleware serves it.
COPY --from=wasm-build \
    /workspace/composeApp/build/dist/wasmJs/productionExecutable/ \
    wwwroot/

ENV NOMNOMZ_DATA_DIR=/app/data
RUN mkdir -p /app/data && chown -R app:app /app
USER app
ENTRYPOINT ["dotnet", "NomNomzBot.Api.dll"]
