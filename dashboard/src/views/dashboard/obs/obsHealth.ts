// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { ObsBridgeStatus, ObsConnection } from '@/api/obs';
import type { HealthDot } from '@/views/dashboard/overlays/components/overlayHealth';

// A state of the OBS page: the dot color and the i18n key (under dashboard.obs) of the words beside it.
export type Health = {
  dot: HealthDot;
  reason: string;
};

// Red beats orange beats green beats the gray "off", so one glance at the header finds a problem.
const SEVERITY = {
  bad: 3,
  warn: 2,
  ok: 1,
  '': 0,
} as const satisfies Record<HealthDot, number>;

export function worstDot(dots: readonly HealthDot[]): HealthDot {
  return dots.reduce<HealthDot>(
    (worst: HealthDot, next: HealthDot) =>
      SEVERITY[next] > SEVERITY[worst]
        ? next
        : worst,
    '',
  );
}

// The saved connection, plus whether OBS answered a live read: switched off is gray, an error red, silent orange.
export function connectionHealth(
  connection: ObsConnection | null | undefined,
  unreachable: boolean,
): Health {
  if (!connection?.isEnabled)
    return { dot: '', reason: 'connection.off' };

  if (connection.lastError)
    return { dot: 'bad', reason: 'connection.error' };

  return unreachable
    ? { dot: 'warn', reason: 'connection.unreachable' }
    : { dot: 'ok', reason: 'connection.connected' };
}

// The browser-source bridge: orange while no bridge page is open in OBS, or none leads.
export function bridgeHealth(
  connection: ObsConnection | null | undefined,
  bridge: ObsBridgeStatus | null | undefined,
): Health {
  if (!connection?.isEnabled)
    return { dot: '', reason: 'bridge.off' };

  if (!bridge || Number(bridge.instanceCount) === 0)
    return { dot: 'warn', reason: 'bridge.none' };

  return bridge.hasLeader
    ? { dot: 'ok', reason: 'bridge.leader' }
    : { dot: 'warn', reason: 'bridge.noLeader' };
}
