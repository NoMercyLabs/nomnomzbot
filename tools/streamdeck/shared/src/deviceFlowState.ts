// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
