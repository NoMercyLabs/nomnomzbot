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
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ref } from 'vue';

import TargetPicker from '@/components/layout/TargetPicker.vue';
import { i18n } from '@/i18n';
import type { PickedTarget } from '@/lib/bindingTarget';

const created = vi.hoisted(() => vi.fn<(name: string) => Promise<unknown>>());

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('chan-1') }),
}));

vi.mock('@/api/commands', () => ({
  useCommands: () => ({
    data: ref([
      { id: 'c-1', name: 'hello' },
      { id: 'c-2', name: 'so' },
    ]),
  }),
}));

vi.mock('@/api/pipelines', () => ({
  PICKER_KINDS: [],
  usePipelineCatalog: () => ({
    data: ref({
      actions: [
        {
          type: 'play_sound',
          category: { key: 'media' },
          description: { key: 'x' },
          fields: [
            {
              name: 'volume',
              kind: 'number',
              required: false,
              repeatable: false,
              description: null,
              templated: false,
              declaresVariable: false,
            },
          ],
        },
        {
          type: 'song_skip',
          category: { key: 'media' },
          description: { key: 'y' },
          fields: [],
        },
      ],
    }),
  }),
  usePipelines: () => ({
    data: ref([{ id: 'p-1', name: 'Welcome flow' }]),
  }),
  useCreatePipeline: () => ({
    isPending: ref(false),
    mutateAsync: created,
  }),
}));

beforeEach(() => {
  created.mockReset();
  Element.prototype.hasPointerCapture = (): boolean => false;
  Element.prototype.releasePointerCapture = (): void => undefined;
  Element.prototype.scrollIntoView = (): void => undefined;
});

afterEach(() => {
  document.body.innerHTML = '';
});

function mountPicker(props: Record<string, unknown> = {}): VueWrapper {
  return mount(TargetPicker, {
    attachTo: document.body,
    props: { target: null, ...props },
    global: { plugins: [i18n] },
  });
}

async function openList(wrapper: VueWrapper): Promise<void> {
  await wrapper.find('[data-slot="select-trigger"]').trigger('keydown', { key: 'Enter' });
  await flushPromises();
}

async function choose(text: string): Promise<void> {
  const item = Array.from(
    document.querySelectorAll<HTMLElement>('[data-slot="select-item"]'),
  ).find((candidate) => candidate.textContent?.includes(text));

  item!.dispatchEvent(new PointerEvent('pointerup', {
    bubbles: true,
    button: 0,
    pointerType: 'mouse',
  }));

  await flushPromises();
}

function emitted(wrapper: VueWrapper): PickedTarget | null {
  const events: unknown[][] = wrapper.emitted('update:target') ?? [];

  return events.at(-1)![0] as PickedTarget | null;
}

describe('the target picker', () => {
  it('offers commands and pipelines in their own groups', async () => {
    const wrapper = mountPicker();

    await openList(wrapper);

    const groups = Array.from(
      document.querySelectorAll<HTMLElement>('[data-slot="select-label"]'),
    ).map((label) => label.textContent!.trim());

    expect(groups).toEqual(['Commands', 'Pipelines']);
  });

  it('emits a command target when a command is chosen', async () => {
    const wrapper = mountPicker();

    await openList(wrapper);
    await choose('!so');

    expect(emitted(wrapper)).toEqual({ kind: 'command', id: 'c-2' });
  });

  it('emits a pipeline target when a pipeline is chosen', async () => {
    const wrapper = mountPicker();

    await openList(wrapper);
    await choose('Welcome flow');

    expect(emitted(wrapper)).toEqual({ kind: 'pipeline', id: 'p-1' });
  });

  it('leaves out a kind the surface does not allow', async () => {
    const wrapper: VueWrapper = mountPicker({ kinds: ['pipeline'] });

    await openList(wrapper);

    const labels = Array.from(
      document.querySelectorAll<HTMLElement>('[data-slot="select-label"]'),
    ).map((label) => label.textContent!.trim());

    expect(labels).toEqual(['Pipelines']);
  });

  it('creates a pipeline in place and emits it as the target', async () => {
    created.mockResolvedValue({ id: 'p-new' });
    const wrapper: VueWrapper = mountPicker({ createName: '!hello' });

    await openList(wrapper);
    await choose('Create a new pipeline');

    expect(created).toHaveBeenCalledWith({ name: '!hello' });
    expect(emitted(wrapper)).toEqual({ kind: 'pipeline', id: 'p-new' });
  });

  it('emits null when "nothing" is chosen on a clearable picker', async () => {
    const wrapper: VueWrapper = mountPicker({
      clearable: true,
      target: { kind: 'command', id: 'c-1' },
    });

    await openList(wrapper);
    await choose('Nothing');

    expect(emitted(wrapper)).toBeNull();
  });

  it('shows the refusal the server sent for the target', () => {
    const wrapper: VueWrapper = mountPicker({
      target: { kind: 'command', id: 'c-1' },
      error: 'That command does not exist in this channel.',
    });

    const alert: Element = wrapper.element.querySelector('[role="alert"]')!;

    expect(alert.textContent).toContain('That command does not exist in this channel.');
  });

  it('warns when the bound command is not in this channel', () => {
    const wrapper: VueWrapper = mountPicker({ target: { kind: 'command', id: 'other-tenant' } });

    const alert: Element = wrapper.element.querySelector('[role="alert"]')!;

    expect(alert.textContent).toContain('not in this channel');
  });

  it('offers actions only on a surface that allows them', async () => {
    const wrapper = mountPicker();

    await openList(wrapper);

    expect(document.body.textContent).not.toContain('Play sound');
  });

  it('emits a new action target with empty settings when an action is chosen', async () => {
    const wrapper: VueWrapper = mountPicker({ kinds: ['command', 'action'] });

    await openList(wrapper);
    await choose('Play sound');

    expect(emitted(wrapper)).toEqual({
      kind: 'action',
      id: '',
      actionType: 'play_sound',
      params: {},
    });
  });

  it('shows the action fields from the catalog and emits the settings typed in them', async () => {
    const wrapper: VueWrapper = mountPicker({
      kinds: ['command', 'action'],
      target: {
        kind: 'action',
        id: '',
        actionType: 'play_sound',
        params: {},
      },
    });

    const input = wrapper.find('[data-action-params] input');

    expect(input.exists()).toBe(true);

    await input.setValue('80');
    await input.trigger('change');
    await input.trigger('keydown', { key: 'Enter' });
    await input.trigger('blur');

    expect(emitted(wrapper)).toEqual({
      kind: 'action',
      id: '',
      actionType: 'play_sound',
      params: { volume: 80 },
    });
  });

  it('shows no settings form for an action without fields', () => {
    const wrapper: VueWrapper = mountPicker({
      kinds: ['action'],
      target: {
        kind: 'action',
        id: '',
        actionType: 'song_skip',
        params: {},
      },
    });

    expect(wrapper.find('[data-action-params]').exists()).toBe(false);
  });

  it('shows a bound action target by its name without a missing warning', () => {
    const wrapper: VueWrapper = mountPicker({
      target: {
        kind: 'action',
        id: '',
        actionType: 'play_sound',
      },
    });

    expect(wrapper.text()).toContain('Play sound');
    expect(wrapper.element.querySelector('[role="alert"]')).toBeNull();
  });

  it('links the open button to the pipeline editor for a pipeline target', () => {
    const wrapper: VueWrapper = mountPicker({ target: { kind: 'pipeline', id: 'p-1' } });

    expect(wrapper.find('a[href="/app/pipelines/p-1"]').exists()).toBe(true);
  });
});
