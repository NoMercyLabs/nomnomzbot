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

import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { computed, ref } from 'vue';
import { createMemoryHistory, createRouter } from 'vue-router';

import type { PipelineOptionList } from '@/api/pipelines';
import TemplateField from '@/components/layout/TemplateField.vue';
import { i18n } from '@/i18n';
import { lookup } from '@/lib/array';

import en from '../i18n/en.json';
import StepField from '../components/StepField.vue';
import type { FieldRow } from '../components/stepFields';

// The bot answers the helper list for a place; the shared client keeps the fetch it finds first.
const bot = vi.hoisted(() => {
  const state: { seen: string[] } = { seen: [] };
  const helpers = [
    {
      key: 'user',
      descriptionKey: 'template.helper.user',
      sample: 'DukaSoft',
    },
    {
      key: 'user.name',
      descriptionKey: 'template.helper.user_name',
      sample: 'DukaSoft',
    },
  ];
  const answers = {
    '/api/v1/templates/helpers': helpers,
    '/api/v1/auth/me': {
      id: 'user-1',
      username: 'moss_mod',
      displayName: 'Moss_Mod',
    },
    '/api/v1/channels': [{
      id: 'channel-1',
      login: 'sunny',
      displayName: 'Sunny',
      role: 'broadcaster',
    }],
  } as const satisfies Record<string, unknown>;

  globalThis.fetch = (async (request: Request) => {
    const url = new URL(request.url);

    state.seen.push(url.pathname + url.search);

    const body = JSON.stringify({ data: lookup(answers, url.pathname) ?? [] });

    return new Response(body, { status: 200 });
  }) as typeof fetch;

  return state;
});

const things: PipelineOptionList = {
  sourceAvailable: true,
  items: [
    {
      value: 'u1',
      label: 'Moss_Mod',
      state: 0,
    },
    {
      value: 'u2',
      label: 'Sunny',
      state: 0,
    },
  ],
  totalCount: 2,
};

vi.mock('@/api/pipelines', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/api/pipelines')>()),
  usePipelineOptions: () => ({
    data: computed(() =>
      things),
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
  useCurrentChannel: () => ({ channelId: ref('channel-1') }),
}));

let mounted: VueWrapper | null = null;

beforeEach(() => {
  bot.seen.length = 0;
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

const baseRow: FieldRow = {
  name: 'message',
  label: 'Message',
  kind: 'text',
  control: 'lines',
  value: undefined,
  help: '',
  choices: [],
  required: false,
  templated: true,
  target: 'action',
};

// A setting that names a thing and is also a template, with a picker kind the form has no special case for.
const thingRow: FieldRow = {
  ...baseRow,
  name: 'recipient',
  label: 'Recipient',
  kind: 'fake_thing',
  control: 'pick',
  value: 'u1',
};

async function mountField(props: InstanceType<typeof StepField>['$props']): Promise<VueWrapper> {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{
      path: '/',
      component: { template: '<div />' },
    }],
  });
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  mounted = mount(StepField, {
    attachTo: document.body,
    props,
    global: { plugins: [i18n, router, [VueQueryPlugin, { queryClient }]] },
  });

  await flushPromises();

  return mounted;
}

async function type(area: HTMLTextAreaElement, value: string): Promise<void> {
  area.focus();
  area.value = value;
  area.setSelectionRange(value.length, value.length);
  area.dispatchEvent(new Event('input', { bubbles: true }));
  await flushPromises();
}

async function press(area: HTMLElement, key: string): Promise<void> {
  area.dispatchEvent(new KeyboardEvent('keydown', {
    key,
    bubbles: true,
    cancelable: true,
  }));

  await flushPromises();
}

// Focus leaving the field hands the typed value over.
async function leave(target: Element): Promise<void> {
  target.dispatchEvent(new FocusEvent('focusout', {
    bubbles: true,
    relatedTarget: null,
  }));

  await flushPromises();
}

function options(): string[] {
  return [...document.body.querySelectorAll('[role="option"][data-helper]')]
    .map((option) =>
      option.getAttribute('data-helper') ?? '');
}

const area = (): HTMLTextAreaElement | null =>
  document.body.querySelector('textarea');

const combobox = (): HTMLElement | null =>
  document.body.querySelector('button[role="combobox"]');

