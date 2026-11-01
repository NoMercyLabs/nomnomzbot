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

import { beforeEach, describe, expect, it, vi } from 'vitest';

import type { ActivityEvent } from '@/api/home';

import { splitByStream } from '../splitByStream';

type Options = { params: { query?: { streams?: number } } };

const get = vi.fn<(path: string, options: Options) => Promise<unknown>>();

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: (path: string, options: Options) => get(path, options),
  },
}));

function event(id: string): ActivityEvent {
  return {
    id,
    type: 'channel.follow',
    timestamp: '2026-10-30T12:00:00Z',
  } as ActivityEvent;
}

describe('the stream window the Home panel and the Events page read', () => {
  beforeEach(() => {
    get.mockReset();
    get.mockResolvedValue({ data: { data: [] } });
  });

  it('asks for the current stream only on the Home feed', async () => {
    const { activityOptions } = await import('@/api/home');

    await activityOptions('c1').queryFn();

    expect(get.mock.calls[0]?.[1].params.query).toEqual({ streams: 1 });
  });

  it('asks for this stream and the one before it on the Events page', async () => {
    const { streamsActivityOptions } = await import('@/api/home');

    await streamsActivityOptions('c1').queryFn();

    expect(get.mock.calls[0]?.[1].params.query).toEqual({ streams: 2 });
  });

  it('files the events of the two-stream window the Home feed does not hold under the earlier stream', () => {
    const split = splitByStream([event('c'), event('b')], [event('c'), event('b'), event('a')]);

    expect(split.newest.map((e) => e.id)).toEqual(['c', 'b']);
    expect(split.earlier.map((e) => e.id)).toEqual(['a']);
  });

  it('has no earlier stream when the window holds only the newest one', () => {
    const split = splitByStream([event('b')], [event('b')]);

    expect(split.earlier).toEqual([]);
  });
});
