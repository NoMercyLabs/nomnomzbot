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
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import type { PipelineActionDescriptor, PipelineStep } from '@/api/pipelines';
import { i18n } from '@/i18n';
import schemaEn from '@/i18n/schema/en.json';

import en from '../i18n/en.json';
import LiveRunConfirm from '../components/LiveRunConfirm.vue';

function step(id: string, type: string, params: Record<string, string> = {}): PipelineStep {
  return {
    id,
    parent_step_id: null,
    branch: null,
    block_kind: null,
    block_config: null,
    order: 0,
    action: { type, ...params },
    condition: null,
    continue_on_error: false,
    stop_on_match: false,
  };
}

function descriptor(type: string, category: string, userField?: string): PipelineActionDescriptor {
  return {
    type,
    category: { key: `pipeline.category.${category}` },
    description: { key: `pipeline.${type}.description` },
    fields: userField === undefined
      ? []
      : [
          {
            name: userField,
            kind: 'twitch_user',
            required: false,
            repeatable: false,
            description: null,
            templated: true,
            declaresVariable: false,
          },
        ],
  } as unknown as PipelineActionDescriptor;
}

const descriptors = new Map<string, PipelineActionDescriptor>([
  ['send_message', descriptor('send_message', 'chat')],
  ['play_sound', descriptor('play_sound', 'sound')],
  ['ban', descriptor('ban', 'moderation', 'user_id')],
  ['grant_currency', descriptor('grant_currency', 'economy', 'user_id')],
  ['wait', descriptor('wait', 'flow')],
]);

const steps = [
  step('a', 'send_message'),
  step('b', 'play_sound'),
  step('c', 'ban', { user_id: '{args.1}' }),
  step('d', 'grant_currency', { user_id: 'abc123' }),
  step('e', 'wait'),
];

let mounted: VueWrapper | null = null;

beforeEach(() => {
  i18n.global.mergeLocaleMessage('en', en);
  i18n.global.mergeLocaleMessage('en', schemaEn);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

async function show(list: PipelineStep[] = steps): Promise<VueWrapper> {
  mounted = mount(LiveRunConfirm, {
    attachTo: document.body,
    props: {
      open: true,
      steps: list,
      descriptors,
    },
    global: { plugins: [i18n] },
  });

  await flushPromises();

  return mounted;
}

const group = (key: string): HTMLElement | null =>
  document.body.querySelector<HTMLElement>(`[data-live-group="${key}"]`);
const rows = (key: string): string[] =>
  [...group(key)!.querySelectorAll<HTMLElement>('[data-live-step]')].map(
    (row) => row.querySelector('[data-live-step-title]')!.textContent!.trim(),
  );

describe('the live run confirm', () => {
  it('groups the steps by the category of their action, in the fixed order', async () => {
    await show();

    const order = [...document.body.querySelectorAll<HTMLElement>('[data-live-group]')].map(
      (item) => item.dataset.liveGroup,
    );

    expect(order).toEqual(['chat', 'overlay', 'moderation', 'economy', 'other']);
    expect(rows('chat')).toEqual(['Send message']);
    expect(rows('overlay')).toEqual(['Play sound']);
    expect(rows('moderation')).toEqual(['Ban']);
    expect(rows('economy')).toEqual(['Grant currency']);
    expect(rows('other')).toEqual(['Wait']);
  });

  it('leaves out a group with no step', async () => {
    await show([step('a', 'send_message')]);

    expect(group('chat')).not.toBeNull();
    expect(group('moderation')).toBeNull();
    expect(group('economy')).toBeNull();
  });

  it('says in its own words that moderation and economy steps act for real, and no other group does', async () => {
    await show();

    expect(group('moderation')!.querySelector('[data-live-real]')?.textContent?.trim()).toBe(
      'These steps act on real viewers. A ban or timeout really happens.',
    );

    expect(group('economy')!.querySelector('[data-live-real]')?.textContent?.trim()).toBe(
      'These steps move real currency. Balances really change.',
    );

    expect(group('chat')!.querySelector('[data-live-real]')).toBeNull();
    expect(group('overlay')!.querySelector('[data-live-real]')).toBeNull();
    expect(group('other')!.querySelector('[data-live-real]')).toBeNull();
  });

  it('flags a step whose user field holds a template, and no step with a fixed user', async () => {
    await show();

    const flagged = [...document.body.querySelectorAll<HTMLElement>('[data-live-step]')]
      .filter((row) => row.querySelector('[data-live-flag]') !== null)
      .map((row) => row.querySelector('[data-live-step-title]')!.textContent!.trim());

    expect(flagged).toEqual(['Ban']);

    expect(group('moderation')!.querySelector('[data-live-flag]')?.textContent?.trim()).toBe(
      'Hits whoever {args.1} names when the pipeline starts.',
    );
  });

  it('says the run starts as the person pressing the button', async () => {
    await show();

    expect(document.body.querySelector('[data-live-as]')?.textContent?.trim()).toBe(
      'The run starts as you, the person pressing Run now.',
    );
  });

  it('has Run now as the one primary action and Cancel as an outline button', async () => {
    await show();

    const run = document.body.querySelector<HTMLButtonElement>('[data-live-confirm]')!;
    const cancel = document.body.querySelector<HTMLButtonElement>('[data-live-cancel]')!;

    expect(run.textContent?.trim()).toBe('Run now');
    expect(cancel.textContent?.trim()).toBe('Cancel');

    // Run now takes the accent variant (no variant attribute, no destructive), so Cancel is the outline one.
    expect(run.dataset.variant).toBeUndefined();
    expect(cancel.className).toContain('bg-(--kit-surface)');
    expect(run.className).not.toContain('bg-(--kit-surface)');
  });

  it('emits confirm on Run now and closes without confirming on Cancel', async () => {
    const wrapper = await show();

    document.body.querySelector<HTMLButtonElement>('[data-live-cancel]')!.click();
    await flushPromises();

    expect(wrapper.emitted('confirm')).toBeUndefined();
    expect(wrapper.emitted('update:open')?.at(-1)).toEqual([false]);

    document.body.querySelector<HTMLButtonElement>('[data-live-confirm]')!.click();
    await flushPromises();

    expect(wrapper.emitted('confirm')).toHaveLength(1);
  });
});
