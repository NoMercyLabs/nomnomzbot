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

// The shared target picker edits the steps of the pipeline it has picked, in place: the real picker and the real
// step parts are mounted, and only the HTTP client is replaced by a fake bot.
import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ref } from 'vue';

import type { PipelineStep } from '@/api/pipelines';
import TargetPicker from '@/components/layout/TargetPicker.vue';
import { i18n } from '@/i18n';

type Options = {
  params?: { path?: Record<string, unknown> };
  body?: { graph?: { steps: PipelineStep[] } };
};

type Held = { keys: string[] };

const wire = vi.hoisted(() => ({
  held: { keys: [] } as Held,
  puts: [] as Options[],
  pipeline: {} as Record<string, unknown>,
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('chan-1') }),
}));

const catalog = {
  actions: [
    {
      type: 'send_message',
      category: { key: 'pipeline.category.chat' },
      description: { key: 'pipeline.send_message.description' },
      fields: [
        {
          name: 'message',
          kind: 'text',
          required: true,
          repeatable: false,
          description: null,
          templated: false,
          declaresVariable: false,
        },
      ],
    },
    {
      type: 'play_sound',
      category: { key: 'pipeline.category.media' },
      description: { key: 'pipeline.play_sound.description' },
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
  ],
  conditions: [],
};

function reply(data: unknown): unknown {
  return { data: { data, hasMore: false }, response: { ok: true, status: 200 } };
}

function read(path: string): unknown {
  if (path.endsWith('/roles/effective/me'))
    return reply({ heldActionKeys: wire.held.keys });

  if (path.endsWith('/pipelines/actions'))
    return reply(catalog);

  if (path.endsWith('/pipelines/{id}'))
    return reply(wire.pipeline);

  if (path.endsWith('/pipelines'))
    return reply([{ id: 'p1', name: 'Welcome flow' }]);

  return reply([]);
}

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: vi.fn<(path: string) => Promise<unknown>>(async (path) =>
      read(path)),
    PUT: vi.fn<(path: string, options: Options) => Promise<unknown>>(async (_path, options) => {
      wire.puts.push(options);
      wire.pipeline = { ...wire.pipeline, graph: options.body?.graph };

      return reply(wire.pipeline);
    }),
  },
}));

function stepOf(id: string, message: string, order: number): PipelineStep {
  return {
    id,
    parent_step_id: null,
    branch: null,
    block_kind: null,
    block_config: null,
    order,
    action: { type: 'send_message', message },
    condition: null,
    continue_on_error: false,
    stop_on_match: false,
  };
}

let mounted: VueWrapper | null = null;