const toggle = (): HTMLElement | null =>
  document.body.querySelector('[data-pick-mode]');

describe('the variables of a step in its template field', () => {
  it('suggests the variable names after a brace and saves the chosen one as {name}', async () => {
    const form = await mountField({
      row: baseRow,
      variables: ['greeting', 'tally'],
    });

    await type(area()!, 'Hi {');

    expect(options()).toEqual(['user', 'user.name', 'greeting', 'tally']);

    await type(area()!, 'Hi {gr');
    await press(area()!, 'Enter');

    expect(area()!.value).toBe('Hi {greeting}');

    await leave(form.element);

    expect(form.emitted('change')).toEqual([['Hi {greeting}']]);
  });
});

describe('the trigger of a step in its template field', () => {
  it('hands its event type to the template field, so the bot narrows the helpers to that trigger', async () => {
    const form = await mountField({
      row: baseRow,
      eventType: 'channel.follow',
    });

    expect(form.findComponent(TemplateField).props('eventType')).toBe('channel.follow');

    expect(bot.seen.some((path) =>
      path.includes('eventType=channel.follow'))).toBe(true);
  });
});

describe('a setting that is a picked thing and a template', () => {
  it('shows the label of a saved item in the picker', async () => {
    await mountField({ row: thingRow });

    expect(combobox()?.textContent).toContain('Moss_Mod');
    expect(area()).toBeNull();
    expect(toggle()).not.toBeNull();
  });

  it('shows a saved template as a template, with its text kept', async () => {
    await mountField({
      row: {
        ...thingRow,
        value: 'Hi {user}',
      },
    });

    expect(combobox()).toBeNull();
    expect(area()?.value).toBe('Hi {user}');
  });

  it('lists the real items and saves the id of the one picked', async () => {
    const form = await mountField({ row: thingRow });

    combobox()!.click();
    await flushPromises();

    document.body.querySelector<HTMLElement>('[data-picker-option="u2"]')!.click();
    await flushPromises();

    expect(form.emitted('change')).toEqual([['u2']]);
  });

  it('takes a typed template with the brace suggestions and saves it when focus leaves', async () => {
    const form = await mountField({ row: thingRow });

    toggle()!.click();
    await flushPromises();

    expect(combobox()).toBeNull();

    await type(area()!, '{us');

    expect(options()).toEqual(['user', 'user.name']);

    await press(area()!, 'ArrowDown');
    await press(area()!, 'Enter');
    await leave(form.element);

    expect(area()!.value).toBe('{user.name}');
    expect(form.emitted('change')).toEqual([['{user.name}']]);
  });

  it('goes back to the list from the template field', async () => {
    await mountField({
      row: {
        ...thingRow,
        value: 'Hi {user}',
      },
    });

    toggle()!.click();
    await flushPromises();

    expect(area()).toBeNull();
    expect(combobox()?.textContent).toContain('Hi {user}');
  });

  it('keeps a plain picker without the template switch when the setting is not a template', async () => {
    await mountField({
      row: {
        ...thingRow,
        templated: false,
      },
    });

    expect(combobox()).not.toBeNull();
    expect(toggle()).toBeNull();
  });
});

describe('a repeatable template setting', () => {
  const listRow: FieldRow = {
    ...baseRow,
    name: 'args',
    label: 'Arguments',
    control: 'list',
    value: ['first', 'second'],
  };

  it('gives every item the brace suggestions and saves the list when focus leaves the item', async () => {
    const form = await mountField({
      row: listRow,
      variables: ['tally'],
    });
    const items = document.body.querySelectorAll<HTMLTextAreaElement>('[data-list-item] textarea');

    expect(items.length).toBe(2);

    await type(items[0]!, '{ta');

    expect(options()).toEqual(['tally']);

    await press(items[0]!, 'Enter');
    await leave(items[0]!);

    expect(form.emitted('change')).toEqual([[['{tally}', 'second']]]);
  });

  it('keeps plain inputs for a repeatable setting that is not a template', async () => {
    await mountField({
      row: {
        ...listRow,
        templated: false,
      },
    });

    expect(document.body.querySelectorAll('[data-list-item] input').length).toBe(2);
    expect(document.body.querySelector('[data-list-item] textarea')).toBeNull();
  });
});
