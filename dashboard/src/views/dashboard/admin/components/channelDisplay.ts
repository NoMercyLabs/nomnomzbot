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

// How a channel looks wherever the admin panel shows one: the list row and the console header read the same
// status tone and the same initials.
import type { StatusTone } from '@/components/layout/StatusBadge.vue';
import { lookup } from '@/lib/array';

const TONES = {
  active: 'ok',
  suspended: 'off',
  churned: 'off',
  platform_banned: 'bad',
} as const satisfies Readonly<Record<string, StatusTone>>;

export function statusTone(status: string | undefined): StatusTone {
  return lookup(TONES, status) ?? 'off';
}

export function initialsOf(name: string | undefined): string {
  return (name ?? '')
    .replace(/[^A-Za-z0-9]/g, '')
    .slice(0, 2)
    .toUpperCase();
}
