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
import { ref } from 'vue';

import ReplyModeField from '@/components/layout/ReplyModeField.vue';
import { i18n } from '@/i18n';
import type { PickedTarget } from '@/lib/bindingTarget';

type NewPipeline = { name: string };
type SavedPipeline = { id: string; graph: { steps: { action: { type: string } }[] } };

const created = vi.hoisted(() => vi.fn<(body: NewPipeline) => Promise<{ id: string }>>());
const saved = vi.hoisted(() => vi.fn<(change: SavedPipeline) => Promise<unknown>>());

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('chan-1') }),
}));

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({ known: ref(true), can: (): boolean => true }),
}));

vi.mock('@/api/commands', () => ({
  useCommands: () => ({ data: ref([]) }),
}));

function action(type: string, category: string) {
  return {
    type,
    category: { key: category },
    description: { key: type },
    fields: [],
  };
}

vi.mock('@/api/pipelines', () => ({
  PICKER_KINDS: [],
  usePipelineCatalog: () => ({
    data: ref({
      actions: [
        action('play_sound', 'pipeline.category.media'),
        action('send_message', 'pipeline.category.chat'),
        action('song_skip', 'pipeline.category.media'),
      ],
    }),
  }),
  usePipelines: () => ({ data: ref([]) }),
  usePipeline: () => ({ data: ref(undefined) }),
  useSavePipeline: () => ({ isPending: ref(false), mutateAsync: saved }),
  useCreatePipeline: () => ({ isPending: ref(false), mutateAsync: created }),
}));

beforeEach(() => {
  created.mockReset();
  saved.mockReset();
  created.mockResolvedValue({ id: 'p-new' });
  saved.mockResolvedValue({});
  Element.prototype.hasPointerCapture = (): boolean => false;
  Element.prototype.releasePointerCapture = (): void => undefined;
  Element.prototype.scrollIntoView = (): void => undefined;
});

let mounted: VueWrapper | null = null;

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

function mountField(props: Record<string, unknown> = {}): VueWrapper {
  mounted = mount(ReplyModeField, {
    attachTo: document.body,
    props: {
      mode: 'full',
      target: null,
      newPipelineName: '!hello',
      ...props,
    },
    global: { plugins: [i18n, [VueQueryPlugin, { queryClient: new QueryClient() }]] },
  });

  return mounted;
}

function paletteRow(type: string): HTMLElement | null {
  return document.body.querySelector<HTMLElement>(`[data-palette-action="${type}"]`);
}

async function settle(): Promise<void> {
  await flushPromises();
  await flushPromises();
}

// The words of the palette load on first use, so the first mount waits for them.
async function paletteShown(): Promise<void> {
  await vi.waitFor(() => expect(document.body.querySelector('[data-step-palette]')).not.toBeNull());
  await settle();
}

describe('the shared reaction switch', () => {
  it('names its second option Actions', async () => {
    mountField({ mode: 'simple' });
    await settle();

    const labels = Array.from(document.body.querySelectorAll('[role="radio"], [role="tab"], button'))
      .map((node) => node.textContent?.trim());

    expect(labels).toContain('Actions');
    expect(labels).not.toContain('Pipeline');
  });

  it('shows the whole action catalog at once, with no picker to open', async () => {
    mountField();
    await paletteShown();

    expect(paletteRow('play_sound')?.textContent?.trim()).toBe('Play sound');
    expect(paletteRow('send_message')?.textContent?.trim()).toBe('Send message');
    expect(paletteRow('song_skip')?.textContent?.trim()).toBe('Skip song');
    expect(document.body.querySelector('[data-slot="popover-content"]')).toBeNull();
  });

  it('creates a pipeline with the first picked action and binds it', async () => {
    const wrapper = mountField();

    await paletteShown();

    paletteRow('play_sound')!.click();
    await settle();

    expect(created.mock.calls.at(0)?.[0]).toEqual({ name: '!hello' });

    const change = saved.mock.calls.at(0)?.[0];

    expect(change?.id).toBe('p-new');
    expect(change?.graph.steps.map((step) => step.action.type)).toEqual(['play_sound']);

    const events: unknown[][] = wrapper.emitted('update:target') ?? [];
    const bound = events.at(-1)![0] as PickedTarget | null;

    expect(bound).toEqual({ kind: 'pipeline', id: 'p-new' });
  });

  it('keeps the existing pipeline or command choice as a quiet link', async () => {
    mountField();
    await settle();

    expect(document.body.querySelector('[data-reply-existing]')).not.toBeNull();
  });
});
