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

import { describe, expect, it } from 'vitest';

import en from '@/i18n/locales/en.json';
import nl from '@/i18n/locales/nl.json';

import { roleIcons } from '../profileRoles';

// ProfileHeader builds the badge key from the role, so no static key check sees a missing one.
function labelKey(role: string): string {
  return role === 'subscriber'
    ? 'subscriberBadge'
    : role;
}

const roles = Object.keys(roleIcons);

describe('The role badges on a profile', () => {
  it.each([
    ['English', en.profile.roles],
    ['Dutch', nl.profile.roles],
  ])('have a %s label for every role', (_language, labels: Record<string, string>) => {
    const missing = roles.filter((role) => !labels[labelKey(role)]);

    expect(missing).toEqual([]);
  });
});
