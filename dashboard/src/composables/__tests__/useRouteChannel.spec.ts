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
import { defineComponent, h, ref } from 'vue';
import { createMemoryHistory, createRouter, type Router } from 'vue-router';

import { useRouteChannel, type RouteChannel } from '@/composables/useRouteChannel';
import { channelVariants } from '@/router/channelRoutes';

import type { ChannelChoice } from '../useCurrentChannel';

function choice(login: string, kind: ChannelChoice['kind']) {
  return {
    login,
    displayName: login,
    profileImageUrl: null,
    isLive: false,
    kind,
    channelId: `${login}-id`,
    twitchId: null,
    chatColor: null,
    suspension: null,
  } as const satisfies ChannelChoice;
}

const channels = ref<ChannelChoice[]>([choice('streamer', 'own'), choice('friend', 'moderated')]);
const current = ref<ChannelChoice | null>(channels.value[0]!);
const isLoaded = ref(true);
const choose = vi.fn<(row: ChannelChoice) => Promise<void>>();

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({
    channels,
    current,
    isLoaded,
    choose,
  }),
}));

const page = defineComponent({ render: () => null });
const pages = [{
  path: 'stream',
  name: 'dashboard-stream',
  component: page,
}];

let state: RouteChannel;
let mounted: VueWrapper | undefined;

afterEach(() => {
  mounted?.unmount();
});

async function open(path: string): Promise<Router> {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/app',
        component: page,
        children: pages,
      },
      {
        path: '/app/u/:channelName',
        component: page,
        children: channelVariants(pages),
      },
    ],
  });

  await router.push(path);

  mounted = mount(
    defineComponent({
      setup() {
        state = useRouteChannel();

        return () => h('div');
      },
    }),
    { global: { plugins: [router] } },
  );

  await flushPromises();

  return router;
}

beforeEach(() => {
  choose.mockReset();
  isLoaded.value = true;
  current.value = channels.value[0]!;
});

describe('opening a channel link', () => {
  it('chooses the channel the link names when the person has access', async () => {
    await open('/app/u/Friend/stream');

    expect(choose).toHaveBeenCalledTimes(1);
    expect(choose.mock.calls[0]![0].login).toBe('friend');
    expect(state.denied.value).toBe(false);
  });

  it('chooses nothing when the named channel is already the current one', async () => {
    current.value = channels.value[1]!;

    await open('/app/u/friend/stream');

    expect(choose).not.toHaveBeenCalled();
  });

  it('says no access for a channel the person does not manage, and keeps the current one', async () => {
    await open('/app/u/stranger/stream');

    expect(choose).not.toHaveBeenCalled();
    expect(state.denied.value).toBe(true);
    expect(state.name.value).toBe('stranger');
  });

  it('waits for the channel list before it decides', async () => {
    isLoaded.value = false;

    await open('/app/u/stranger/stream');

    expect(state.denied.value).toBe(false);

    isLoaded.value = true;
    await flushPromises();

    expect(state.denied.value).toBe(true);
  });

  it('does nothing on the short route', async () => {
    await open('/app/stream');

    expect(choose).not.toHaveBeenCalled();
    expect(state.denied.value).toBe(false);
    expect(state.name.value).toBeNull();
  });
});
