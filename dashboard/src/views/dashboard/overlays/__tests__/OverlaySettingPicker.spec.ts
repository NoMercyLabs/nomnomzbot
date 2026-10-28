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

  // The field is picked from a list of rewards, so "ID" names nothing a person sees.
  it('names a picked reward as a reward, never as its ID', () => {
    const field = mountField({
      label: text('Channel point reward ID'),
      help: text('The ID of the channel point reward that starts the crash. Empty means nothing starts it.'),
    });

    expect(field.text()).toContain('Channel point reward');
    expect(field.text()).toContain('The channel point reward that starts the crash. Empty means nothing starts it.');
    expect(field.text()).not.toMatch(/\bID\b/);
  });

  it('keeps the ID in the words of a typed text field', () => {
    const field = mountField({ key: 'title', label: text('Stream ID') });

    expect(field.text()).toContain('Stream ID');
  });

  it('drops a help sentence that sends the person to the code editor when the page edits the value', () => {
    const field = mountField(
      {
        key: 'osConfig',
        label: text('Windows versions and timings'),
        type: 'json',
        help: text(
          'Which Windows versions can appear. This is a group of values; it is shown here but is edited in the code editor.',
        ),
      },
      '{"win31":{"enabled":true}}',
    );

    expect(field.text()).toContain('Which Windows versions can appear.');
    expect(field.text()).not.toContain('code editor');
  });

  it('keeps a flat group of values closed until it is opened, so the settings fit one screen', async () => {
    const field = mountField(
      {
        key: 'assets',
        label: text('Sounds and images'),
        type: 'json',
      },
      '{"win31StartupSoundUrl":"a","win95StartupSoundUrl":"b"}',
    );

    expect(field.find('[data-group-key="win31StartupSoundUrl"]').exists()).toBe(false);

    await field.get('[data-field-toggle]').trigger('click');
    await flushPromises();

    expect(field.find('[data-group-key="win31StartupSoundUrl"]').exists()).toBe(true);
  });

  it('marks a changed setting by its Reset button alone, with no unexplained dot', () => {
    mounted = mount(OverlaySettingField, {
      attachTo: document.body,
      props: {
        field: {
          key: 'title',
          label: text('Title'),
          type: 'text',
          group: text('Trigger'),
          default: '',
        },
        value: 'x',
        changed: true,
      },
      global: { plugins: [i18n, createRouter({ history: createMemoryHistory(), routes: [] })] },
    });

    expect(mounted.find('[data-reset]').exists()).toBe(true);
    expect(mounted.find('.rounded-full').exists()).toBe(false);
  });

  it('puts the Reset of a group beside its name, never alone under the group', () => {
    mounted = mount(OverlaySettingField, {
      attachTo: document.body,
      props: {
        field: {
          key: 'osConfig',
          label: text('Windows versions and timings'),
          type: 'json',
          group: text('Windows versions'),
          default: '{}',
        },
        value: '{"win31":{"enabled":true}}',
        changed: true,
      },
      global: { plugins: [i18n, createRouter({ history: createMemoryHistory(), routes: [] })] },
    });

    expect(mounted.find('[data-setting-head] [data-reset]').exists()).toBe(true);
    expect(mounted.findAll('[data-reset]')).toHaveLength(1);
  });
});
