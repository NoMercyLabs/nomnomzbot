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

import type { VtsConnection } from '@/api/vts';
import { lookup } from '@/lib/array';
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

  const known: VtsHealth | undefined = lookup(BY_STATUS, connection.status);

  return known ?? { dot: 'warn', reason: 'unauthorized' };
}

// Models, hotkeys and expressions answer only while VTube Studio is linked.
export function vtsReady(connection: VtsConnection | null | undefined): boolean {
  return connection?.isEnabled === true && connection.status === 'connected';
}
