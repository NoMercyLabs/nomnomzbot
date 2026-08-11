// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import streamDeck from "@elgato/streamdeck";
import type { JsonObject } from "@elgato/utils";

/** The self-hosted default (stream-deck.md D9) — this project's primary use case runs the bot on the
 * same machine or LAN, so the plugin should work with zero configuration for that golden path. */
export const DEFAULT_HOST = "http://localhost:5080";

/** Global-settings shape (stream-deck.md D8/D9) — one pairing, shared by every key. */
export interface PairingState extends JsonObject {
  backendUrl: string;
  token: string;
  tokenExpiresAt: string; // ISO-8601
  deviceKind: string;
}

interface GlobalSettings extends Partial<PairingState> {
  host?: string;
}

const REFRESH_THRESHOLD_MS = 7 * 24 * 60 * 60 * 1000; // 7 days (D8)

let cached: PairingState | null | undefined;

export async function getPairingState(): Promise<PairingState | null> {
  if (cached !== undefined) return cached;
  const settings = await streamDeck.settings.getGlobalSettings<GlobalSettings>();
  if (!settings.backendUrl || !settings.token || !settings.tokenExpiresAt) {
    cached = null;
    return null;
  }
  cached = {
    backendUrl: settings.backendUrl,
    token: settings.token,
    tokenExpiresAt: settings.tokenExpiresAt,
    deviceKind: settings.deviceKind ?? "streamdeck",
  };
  return cached;
}

export async function setPairingState(state: PairingState): Promise<void> {
  cached = state;
  const settings = await streamDeck.settings.getGlobalSettings<GlobalSettings>();
  await streamDeck.settings.setGlobalSettings({ ...settings, ...state });
}

export async function clearPairingState(): Promise<void> {
  cached = null;
  const settings = await streamDeck.settings.getGlobalSettings<GlobalSettings>();
  await streamDeck.settings.setGlobalSettings({ host: settings.host });
}

/** The backend host the device flow talks to — a per-installation setting, not per-key. */
export async function getHost(): Promise<string> {
  const settings = await streamDeck.settings.getGlobalSettings<GlobalSettings>();
  return settings.host || DEFAULT_HOST;
}

/** Bumped on every {@link setHost} call — pairing.ts polls this to notice a host edit mid-flow
 * (e.g. while waiting on approval against the old host) within one poll interval, not up to 10 min. */
let hostGeneration = 0;
export function getHostGeneration(): number {
  return hostGeneration;
}

export async function setHost(host: string): Promise<void> {
  const settings = await streamDeck.settings.getGlobalSettings<GlobalSettings>();
  await streamDeck.settings.setGlobalSettings({ ...settings, host });
  hostGeneration++;
}

/** True once the token is under the D8 proactive-refresh threshold (or already past expiry). */
export function needsRefresh(state: PairingState, now: Date = new Date()): boolean {
  const expiresAt = Date.parse(state.tokenExpiresAt);
  return expiresAt - now.getTime() < REFRESH_THRESHOLD_MS;
}

export function isExpired(state: PairingState, now: Date = new Date()): boolean {
  return Date.parse(state.tokenExpiresAt) <= now.getTime();
}
