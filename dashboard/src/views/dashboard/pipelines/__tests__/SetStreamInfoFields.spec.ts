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
import schemaEn from '@/i18n/schema/en.json';

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

type FieldExtra = Record<string, boolean | number | string[]>;

const field = (name: string, kind: string, extra: FieldExtra = {}) => ({
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
    field('tags', 'text', { repeatable: true, maxItems: 10 }),
    field('language', 'stream_language'),
    field('labels_on', 'enum', { repeatable: true, options: ['Gambling', 'ProfanityVulgarity'] }),
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
      `${row.name}:${row.control}`)).toEqual([
      'title:lines',
      'category:pick',
      'tags:list',
      'language:pick',
      'labels_on:choices',
    ]);

    const tags = rows.find((row) =>
      row.name === 'tags')!;

    mounted = mount(StepField, {
      attachTo: document.body,
      props: { row: { ...tags, help: 'Up to 10 tags, 25 characters each.' } },
      global: { plugins: [i18n] },
    });

    expect(document.body.querySelector('[data-list-help]')?.textContent?.trim()).toBe('Up to 10 tags, 25 characters each.');
  });

  it('picks content labels from the fixed list, never typed, and saves the chosen ones as a list', async () => {
    i18n.global.mergeLocaleMessage('en', en);
    const row = actionRows(stepOf({ labels_on: ['Gambling'] }), descriptor, words).find((candidate) =>
      candidate.name === 'labels_on')!;

    mounted = mount(StepField, {
      attachTo: document.body,
      props: { row },
      global: { plugins: [i18n] },
    });

    expect(document.body.querySelectorAll('[data-step-field="labels_on"] input')).toHaveLength(0);

    const presses = [...document.body.querySelectorAll<HTMLButtonElement>('[data-step-field="labels_on"] button')];

    expect(presses.map((press) =>
      press.getAttribute('data-state'))).toEqual(['on', 'off']);

    presses[1]!.click();
    await flushPromises();
    // The inspector hands the saved value back, as it does after each change.
    await mounted.setProps({ row: { ...row, value: ['Gambling', 'ProfanityVulgarity'] } });
    presses[0]!.click();
    await flushPromises();
    await mounted.setProps({ row: { ...row, value: ['ProfanityVulgarity'] } });
    presses[1]!.click();
    await flushPromises();

    // The last label switched off leaves the setting out, so the step changes no label.
    expect(mounted.emitted('change')).toEqual([
      [['Gambling', 'ProfanityVulgarity']],
      [['ProfanityVulgarity']],
      [undefined],
    ]);
  });

  it('names each content label in plain words, never the machine name', () => {
    i18n.global.mergeLocaleMessage('en', schemaEn);
    const schemaWords = {
      t: (key: string) =>
        i18n.global.t(key),
      te: (key: string) =>
        i18n.global.te(key),
      text: words.text,
    };
    const row = actionRows(stepOf({}), descriptor, schemaWords).find((candidate) =>
      candidate.name === 'labels_on')!;

    expect(row.choices).toEqual([
      {
        value: 'Gambling',
        label: 'Gambling',
      },
      {
        value: 'ProfanityVulgarity',
        label: 'Strong language',
      },
    ]);
  });

  it('asks the bot for the language list: the language is picked, never typed', async () => {
    i18n.global.mergeLocaleMessage('en', en);
    const row = actionRows(stepOf({}), descriptor, words).find((candidate) =>
      candidate.name === 'language')!;

    mounted = mount(StepField, {
      attachTo: document.body,
      props: { row },
      global: { plugins: [i18n] },
    });

    document.body.querySelector<HTMLButtonElement>('button[role="combobox"]')!.click();
    await flushPromises();

    expect(asked.kinds.at(-1)).toBe('stream_language');
  });

  const mountTags = (tags: string[]): VueWrapper => {
    i18n.global.mergeLocaleMessage('en', en);
    const row = actionRows(stepOf({ tags }), descriptor, words).find((candidate) =>
      candidate.name === 'tags')!;

    mounted = mount(StepField, {
      attachTo: document.body,
      props: { row },
      global: { plugins: [i18n] },
    });

    return mounted;
  };

  const addButton = (): HTMLButtonElement =>
    document.body.querySelector<HTMLButtonElement>('[data-list-add]')!;

  it('takes no eleventh tag: the add button stops at ten rows, filled or empty', async () => {
    mountTags(Array.from({ length: 9 }, (_, index) =>
      `tag${index + 1}`));

    expect(addButton().disabled).toBe(false);

    addButton().click();
    await flushPromises();

    expect(document.body.querySelectorAll('[data-list-item]')).toHaveLength(10);
    expect(addButton().disabled).toBe(true);

    addButton().click();
    await flushPromises();

    expect(document.body.querySelectorAll('[data-list-item]')).toHaveLength(10);

    document.body.querySelector<HTMLButtonElement>('[data-list-remove]')!.click();
    await flushPromises();

    expect(addButton().disabled).toBe(false);
  });

  it('moves a tag up or down and saves the new order; the first cannot go up, the last cannot go down', async () => {
    const wrapper = mountTags(['english', 'chill', 'coding']);
    const ups = (): HTMLButtonElement[] =>
      [...document.body.querySelectorAll<HTMLButtonElement>('[data-list-up]')];
    const downs = (): HTMLButtonElement[] =>
      [...document.body.querySelectorAll<HTMLButtonElement>('[data-list-down]')];

    expect(ups().map((button) =>
      button.disabled)).toEqual([true, false, false]);

    expect(downs().map((button) =>
      button.disabled)).toEqual([false, false, true]);

    downs()[0]!.click();
    await flushPromises();
    ups()[2]!.click();
    await flushPromises();

    expect(wrapper.emitted('change')).toEqual([
      [['chill', 'english', 'coding']],
      [['chill', 'coding', 'english']],
    ]);

    expect([...document.body.querySelectorAll<HTMLInputElement>('[data-list-item] input')].map((input) =>
      input.value)).toEqual(['chill', 'coding', 'english']);
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
