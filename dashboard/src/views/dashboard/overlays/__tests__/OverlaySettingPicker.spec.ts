// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { computed, ref } from 'vue';
import { createMemoryHistory, createRouter } from 'vue-router';

import type { PipelineOptionList } from '@/api/pipelines';
import { i18n } from '@/i18n';

import OverlaySettingField from '../components/OverlaySettingField.vue';

const lists = vi.hoisted(() => ({ byKind: {} as Record<string, unknown> }));

vi.mock('@/api/pipelines', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/api/pipelines')>()),
  usePipelineOptions: (_channel: unknown, kind: () => string) => ({
    data: computed(() =>
      lists.byKind[kind()]),
    isPending: ref(false),
    isFetching: ref(false),
  }),
  usePipelineOption: () => ({ data: computed(() =>
    null) }),
}));

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({ known: ref(true), can: (): boolean => false }),
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

const text = (value: string): { key: string; text: string } => ({ key: `t.${value}`, text: value });

const rewards: PipelineOptionList = {
  sourceAvailable: true,
  items: [
    {
      value: '9f1c2b6e-0000-4aaa-bbbb-123456789abc',
      label: 'Crash the stream',
      secondaryText: '1000 points · active',
      state: 0,
    },
  ],
  totalCount: 1,
};

let mounted: VueWrapper | null = null;

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

function mountField(field: Record<string, unknown>, value = ''): VueWrapper {
  const router = createRouter({ history: createMemoryHistory(), routes: [] });

  mounted = mount(OverlaySettingField, {
    attachTo: document.body,
    props: {
      field: {
        key: 'rewardId',
        label: text('Channel point reward'),
        type: 'text',
        group: text('Trigger'),
        default: '',
        ...field,
      },
      value,
      changed: false,
    },
    global: { plugins: [i18n, router] },
  });

  return mounted;
}

describe('a text setting that names a picker', () => {
  it('shows a searchable list and saves the picked option value', async () => {
    lists.byKind.twitch_reward = rewards;
    const field = mountField({ picker: 'twitch_reward' });

    expect(field.find('input').exists()).toBe(false);

    document.body.querySelector<HTMLButtonElement>('[role="combobox"]')!.click();
    await flushPromises();

    document.body
      .querySelector<HTMLElement>('[data-picker-option="9f1c2b6e-0000-4aaa-bbbb-123456789abc"]')!
      .click();

    await flushPromises();

    expect(field.emitted('update')).toEqual([['9f1c2b6e-0000-4aaa-bbbb-123456789abc']]);
  });

  it('lists the OBS audio inputs for the obs_audio_input picker', async () => {
    lists.byKind.obs_audio_input = {
      sourceAvailable: true,
      items: [{
        value: 'Mic',
        label: 'Mic',
        secondaryText: 'live',
        state: 0,
      }],
      totalCount: 1,
    } as const satisfies PipelineOptionList;

    mountField({ key: 'muteInputName', picker: 'obs_audio_input' });

    document.body.querySelector<HTMLButtonElement>('[role="combobox"]')!.click();
    await flushPromises();

    expect(document.body.querySelector('[data-picker-option="Mic"]')?.textContent).toContain('Mic');
  });

  it('keeps the typed box for a text setting with no picker, or an unknown one', () => {
    expect(mountField({ key: 'title' }).find('input').exists()).toBe(true);
    mounted?.unmount();
    expect(mountField({ key: 'title', picker: 'not_a_kind' }).find('input').exists()).toBe(true);
  });

  // A custom overlay may declare its reward field as plain text; the reward still comes from the channel's list.
  it('picks a reward from the channel rewards for a rewardId field that names no picker', async () => {
    lists.byKind.twitch_reward = rewards;
    const field = mountField({});

    expect(field.find('input').exists()).toBe(false);

    document.body.querySelector<HTMLButtonElement>('[role="combobox"]')!.click();
    await flushPromises();

    document.body
      .querySelector<HTMLElement>('[data-picker-option="9f1c2b6e-0000-4aaa-bbbb-123456789abc"]')!
      .click();

    await flushPromises();

    expect(field.emitted('update')).toEqual([['9f1c2b6e-0000-4aaa-bbbb-123456789abc']]);
  });

  it('picks an OBS audio input for an input name field that names no picker', () => {
    expect(mountField({ key: 'muteInputName' }).find('input').exists()).toBe(false);
    expect(document.body.querySelector('[role="combobox"]')).not.toBeNull();
  });
});

describe('the words beside a setting', () => {
  it('shows the label and the help, never the key or the type', () => {
    const field = mountField({ help: text('The reward that starts the crash.') });

    expect(field.text()).toContain('Channel point reward');
    expect(field.text()).toContain('The reward that starts the crash.');
    expect(field.text()).not.toContain('rewardId');
    expect(field.text()).not.toContain('text ·');
  });
});
