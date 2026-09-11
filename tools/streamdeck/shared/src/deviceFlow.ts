// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import open from "open";
import type { PairingState } from "./tokenStore.js";

/** The project-wide response envelope (StatusResponseDto&lt;T&gt;): {@code status: "ok"|"error"},
 * never a {@code success} boolean — that field doesn't exist on the wire. */
interface DeviceInitResponse {
  status: string;
  data?: {
    deviceCode: string;
    userCode: string;
    verificationUri: string;
    expiresAt: string;
    pollIntervalSeconds: number;
  };
}

interface DevicePollResponse {
  status: string;
  data?: {
    status: "pending" | "approved";
    backendUrl?: string;
    token?: string;
    scopes?: string[];
    tokenExpiresAt?: string;
  };
}

export interface DeviceInitResult {
  deviceCode: string;
  userCode: string;
  verificationUri: string;
  expiresAt: Date;
  pollIntervalMs: number;
}

/** Set on every init/poll failure so the property inspector can show WHY it's retrying instead of a
 * silent "starting…" forever — read this immediately after a null/"pending"-less result. */
let lastError: string | null = null;
export function getLastDeviceFlowError(): string | null {
  return lastError;
}

/** Strips a trailing slash a user is bound to type ("https://dev.nomnomz.bot/") — left in place, it
 * turns every request path into a double slash (".bot//automation/...") which the backend answers
 * with a bare 405 instead of routing, a confusing failure for what's just a copy-pasted URL. */
function normalizeHost(host: string): string {
  return host.replace(/\/+$/, "");
}

/**
 * Device-initiated pairing, step 1 (stream-deck.md D9): the plugin calls the backend itself — no
 * dashboard interaction. Returns null on any network/backend failure (unreachable host, wrong port);
 * the caller retries on its own schedule rather than surfacing a raw exception.
 */
export async function initDevicePairing(hostInput: string): Promise<DeviceInitResult | null> {
  const host = normalizeHost(hostInput);
  try {
    const res = await fetch(`${host}/automation/v1/pair/device/init`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ device: { kind: "streamdeck", name: null } }),
    });
    const body = (await res.json().catch(() => null)) as DeviceInitResponse | null;
    if (!res.ok || body?.status !== "ok" || !body.data) {
      lastError = `Backend at ${host} answered ${res.status} — is the host correct?`;
      return null;
    }
    lastError = null;
    return {
      deviceCode: body.data.deviceCode,
      userCode: body.data.userCode,
      verificationUri: body.data.verificationUri,
      expiresAt: new Date(body.data.expiresAt),
      pollIntervalMs: body.data.pollIntervalSeconds * 1000,
    };
  } catch (error) {
    lastError = `Can't reach ${host} (${error instanceof Error ? error.message : "network error"})`;
    return null;
  }
}

/** "pending" while unapproved, the minted pairing state once approved, or null on a hard failure
 * (expired/unknown device code) meaning the caller must start over with a fresh {@link initDevicePairing}. */
export async function pollDevicePairing(
  hostInput: string,
  deviceCode: string,
): Promise<"pending" | PairingState | null> {
  const host = normalizeHost(hostInput);
  try {
    const res = await fetch(`${host}/automation/v1/pair/device/poll`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ deviceCode }),
    });
    const body = (await res.json().catch(() => null)) as DevicePollResponse | null;
    if (body?.status !== "ok" || !body.data) {
      lastError = "The pairing request expired — starting a new one.";
      return null;
    }
    if (body.data.status === "pending") return "pending";
    if (!body.data.token || !body.data.backendUrl || !body.data.tokenExpiresAt) {
      lastError = "Backend approved the device but the response was malformed.";
      return null;
    }
    lastError = null;
    return {
      backendUrl: body.data.backendUrl,
      token: body.data.token,
      tokenExpiresAt: body.data.tokenExpiresAt,
      deviceKind: "streamdeck",
    };
  } catch (error) {
    lastError = `Can't reach ${host} (${error instanceof Error ? error.message : "network error"})`;
    return null;
  }
}

/** Best-effort: opens the verification URL in the operator's default browser so approving the
 * device is a single click, never a copy-paste. Silently does nothing if it fails (headless host,
 * sandboxed environment) — the URL is still shown in the property inspector either way. */
export function openInBrowser(url: string): void {
  void open(url).catch(() => {
    /* best-effort */
  });
}
