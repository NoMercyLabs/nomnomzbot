# -----------------------------------------------------------------------------
#  Copyright (c) NoMercy Labs.
#
#  This file is part of NomNomzBot, free software licensed under the GNU Affero
#  General Public License v3.0 or later. You may redistribute and/or modify it
#  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
#
#  SPDX-License-Identifier: AGPL-3.0-or-later
# -----------------------------------------------------------------------------
#
# The DEVELOPMENT environment, not a shipping image. The bot's image is the repo-root Dockerfile.

FROM mcr.microsoft.com/dotnet/sdk:10.0

ARG TARGETARCH=amd64
ARG PWSH_VERSION=7.5.4
ARG NODE_MAJOR=22
ARG USERNAME=dev
ARG USER_UID=1000
ARG USER_GID=1000

ENV DEBIAN_FRONTEND=noninteractive \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    JAVA_HOME=/usr/lib/jvm/java-21-openjdk-${TARGETARCH} \
    GRADLE_USER_HOME=/home/${USERNAME}/.gradle \
    NOMNOMZ_DEVBOX=1

# --- OS packages -------------------------------------------------------------
RUN apt-get update && apt-get install -y --no-install-recommends \
        ca-certificates curl wget gnupg lsb-release \
        git git-lfs openssh-client openssh-server \
        openjdk-21-jdk-headless \
        build-essential unzip zip jq less nano vim procps htop \
        iproute2 lsof \
        sudo locales tzdata rsync \
    && rm -rf /var/lib/apt/lists/*

RUN sed -i 's/^# *\(en_US.UTF-8\)/\1/' /etc/locale.gen && locale-gen
ENV LANG=en_US.UTF-8 LC_ALL=en_US.UTF-8

# --- Node (widget / Vue SFC tooling) ----------------------------------------
RUN curl -fsSL https://deb.nodesource.com/setup_${NODE_MAJOR}.x | bash - \
    && apt-get install -y --no-install-recommends nodejs \
    && rm -rf /var/lib/apt/lists/* \
    && npm install -g npm@latest

# --- GitHub CLI (the CI gate uses `gh run watch`) ----------------------------
RUN curl -fsSL https://cli.github.com/packages/githubcli-archive-keyring.gpg \
        -o /usr/share/keyrings/githubcli-archive-keyring.gpg \
    && chmod go+r /usr/share/keyrings/githubcli-archive-keyring.gpg \
    && echo "deb [arch=${TARGETARCH} signed-by=/usr/share/keyrings/githubcli-archive-keyring.gpg] https://cli.github.com/packages stable main" \
        > /etc/apt/sources.list.d/github-cli.list \
    && apt-get update && apt-get install -y --no-install-recommends gh \
    && rm -rf /var/lib/apt/lists/*

# --- Docker CLI ONLY ---------------------------------------------------------
# CLI only - the host socket is mounted, so containers started here are siblings.
RUN install -m 0755 -d /etc/apt/keyrings \
    && curl -fsSL https://download.docker.com/linux/debian/gpg -o /etc/apt/keyrings/docker.asc \
    && chmod a+r /etc/apt/keyrings/docker.asc \
    && echo "deb [arch=${TARGETARCH} signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/$(. /etc/os-release && echo $ID) $(. /etc/os-release && echo ${UBUNTU_CODENAME:-$VERSION_CODENAME}) stable" \
        > /etc/apt/sources.list.d/docker.list \
    && apt-get update && apt-get install -y --no-install-recommends docker-ce-cli docker-compose-plugin docker-buildx-plugin \
    && rm -rf /var/lib/apt/lists/*

# --- PowerShell 7 (scripts/slice-check.ps1, ship.ps1, switchover.ps1) --------
RUN set -eux; \
    case "${TARGETARCH}" in \
      amd64) pwsh_arch=x64 ;; \
      arm64) pwsh_arch=arm64 ;; \
      *) echo "unsupported arch ${TARGETARCH}" >&2; exit 1 ;; \
    esac; \
    curl -fsSL "https://github.com/PowerShell/PowerShell/releases/download/v${PWSH_VERSION}/powershell-${PWSH_VERSION}-linux-${pwsh_arch}.tar.gz" -o /tmp/pwsh.tar.gz; \
    mkdir -p /opt/microsoft/powershell/7; \
    tar zxf /tmp/pwsh.tar.gz -C /opt/microsoft/powershell/7; \
    chmod +x /opt/microsoft/powershell/7/pwsh; \
    ln -sf /opt/microsoft/powershell/7/pwsh /usr/bin/pwsh; \
    rm /tmp/pwsh.tar.gz

# --- cloudflared (HTTPS tunnel for Twitch OAuth redirects during local dev) --
RUN curl -fsSL "https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-linux-${TARGETARCH}" \
        -o /usr/local/bin/cloudflared \
    && chmod +x /usr/local/bin/cloudflared

# --- code-server -------------------------------------------------------------
RUN curl -fsSL https://code-server.dev/install.sh | sh

# --- Claude Code CLI ---------------------------------------------------------
# The postinstall that fetches the native binary does not reliably run for a global install in a
# build layer, so run it explicitly and assert the CLI answers.
RUN npm install -g @anthropic-ai/claude-code \
    && node "$(npm root -g)/@anthropic-ai/claude-code/install.cjs" \
    && claude --version

# --- unprivileged user -------------------------------------------------------
# Match the host UID/GID or bind-mounted files come out root-owned.
RUN if getent group ${USER_GID} >/dev/null; then groupmod -n ${USERNAME} "$(getent group ${USER_GID} | cut -d: -f1)"; else groupadd -g ${USER_GID} ${USERNAME}; fi \
    && if getent passwd ${USER_UID} >/dev/null; then usermod -l ${USERNAME} -d /home/${USERNAME} -m "$(getent passwd ${USER_UID} | cut -d: -f1)"; else useradd -m -u ${USER_UID} -g ${USER_GID} -s /bin/bash ${USERNAME}; fi \
    && echo "${USERNAME} ALL=(ALL) NOPASSWD:ALL" > /etc/sudoers.d/${USERNAME} \
    && chmod 0440 /etc/sudoers.d/${USERNAME} \
    && mkdir -p /home/${USERNAME}/.local/share/code-server \
                /home/${USERNAME}/.nuget /home/${USERNAME}/.gradle \
                /home/${USERNAME}/.dotnet /home/${USERNAME}/.config \
                /home/${USERNAME}/.ssh/sshd /home/${USERNAME}/.vscode-server \
                /home/${USERNAME}/artifacts \
                /workspace \
    && chmod 700 /home/${USERNAME}/.ssh \
    && chown -R ${USER_UID}:${USER_GID} /home/${USERNAME} /workspace

COPY --chown=${USER_UID}:${USER_GID} settings/ /opt/devbox/settings/
COPY entrypoint.sh /usr/local/bin/devbox-entrypoint
COPY auth-onboarding.sh /usr/local/bin/devbox-auth
RUN chmod +x /usr/local/bin/devbox-entrypoint /usr/local/bin/devbox-auth \
    && printf '%s\n' \
        '[ -t 1 ] && [ ! -f /tmp/.devbox-auth-shown ] && { touch /tmp/.devbox-auth-shown 2>/dev/null; devbox-auth; }' \
        'true' \
        > /etc/profile.d/10-devbox-auth.sh

USER ${USERNAME}
WORKDIR /workspace
ENV PATH="/home/${USERNAME}/.dotnet/tools:${PATH}"

# 8080 code-server · 2222 sshd (VS Code Remote-SSH) · 5080 API · 5090 dashboard dev server
EXPOSE 8080 2222 5080 5090

ENTRYPOINT ["/usr/local/bin/devbox-entrypoint"]
