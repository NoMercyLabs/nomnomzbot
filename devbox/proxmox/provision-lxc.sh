#!/usr/bin/env bash
# -----------------------------------------------------------------------------
#  Copyright (c) NoMercy Labs.
#
#  This file is part of NomNomzBot. You may use, change and share it for free,
#  for any noncommercial purpose, under the PolyForm Noncommercial License
#  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
#  See LICENSE for details, and TRADEMARKS.md for the name and logo.
#
#  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
# -----------------------------------------------------------------------------
#
# Creates the NomNomzBot devbox as its own LXC on a Proxmox host, and starts the devbox image
# (devbox/docker-compose.yml) inside it. Run it ON the Proxmox host, as root. Safe to run again:
# every step checks first and skips what is already done.
#
#   AUTHORIZED_KEY="$(cat key.pub)" bash provision-lxc.sh
#
# Settings (environment, all optional):
#   VMID=121 NAME=nnz-devbox CORES=8 MEMORY_MB=16384 DISK_GB=120 STORAGE=local-lvm BRIDGE=vmbr0
#   TEMPLATE=local:vztmpl/ubuntu-24.04-standard_24.04-2_amd64.tar.zst
#   REPO_URL=https://github.com/NoMercyLabs/nomnomzbot.git
set -euo pipefail

VMID="${VMID:-121}"
NAME="${NAME:-nnz-devbox}"
CORES="${CORES:-8}"
MEMORY_MB="${MEMORY_MB:-16384}"
DISK_GB="${DISK_GB:-120}"
STORAGE="${STORAGE:-local-lvm}"
BRIDGE="${BRIDGE:-vmbr0}"
TEMPLATE="${TEMPLATE:-local:vztmpl/ubuntu-24.04-standard_24.04-2_amd64.tar.zst}"
REPO_URL="${REPO_URL:-https://github.com/NoMercyLabs/nomnomzbot.git}"
REPO_DIR=/srv/nomnomzbot

step() { echo "== $* =="; }
in_ct() { pct exec "$VMID" -- bash -lc "$1"; }

# LXC 120 belongs to something else on this host and is never touched (Stoney, 2026-11-24).
[ "$VMID" != "120" ] || { echo "VMID 120 is off limits. Pick another VMID." >&2; exit 1; }

if pct status "$VMID" >/dev/null 2>&1; then
    existing="$(pct config "$VMID" | awk '/^hostname:/{print $2}')"
    [ "$existing" = "$NAME" ] || { echo "VMID $VMID is taken by '$existing'. Pick another VMID." >&2; exit 1; }
    step "container $VMID ($NAME) exists"
else
    step "create container $VMID ($NAME)"
    pct create "$VMID" "$TEMPLATE" \
        --hostname "$NAME" --cores "$CORES" --memory "$MEMORY_MB" --swap 4096 \
        --rootfs "$STORAGE:$DISK_GB" --net0 "name=eth0,bridge=$BRIDGE,ip=dhcp,type=veth" \
        --unprivileged 1 --features nesting=1,keyctl=1 --onboot 1 --ostype ubuntu \
        --description "NomNomzBot devbox: builds, tests and gates for Arc (Stoney 2026-11-24)"
fi

[ "$(pct status "$VMID" | awk '{print $2}')" = running ] || { step "start"; pct start "$VMID"; }

step "wait for network"
for _ in $(seq 1 30); do in_ct 'getent hosts deb.debian.org >/dev/null' && break; sleep 2; done

step "base packages and Docker"
in_ct 'command -v docker >/dev/null || { apt-get update -qq && apt-get install -y -qq curl git ca-certificates openssh-server rsync && curl -fsSL https://get.docker.com | sh; }'
in_ct 'systemctl enable --now docker ssh >/dev/null'

if [ -n "${AUTHORIZED_KEY:-}" ]; then
    step "authorize the key"
    in_ct "install -d -m 700 /root/.ssh && touch /root/.ssh/authorized_keys && chmod 600 /root/.ssh/authorized_keys && grep -qxF '$AUTHORIZED_KEY' /root/.ssh/authorized_keys || echo '$AUTHORIZED_KEY' >> /root/.ssh/authorized_keys"
fi

step "clone the repo"
in_ct "[ -d $REPO_DIR/.git ] || git clone $REPO_URL $REPO_DIR"
in_ct "git config --global --add safe.directory $REPO_DIR"

step "devbox image"
# The editor holds the Docker socket, so it never runs without a password. One is made in the box and stays
# there (read it with: pct exec $VMID -- grep PASSWORD $REPO_DIR/devbox/.env).
in_ct "cd $REPO_DIR && { grep -q '^DEVBOX_PASSWORD=.' devbox/.env 2>/dev/null || echo \"DEVBOX_PASSWORD=\$(openssl rand -hex 16)\" >> devbox/.env; }"
in_ct "cd $REPO_DIR && DEVBOX_MEM_LIMIT=$((MEMORY_MB - 2048))m DEVBOX_GITCONFIG=/dev/null docker compose -f devbox/docker-compose.yml up -d --build"

step "ready"
in_ct 'hostname -I'