beforeEach(() => {
  wire.held.keys = ['pipelines:read', 'pipelines:write'];
  wire.puts.length = 0;

  wire.pipeline = {
    id: 'p1',
    name: 'Welcome flow',
    description: null,
    isEnabled: true,
    triggerCount: 0,
    inputs: [],
    graph: { steps: [stepOf('a', 'first line', 0), stepOf('b', 'second line', 1)] },
  };

  Element.prototype.hasPointerCapture = (): boolean => false;
  Element.prototype.releasePointerCapture = (): void => undefined;
  Element.prototype.scrollIntoView = (): void => undefined;
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

const queryClient = () => new QueryClient({ defaultOptions: { queries: { retry: false } } });

async function mountPicker(): Promise<VueWrapper> {
  mounted = mount(TargetPicker, {
    attachTo: document.body,
    props: { target: { kind: 'pipeline', id: 'p1' } },
    global: {
      plugins: [
        i18n,
        createPinia(),
        [VueQueryPlugin, { queryClient: queryClient() }],
      ],
    },
  });

  await vi.waitFor(() => {
    expect(document.querySelectorAll('[data-step]').length).toBe(2);
  }, { timeout: 5000 });

  return mounted;
}

function shown(): string[] {
  return Array.from(document.querySelectorAll('[data-step-id]'))
    .map((card) => card.getAttribute('data-step-id') ?? '');
}

function control(selector: string): HTMLElement | null {
  return document.querySelector<HTMLElement>(selector);
}

async function press(selector: string): Promise<void> {
  control(selector)!.click();

  await flushPromises();
}

async function pick(id: string): Promise<void> {
  await press(`[data-step-id="${id}"] button[aria-pressed]`);
}

async function addPlaySound(): Promise<void> {
  await press('[data-steps-add]');

  await vi.waitFor(() => {
    expect(control('[data-palette-action="play_sound"]')).not.toBeNull();
  }, { timeout: 5000 });

  await press('[data-palette-action="play_sound"]');
}

async function nudgeDown(id: string): Promise<void> {
  const handle = control(`[data-step-id="${id}"] [data-step-handle]`)!;

  handle.dispatchEvent(new KeyboardEvent('keydown', {
    key: 'ArrowDown',
    altKey: true,
    bubbles: true,
    cancelable: true,
  }));

  await flushPromises();
}

describe('the target picker with a picked pipeline', () => {
  it('shows the steps of the pipeline in order', async () => {
    await mountPicker();

    expect(shown()).toEqual(['a', 'b']);
    expect(document.body.textContent).toContain('Welcome flow');
  });

  it('adds a catalog action to the list', async () => {
    await mountPicker();
    await addPlaySound();

    const ids = shown();

    expect(ids.length).toBe(3);
    expect(ids.slice(0, 2)).toEqual(['a', 'b']);
    expect(document.querySelectorAll('[data-step]')[2]!.textContent).toContain('Play');
    expect(control('[data-steps-save]')).not.toBeNull();
  });

  it('moves a step, so the order changes', async () => {
    await mountPicker();
    await nudgeDown('a');

    expect(shown()).toEqual(['b', 'a']);
  });

  it('edits a field of the selected step in the draft', async () => {
    await mountPicker();
    await pick('b');

    const field = control('[data-step-field="message"] input, [data-step-field="message"] textarea')!;

    (field as HTMLInputElement).value = 'changed line';
    field.dispatchEvent(new Event('change', { bubbles: true }));
    await flushPromises();

    // The edit stays in the draft until Save: no request yet, then one request that holds the new text.
    expect(wire.puts.length).toBe(0);

    await press('[data-steps-save]');

    await vi.waitFor(() => {
      expect(wire.puts.length).toBe(1);
    }, { timeout: 5000 });

    const steps = wire.puts[0]!.body!.graph!.steps;
    const edited = steps.find((step) => step.id === 'b');
    const untouched = steps.find((step) => step.id === 'a');

    expect(edited!.action.message).toBe('changed line');
    expect(untouched!.action.message).toBe('first line');
  });

  it('removes the selected step from the list', async () => {
    await mountPicker();
    await pick('a');
    await press('[data-step-remove]');

    expect(shown()).toEqual(['b']);
  });

  it('discards the draft without a request', async () => {
    await mountPicker();
    await nudgeDown('a');
    await press('[data-steps-discard]');

    expect(shown()).toEqual(['a', 'b']);
    expect(control('[data-steps-save]')).toBeNull();
    expect(wire.puts.length).toBe(0);
  });

  it('saves once, with the steps in their new order and the added action', async () => {
    await mountPicker();
    await nudgeDown('a');
    await addPlaySound();
    await press('[data-steps-save]');

    await vi.waitFor(() => {
      expect(wire.puts.length).toBe(1);
    }, { timeout: 5000 });

    const saved = wire.puts[0]!;
    // The bot reads the order of a step from its order number, so the saved list is read the same way.
    const steps = saved.body!.graph!.steps.slice().sort((one, other) => one.order - other.order);

    expect(saved.params!.path!.id).toBe('p1');
    expect(steps.map((step) => step.order)).toEqual([0, 1, 2]);
    expect(steps.map((step) => step.id).slice(0, 2)).toEqual(['b', 'a']);
    expect(steps.map((step) => step.action.type)).toEqual(['send_message', 'send_message', 'play_sound']);

    await flushPromises();

    expect(control('[data-steps-save]')).toBeNull();
  });

  it('locks the steps without the write permission', async () => {
    wire.held.keys = ['pipelines:read'];

    await mountPicker();
    await pick('a');

    expect(shown()).toEqual(['a', 'b']);
    expect(control('[data-steps-add]')).toBeNull();
    expect(control('[data-step-handle]')).toBeNull();
    expect(control('[data-step-remove]')).toBeNull();
    expect(control('[data-steps-save]')).toBeNull();
    expect(control('[data-steps-discard]')).toBeNull();
  });
});
