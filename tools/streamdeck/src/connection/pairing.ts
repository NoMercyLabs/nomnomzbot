// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { setPairingState, getPairingState, getHost, getHostGeneration } from "./tokenStore.js";
import { automationClient } from "./automationClient.js";
import { initDevicePairing, pollDevicePairing, openInBrowser, getLastDeviceFlowError } from "./deviceFlow.js";
import { setDeviceFlowStatus } from "./deviceFlowState.js";

const RETRY_ON_FAILURE_MS = 5000;

/**
 * The plugin-initiated device flow (stream-deck.md D9), run for the lifetime of the process: while
 * unpaired, repeatedly init → open the verification link in the operator's browser → poll until
 * approved or expired, then start over. Nothing is ever typed into the plugin itself — the host field
 * (the Connection key's Property Inspector) is the only manual input, and it defaults to the
 * self-hosted golden path.
 */
export async function runDeviceFlowLoop(onPaired: () => void): Promise<void> {
  for (;;) {
    if (await getPairingState()) {
      setDeviceFlowStatus({ paired: true, verificationUri: null, tokenExpiresAt: null, lastError: null });
      return; // Already paired (e.g. a prior run of this loop succeeded) — nothing left to do.
    }

    const host = await getHost();
    const startedAtGeneration = getHostGeneration();
    const init = await initDevicePairing(host);
    if (!init) {
      setDeviceFlowStatus({
        paired: false,
        verificationUri: null,
        tokenExpiresAt: null,
        lastError: getLastDeviceFlowError(),
      });
      await sleep(RETRY_ON_FAILURE_MS);
      continue;
    }

    setDeviceFlowStatus({
      paired: false,
      verificationUri: init.verificationUri,
      tokenExpiresAt: null,
      lastError: null,
    });
    openInBrowser(init.verificationUri);

    const approved = await pollUntilApprovedOrExpired(host, init, startedAtGeneration);
    if (approved) {
      await setPairingState(approved);
      await automationClient.connectStream();
      setDeviceFlowStatus({
        paired: true,
        verificationUri: null,
        tokenExpiresAt: approved.tokenExpiresAt,
        lastError: null,
      });
      onPaired();
      return;
    }
    // Expired, rejected, or the host changed mid-wait — restart with a fresh init against the
    // (possibly new) host.
  }
}

async function pollUntilApprovedOrExpired(
  host: string,
  init: Awaited<ReturnType<typeof initDevicePairing>> & object,
  startedAtGeneration: number,
) {
  while (Date.now() < init.expiresAt.getTime()) {
    await sleep(init.pollIntervalMs);
    if (getHostGeneration() !== startedAtGeneration) return null; // Host edited — abandon this attempt.
    const result = await pollDevicePairing(host, init.deviceCode);
    if (result === "pending") continue;
    return result; // PairingState on success, null on hard failure — both end this attempt.
  }
  return null;
}

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}
