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
import { mount, flushPromises } from '@vue/test-utils';
import { describe, expect, it, vi } from 'vitest';
import { defineComponent, h, ref } from 'vue';

import type { KeyParts } from '@/lib/array';

import { channelQuery } from '../channelQuery';
import { client } from '../client';

const key = (channelId: string | null): KeyParts => ['probe', channelId];

// Stoney 2026-11-17: the live box logged GET /api/v1/channels/%7BchannelId%7D/obs/bridge/status and
// .../%7BchannelId%7D/chat/emotes. A refetch of the last channel's query ran after the page had no channel,
// and the request read the channel id at run time instead of the id its own cache key holds.
describe('a channel query', () => {
  it('asks for the channel in its own key, even when it runs after the page lost its channel', async () => {
    const asked: (string | null)[] = [];
    const fetcher = vi.fn(async (id: string) => {
      asked.push(id);

      return id;
    });
    const channel = ref<string | null>('channel-a');
    const queries = new QueryClient();
    const Host = defineComponent({
      setup() {
        channelQuery(channel, key, fetcher);

        return () => h('div');
      },
    });
    const wrapper = mount(Host, {
      global: { plugins: [[VueQueryPlugin, { queryClient: queries }]] },
    });

    await flushPromises();
    channel.value = null;
    await queries.refetchQueries({ queryKey: key('channel-a') });
    await flushPromises();

    expect(asked).toEqual(['channel-a', 'channel-a']);

    wrapper.unmount();
  });
});

describe('the API client', () => {
  it('never sends a path with a placeholder left unfilled', async () => {
    const fetcher = vi.fn<typeof fetch>(async () => new Response('{}', { status: 200 }));

    await expect(client.GET('/api/v{version}/channels/{channelId}/obs/bridge/status', {
      params: { path: { version: '1', channelId: null as unknown as string } },
      fetch: fetcher,
    })).rejects.toThrow(/channelId/u);

    expect(fetcher).not.toHaveBeenCalled();
  });
});
