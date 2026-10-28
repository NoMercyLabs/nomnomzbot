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

import type { PersonGrant } from '@/api/permissions';

// A grant the person holds now; the bot stores a given capability as "allow" (older rows as "1").
export const isAllowedGrant = (row: PersonGrant): boolean =>
  row.permissionValue === 'allow' || row.permissionValue === '1';
