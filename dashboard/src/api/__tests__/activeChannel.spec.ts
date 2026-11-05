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

import { VueQueryPlugin } from '@tanstack/vue-query';
import { mount } from '@vue/test-utils';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { defineComponent, h, nextTick, watch } from 'vue';

import { activeChannelId, channelHeader, followChannel } from '../activeChannel';
import { moderatedChannelsQuery, myChannelsQuery, type ChannelSummary } from '../channels';
import { client } from '../client';
import { queryClient } from '../queryClient';
import { chosenChannelId, useCurrentChannel } from '@/composables/useCurrentChannel';

function answer(): ReturnType<typeof vi.fn<typeof fetch>> {
  return vi.fn<typeof fetch>(async () => new Response('{"data":[]}', { status: 200 }));
}

function sent(fetcher: ReturnType<typeof vi.fn<typeof fetch>>, call: number): Request {
  return fetcher.mock.calls[call]?.[0] as Request;
}

afterEach(() => {
  activeChannelId.value = null;
  chosenChannelId.value = '';
  vi.unstubAllGlobals();
});

// Stoney 2026-10-23: kitte switched to his profile and saw HER scripts. The scripts list has no channel in its
// path, so the bot answers for the caller's own channel unless the call names the channel being managed.
describe('the channel being managed', () => {
  it('rides on every call, including a list whose path names no channel', async () => {
    const fetcher = answer();

    activeChannelId.value = 'channel-stoney';

    await client.GET('/api/v{version}/code-scripts', {
      params: { path: { version: '1' }, query: { PageSize: 100 } },
      fetch: fetcher,
    });

    expect(sent(fetcher, 0).headers.get(channelHeader)).toBe('channel-stoney');
  });

  it('follows a switch: the next call names the new channel, not the old one', async () => {
    const fetcher = answer();

    await followChannel('channel-kitte');
    await client.GET('/api/v{version}/code-scripts', { params: { path: { version: '1' } }, fetch: fetcher });
    await followChannel('channel-stoney');
    await client.GET('/api/v{version}/code-scripts', { params: { path: { version: '1' } }, fetch: fetcher });

    expect(sent(fetcher, 0).headers.get(channelHeader)).toBe('channel-kitte');
    expect(sent(fetcher, 1).headers.get(channelHeader)).toBe('channel-stoney');
  });

  it('sends nothing while no channel is known, so the bot keeps answering for the caller\'s own', async () => {
    const fetcher = answer();

    await client.GET('/api/v{version}/code-scripts', { params: { path: { version: '1' } }, fetch: fetcher });

    expect(sent(fetcher, 0).headers.has(channelHeader)).toBe(false);
  });

  it('keeps a channel header a call set itself', async () => {
    const fetcher = answer();

    activeChannelId.value = 'channel-stoney';

    await client.GET('/api/v{version}/code-scripts', {
      params: { path: { version: '1' } },
      headers: { [channelHeader]: 'channel-other' },
      fetch: fetcher,
    });

    expect(sent(fetcher, 0).headers.get(channelHeader)).toBe('channel-other');
  });

  it('drops every cached answer when the channel changes, so a page never shows the last channel\'s data', async () => {
    queryClient.setQueryData(['code-scripts'], [{ id: 'kittes-script' }]);

    await followChannel('channel-kitte');
    expect(queryClient.getQueryData(['code-scripts'])).toEqual([{ id: 'kittes-script' }]);

    await followChannel('channel-stoney');

    expect(queryClient.getQueryData(['code-scripts'])).toBeUndefined();
  });

  it('keeps the cache when the same channel is followed again', async () => {
    queryClient.setQueryData(['code-scripts'], [{ id: 'kittes-script' }]);

    await followChannel('channel-kitte');
    await followChannel('channel-kitte');

    expect(queryClient.getQueryData(['code-scripts'])).toEqual([{ id: 'kittes-script' }]);
  });

  // Stoney 2026-11-05: picking CheeseTheKenku from the switcher fired a burst of 13 queries against the
  // literal unresolved "{channelId}" path placeholder, then fell back to his own channel. The cause: a
  // channel switch dropped the person's own + moderated channel lists along with everything else, so
  // useCurrentChannel briefly had no channel to resolve (channelId went null) while the reset forced the
  // mounted queries to refetch anyway, and the lists then came back under the still-old chosen channel.
  it('keeps the person\'s own and moderated channel lists across a switch', async () => {
    const mine: ChannelSummary[] = [{
      id: 'channel-stoney',
      login: 'stoney_eagle',
      displayName: 'Stoney_Eagle',
      isLive: false,
      role: 'broadcaster',
      provider: 'twitch',
    }];

    queryClient.setQueryData(myChannelsQuery.queryKey, mine);
    queryClient.setQueryData(moderatedChannelsQuery.queryKey, []);

    await followChannel('channel-kitte');
    await followChannel('channel-cheese');

    expect(queryClient.getQueryData(myChannelsQuery.queryKey)).toEqual(mine);
    expect(queryClient.getQueryData(moderatedChannelsQuery.queryKey)).toEqual([]);
  });

  // The real end-to-end path: useCurrentChannel (what every page's channelId prop comes from) must never
  // resolve to null mid-switch, because a channel-scoped query still enabled at that instant would then
  // fire with a null channel id and the OpenAPI client leaves "{channelId}" literal in its path instead
  // of skipping the call. It must also settle on the channel that was switched to, not fall back to the
  // one that was active before the switch.
  it('never resolves no channel while switching, and settles on the picked one', async () => {
    chosenChannelId.value = 'channel-stoney';

    const mine: ChannelSummary[] = [
      {
        id: 'channel-stoney',
        login: 'stoney_eagle',
        displayName: 'Stoney_Eagle',
        isLive: false,
        role: 'broadcaster',
        provider: 'twitch',
      },
      {
        id: 'channel-cheese',
        login: 'cheesethekenku',
        displayName: 'CheeseTheKenku',
        isLive: true,
        role: 'moderator',
        provider: 'twitch',
      },
    ];

    queryClient.setQueryData(myChannelsQuery.queryKey, mine);
    queryClient.setQueryData(moderatedChannelsQuery.queryKey, []);

    // A refetch forced by the reset hangs here until released, so the test can inspect the window
    // between the reset and the lists coming back — the window that, on the real box, took a real
    // network round trip and was long enough for other channel-scoped queries to fire against it.
    let releaseLists: () => void = () => undefined;
    const listsReleased = new Promise<void>((resolve) => {
      releaseLists = resolve;
    });

    vi.stubGlobal('fetch', vi.fn<typeof fetch>(async () => {
      await listsReleased;

      return new Response('{"data":[]}', { status: 200 });
    }));

    const seen: (string | null)[] = [];
    const Host = defineComponent({
      setup() {
        const { channelId, choose, channels } = useCurrentChannel();

        watch(channelId, (id) => seen.push(id), { immediate: true });

        return { choose, channels };
      },
      render: () => h('div'),
    });

    const wrapper = mount(Host, { global: { plugins: [[VueQueryPlugin, { queryClient }]] } });

    // Stoney was already managing his own channel: a reset only happens on a real switch, never on
    // the very first channel a session follows.
    activeChannelId.value = 'channel-stoney';

    // The router guard follows the picked channel's real id before the route (and so chosenChannelId)
    // ever updates — this is the exact ordering the live switch produced.
    const switching = followChannel('channel-cheese');

    await nextTick();
    expect(seen).not.toContain(null);

    releaseLists();
    await switching;
    await nextTick();

    // The route then commits and useRouteChannel chooses the matched channel.
    const cheese = wrapper.vm.channels.find((row) => row.login === 'cheesethekenku');

    await wrapper.vm.choose(cheese!);
    await nextTick();

    expect(seen).not.toContain(null);
    expect(seen.at(-1)).toBe('channel-cheese');

    wrapper.unmount();
  });
});
