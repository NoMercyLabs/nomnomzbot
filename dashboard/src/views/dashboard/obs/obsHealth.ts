// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { ObsBridgeStatus, ObsConnectionStatus } from '@/api/obs';
import type { Widget } from '@/api/widgets';
import type { HealthDot } from '@/views/dashboard/overlays/components/overlayHealth';

// The connection as far as the viewer may see it: the status always, the last error only with the configuration.
export type ObsConnectionSummary = ObsConnectionStatus & { lastError?: string | null };

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
    (worst: HealthDot, next) =>
      SEVERITY[next] > SEVERITY[worst]
        ? next
        : worst,
    '',
  );
}

// The saved connection, plus whether OBS answered a live read: switched off is gray, an error red, silent orange.
export function connectionHealth(
  connection: ObsConnectionSummary | null | undefined,
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
  connection: ObsConnectionSummary | null | undefined,
  bridge: ObsBridgeStatus | null | undefined,
): Health {
  if (!connection?.isEnabled)
    return { dot: '', reason: 'bridge.off' };

  if (!bridge || Number(bridge.instanceCount) === 0) {
    // In direct mode OBS is driven by its own socket; the bridge page only serves browser sources.
    return connection.mode === 'bridge'
      ? { dot: 'warn', reason: 'bridge.none' }
      : { dot: '', reason: 'bridge.optional' };
  }

  return bridge.hasLeader
    ? { dot: 'ok', reason: 'bridge.leader' }
    : { dot: 'warn', reason: 'bridge.noLeader' };
}

// The i18n key of the words for one browser source: switched off, failed, loaded in OBS or not.
export function sourceReasonKey(
  widget: Pick<Widget, 'isEnabled' | 'lastRuntimeError' | 'isAttached'>,
): string {
  if (!widget.isEnabled)
    return 'dashboard.obs.sources.off';

  if (widget.lastRuntimeError)
    return 'dashboard.obs.sources.failed';

  return widget.isAttached
    ? 'dashboard.obs.sources.loaded'
    : 'dashboard.obs.sources.notLoaded';
}
