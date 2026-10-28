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

/** What the property inspector shows while the background device-flow loop (pairing.ts) runs. */
export interface DeviceFlowStatus {
  paired: boolean;
  verificationUri: string | null;
  tokenExpiresAt: string | null;
  /** Human-readable reason the last init/poll attempt failed, so "Starting pairing…" never sits
   * silent forever — an unreachable host or a stale/wrong one is a real, actionable state to show. */
  lastError: string | null;
}

let current: DeviceFlowStatus = {
  paired: false,
  verificationUri: null,
  tokenExpiresAt: null,
  lastError: null,
};
const listeners = new Set<(status: DeviceFlowStatus) => void>();

export function getDeviceFlowStatus(): DeviceFlowStatus {
  return current;
}

export function setDeviceFlowStatus(status: DeviceFlowStatus): void {
  current = status;
  for (const listener of listeners) listener(current);
}

export function onDeviceFlowStatusChange(listener: (status: DeviceFlowStatus) => void): void {
  listeners.add(listener);
}
