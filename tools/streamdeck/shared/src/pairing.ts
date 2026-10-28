// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

import streamDeck from "@elgato/streamdeck";
import { getPairingState } from "./tokenStore.js";
import { openAuthWindow } from "./authWindow.js";

/**
 * Entry point for the device-pairing flow (stream-deck.md D9, revised): the plugin never pairs
 * silently in the background. Instead, the moment it's unpaired, it spawns its own local page in the
 * operator's browser (authWindow.ts) — a host field defaulting to the self-hosted golden path and an
 * "Authorize" button — and that page drives the rest of the handshake. Already-paired installs are a
 * no-op.
 */
export async function runDeviceFlowLoop(onPaired: () => void): Promise<void> {
  if (await getPairingState()) {
    streamDeck.logger.info("Device flow: already paired, nothing to do.");
    return;
  }
  await openAuthWindow(onPaired);
}
