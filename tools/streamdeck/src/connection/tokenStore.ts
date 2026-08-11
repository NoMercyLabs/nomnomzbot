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

/** Global-settings shape (stream-deck.md D7/D8) — one pairing, shared by every key. */
export interface PairingState extends JsonObject {
  backendUrl: string;
  token: string;
  tokenExpiresAt: string; // ISO-8601
  deviceKind: string;
}

const REFRESH_THRESHOLD_MS = 7 * 24 * 60 * 60 * 1000; // 7 days (D8)

let cached: PairingState | null | undefined;

export async function getPairingState(): Promise<PairingState | null> {
  if (cached !== undefined) return cached;
  const settings = await streamDeck.settings.getGlobalSettings<Partial<PairingState>>();
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
  await streamDeck.settings.setGlobalSettings(state);
}

export async function clearPairingState(): Promise<void> {
  cached = null;
  await streamDeck.settings.setGlobalSettings({});
}

/** True once the token is under the D8 proactive-refresh threshold (or already past expiry). */
export function needsRefresh(state: PairingState, now: Date = new Date()): boolean {
  const expiresAt = Date.parse(state.tokenExpiresAt);
  return expiresAt - now.getTime() < REFRESH_THRESHOLD_MS;
}

export function isExpired(state: PairingState, now: Date = new Date()): boolean {
  return Date.parse(state.tokenExpiresAt) <= now.getTime();
}
