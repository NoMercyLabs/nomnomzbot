// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { mount } from '@vue/test-utils';
import { describe, expect, it, vi } from 'vitest';
import { ref } from 'vue';

import { i18n } from '@/i18n';

import ShieldToggle from '../ShieldToggle.vue';

vi.mock('@/api/shield', () => ({
  useShieldMode: () => ({ data: ref(false) }),
  useSetShieldMode: () => ({ mutate: vi.fn(), isPending: ref(false) }),
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1'), current: ref({ displayName: 'Streamer' }) }),
}));

describe('the Shield Mode toggle', () => {
  it('is a 36px control (h-9), not the 32px small one', () => {
    const classes: string = mount(ShieldToggle, { global: { plugins: [i18n] } })
      .get('[data-slot="shield-toggle"]')
      .classes()
      .join(' ');

    expect(classes).toContain('h-9');
    expect(classes).not.toContain('h-8');
  });
});
