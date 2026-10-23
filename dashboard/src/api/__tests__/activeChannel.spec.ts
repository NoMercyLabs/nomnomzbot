// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { afterEach, describe, expect, it, vi } from 'vitest';

import { activeChannelId, channelHeader, followChannel } from '../activeChannel';
import { client } from '../client';
import { queryClient } from '../queryClient';

function answer(): ReturnType<typeof vi.fn<typeof fetch>> {
  return vi.fn<typeof fetch>(async () => new Response('{"data":[]}', { status: 200 }));
}

function sent(fetcher: ReturnType<typeof vi.fn<typeof fetch>>, call: number): Request {
  return fetcher.mock.calls[call]?.[0] as Request;
}

afterEach(() => {
  activeChannelId.value = null;
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
});
