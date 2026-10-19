// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ref } from 'vue';

import { i18n } from '@/i18n';

import en from '../../i18n/en.json';
import PollDialog from '../PollDialog.vue';

vi.mock('@/api/client', () => ({
  client: { use: () => undefined, GET: async () => ({ data: { data: [] } }) },
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

const access = vi.hoisted(() => ({ held: [] as string[] }));

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({
    known: ref(true),
    can: (key: string): boolean => access.held.includes(key),
  }),
}));

let mounted: VueWrapper | null = null;

beforeEach(() => {
  access.held = [];
  setActivePinia(createPinia());
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

async function openForm(): Promise<void> {
  mounted = mount(PollDialog, {
    attachTo: document.body,
    props: { poll: null, open: true },
    global: { plugins: [i18n, [VueQueryPlugin, { queryClient: new QueryClient() }]] },
  });

  await flushPromises();
}

const fields = (): HTMLInputElement[] =>
  Array.from(document.querySelectorAll<HTMLInputElement>('[role="dialog"] input'));

describe('PollDialog start form permissions', () => {
  it('locks every field when the person cannot start polls', async () => {
    access.held = ['live-ops:polls:read'];
    await openForm();

    expect(fields().length).toBeGreaterThanOrEqual(4);
    expect(fields().filter((field) => !field.disabled)).toHaveLength(0);
    expect(document.querySelector('[role="dialog"] button[data-variant="default"]')).toBeNull();
  });

  it('leaves the fields editable with the polls write key', async () => {
    access.held = ['live-ops:polls:read', 'live-ops:polls:write'];
    await openForm();

    expect(fields().filter((field) => field.disabled)).toHaveLength(0);
  });
});
