// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { VtsConnection } from '@/api/vts';
import type { HealthDot } from '@/views/dashboard/overlays/components/overlayHealth';

// A state of the VTube Studio link: the dot color and the i18n key (under dashboard.vts.status) of the words beside it.
export type VtsHealth = {
  dot: HealthDot;
  reason: string;
};

const BY_STATUS = {
  connected: { dot: 'ok', reason: 'connected' },
  authorized: { dot: 'warn', reason: 'authorized' },
  error: { dot: 'bad', reason: 'error' },
} as const satisfies Record<string, VtsHealth>;

// Switched off is gray; otherwise the bot's own status picks the dot, and anything it does not know needs the approval.
export function vtsHealth(connection: VtsConnection | null | undefined): VtsHealth {
  if (connection?.isEnabled !== true)
    return { dot: '', reason: 'off' };

  const known = BY_STATUS[connection.status as keyof typeof BY_STATUS] as VtsHealth | undefined;

  return known ?? { dot: 'warn', reason: 'unauthorized' };
}

// Models, hotkeys and expressions answer only while VTube Studio is linked.
export function vtsReady(connection: VtsConnection | null | undefined): boolean {
  return connection?.isEnabled === true && connection.status === 'connected';
}
