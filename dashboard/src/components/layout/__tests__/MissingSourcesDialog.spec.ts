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
import { createPinia, setActivePinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { defineComponent, h } from 'vue';

import { useSavePipeline } from '@/api/pipelines';
import MissingSourcesDialog from '@/components/layout/MissingSourcesDialog.vue';
import { useMissingSources } from '@/composables/useMissingSources';
import { i18n } from '@/i18n';

const http = vi.hoisted(() => ({
  GET: vi.fn<(path: string, init: unknown) => Promise<unknown>>(),
  POST: vi.fn<(path: string, init: unknown) => Promise<unknown>>(),
  PUT: vi.fn<(path: string, init: unknown) => Promise<unknown>>(),
  DELETE: vi.fn<(path: string, init: unknown) => Promise<unknown>>(),
  use: (): undefined => undefined,
}));

const push = vi.hoisted(() => vi.fn<(to: unknown) => Promise<void>>());

vi.mock('@/api/client', () => ({ client: http }));
vi.mock('vue-router', () => ({ useRouter: () => ({ push }) }));

const ok = { ok: true, status: 200 } as const;

function saved(missingSources: unknown[]) {
  return {
    data: {
      data: {
        id: 'p1',
        channelId: 'c1',
        name: 'Speaks',
        isEnabled: true,
        triggerKind: 'command',
        graph: { steps: [] },
        triggerCount: 0,
        inputs: [],
        missingSources,
      },
    },
    response: ok,
  };
}

let mounted: VueWrapper | null = null;
let save: ReturnType<typeof useSavePipeline>;

beforeEach(() => {
  const pinia = createPinia();

  setActivePinia(pinia);
  useMissingSources().dismiss();
  push.mockReset();
  http.PUT.mockReset();

  const queries = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const Host = defineComponent({
    setup() {
      save = useSavePipeline('c1');

      return () => h(MissingSourcesDialog);
    },
  });

  mounted = mount(Host, {
    attachTo: document.body,
    global: { plugins: [i18n, pinia, [VueQueryPlugin, { queryClient: queries }]] },
  });
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

describe('MissingSourcesDialog after a pipeline save', () => {
  it('opens and names Text to speech in plain words when the save reports a missing source', async () => {
    http.PUT.mockResolvedValue(
      saved([{ surfaceKey: 'tts_caption', actionTypes: ['tts_synthesize'] }]),
    );

    await save.mutateAsync({ id: 'p1', graph: { steps: [] } });
    await flushPromises();

    const body = document.body.textContent;

    expect(body).toContain('One thing is missing');

    expect(body).toContain(
      'Text to speech: this pipeline uses it, but it is not open in OBS. Those steps are skipped until you add it.',
    );

    expect(body).not.toContain('tts_synthesize');
    expect(document.querySelectorAll('[data-missing-set-up]')).toHaveLength(1);
  });

  it('opens nothing when the save reports no missing source', async () => {
    http.PUT.mockResolvedValue(saved([]));

    await save.mutateAsync({ id: 'p1', graph: { steps: [] } });
    await flushPromises();

    expect(document.querySelector('[data-missing-sources]')).toBeNull();
  });

  it('Later closes it and Set it up leads to the OBS overlays', async () => {
    http.PUT.mockResolvedValue(saved([{ surfaceKey: 'audio', actionTypes: ['play_sound'] }]));

    await save.mutateAsync({ id: 'p1', graph: { steps: [] } });
    await flushPromises();
    document.querySelector<HTMLButtonElement>('[data-missing-later]')?.click();
    await flushPromises();

    expect(document.querySelector('[data-missing-sources]')).toBeNull();

    await save.mutateAsync({ id: 'p1', graph: { steps: [] } });
    await flushPromises();
    document.querySelector<HTMLButtonElement>('[data-missing-set-up]')?.click();
    await flushPromises();

    expect(push).toHaveBeenCalledWith({
      name: 'dashboard-obs',
      query: { view: 'overlays' },
      hash: '#required-overlays',
    });

    expect(document.querySelector('[data-missing-sources]')).toBeNull();
  });
});
