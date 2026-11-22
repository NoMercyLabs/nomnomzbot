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

import type { PipelineOptionList, PipelineStep } from '@/api/pipelines';
import { i18n } from '@/i18n';

import StepField from '../components/StepField.vue';
import { actionRows, settingsAfter } from '../components/stepFields';
import en from '../i18n/en.json';

// The platform's category search, stubbed: the picker asks for the `stream_category` kind.
const categories: PipelineOptionList = {
  sourceAvailable: true,
  items: [
    {
      value: 'Just Chatting',
      label: 'Just Chatting',
      state: 0,
    },
    {
      value: 'Software and Game Development',
      label: 'Software and Game Development',
      state: 0,
    },
  ],
  totalCount: 2,
};

const asked = vi.hoisted(() => ({ kinds: [] as string[] }));

vi.mock('@/api/pipelines', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/api/pipelines')>()),
  usePipelineOptions: (_channel: unknown, kind: () => string) => ({
    data: computed(() => {
      asked.kinds.push(kind());

      return categories;
    }),
    isPending: ref(false),
    isFetching: ref(false),
  }),
  usePipelineOption: () => ({ data: computed(() =>
    null) }),
}));

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({
    known: ref(true),
    can: (): boolean =>
      false,
  }),
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

let mounted: VueWrapper | null = null;

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

const words = {
  t: (key: string) =>
    key,
  te: () =>
    false,
  text: (_text: unknown, fallback: string) =>
    fallback,
};

const field = (name: string, kind: string, extra: Record<string, boolean> = {}) => ({
  name,
  kind,
  required: false,
  repeatable: false,
  description: null,
  templated: false,
  declaresVariable: false,
  ...extra,
}) as const satisfies Record<string, unknown>;

// The catalog entry as the server serves it for set_stream_info.
const descriptor = {
  type: 'set_stream_info',
  category: { key: 'c' },
  description: { key: 'd' },
  fields: [
    field('title', 'text', { templated: true }),
    field('category', 'stream_category'),
    field('tags', 'text', { repeatable: true }),
  ],
} as never;

function stepOf(action: Record<string, unknown>): PipelineStep {
  return {
    id: 's1',
    parent_step_id: null,
    branch: null,
    block_kind: null,
    block_config: null,
    order: 0,
    action: { type: 'set_stream_info', ...action },
    condition: null,
    continue_on_error: false,
    stop_on_match: false,
  } as PipelineStep;
}

describe('the Set stream info step form', () => {
  it('shows a title, a category picker and a tag list, and the tag limit under the list', () => {
    i18n.global.mergeLocaleMessage('en', en);
    const rows = actionRows(stepOf({}), descriptor, words);

    expect(rows.map((row) =>
      `${row.name}:${row.control}`)).toEqual(['title:lines', 'category:pick', 'tags:list']);

    const tags = rows.find((row) =>
      row.name === 'tags')!;

    mounted = mount(StepField, {
      attachTo: document.body,
      props: { row: { ...tags, help: 'Up to 10 tags, 25 characters each.' } },
      global: { plugins: [i18n] },
    });

    expect(document.body.querySelector('[data-list-help]')?.textContent?.trim()).toBe('Up to 10 tags, 25 characters each.');
  });

  it('saves the category picked from the search and the tag added, as the step parameters', async () => {
    i18n.global.mergeLocaleMessage('en', en);
    let action: Record<string, unknown> = { type: 'set_stream_info', title: 'Chill {args}' };

    // One edit as the inspector applies it: the form hands over a value, the step keeps it beside the others.
    const apply = (name: string, value: unknown): void => {
      const rows = actionRows(stepOf(action), descriptor, words);
      const row = rows.find((candidate) =>
        candidate.name === name)!;

      action = { ...action, ...settingsAfter(rows, row, value as never) };
    };

    const category = actionRows(stepOf(action), descriptor, words).find((row) =>
      row.name === 'category')!;

    mounted = mount(StepField, {
      attachTo: document.body,
      props: { row: category },
      global: { plugins: [i18n] },
    });

    document.body.querySelector<HTMLButtonElement>('button[role="combobox"]')!.click();
    await flushPromises();

    expect(asked.kinds.at(-1)).toBe('stream_category');

    document.body.querySelector<HTMLElement>('[data-picker-option="Just Chatting"]')!.click();
    await flushPromises();

    expect(mounted.emitted('change')).toEqual([['Just Chatting']]);
    apply('category', 'Just Chatting');
    mounted.unmount();
    document.body.innerHTML = '';

    const tags = actionRows(stepOf(action), descriptor, words).find((row) =>
      row.name === 'tags')!;

    mounted = mount(StepField, {
      attachTo: document.body,
      props: { row: tags },
      global: { plugins: [i18n] },
    });

    document.body.querySelector<HTMLButtonElement>('[data-list-add]')!.click();
    await flushPromises();

    const input = document.body.querySelector<HTMLInputElement>('[data-list-item] input')!;

    input.value = 'english';
    input.dispatchEvent(new Event('change'));
    await flushPromises();

    expect(mounted.emitted('change')).toEqual([[['english']]]);
    apply('tags', ['english']);

    expect(action).toEqual({
      type: 'set_stream_info',
      title: 'Chill {args}',
      category: 'Just Chatting',
      tags: ['english'],
    });
  });
});
