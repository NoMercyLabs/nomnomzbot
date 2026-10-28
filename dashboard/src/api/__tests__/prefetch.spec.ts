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

import { QueryClient } from '@tanstack/vue-query';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { analyticsOverviewOptions } from '@/api/analytics';
import { userChatKey } from '@/api/chat';
import { codeScriptsKey } from '@/api/codeScripts';
import { builtinsKey, commandsKey } from '@/api/commands';
import { personEmotesKey } from '@/api/community';
import { discordConnectionsKey } from '@/api/discord';
import { botStatusKey, connectionsKey, homeStatsKey, supporterActivityKey } from '@/api/home';
import { pollHistoryKey } from '@/api/liveOps';
import {
  blockedTracksKey,
  musicConfigKey,
  musicHistoryKey,
  musicQueueKey,
  musicTopRequestersKey,
} from '@/api/music';
import { obsBridgeKey, obsConnectionKey } from '@/api/obs';
import { pipelinesKey } from '@/api/pipelines';
import { prefetchProfile, prefetchRouteData } from '@/api/prefetch';
import { redemptionsKey, rewardsKey } from '@/api/rewards';
import { streamHistoryKey, streamInfoKey } from '@/api/stream';
import { supporterEventsKey } from '@/api/supporters';
import { ttsConfigKey } from '@/api/tts';
import { requiredOverlaysKey, widgetsKey } from '@/api/widgets';
import type { KeyParts } from '@/lib/array';

type Answer = {
  data?: unknown;
  response: { ok: boolean; status: number };
};

const http = vi.hoisted(() => ({
  GET: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
}));

vi.mock('@/api/client', () => ({ client: http }));

const ok = { ok: true, status: 200 } as const satisfies Answer['response'];

// Every read answers with a row named after its path, so a cached entry can be traced to its request.
const row = (path: string) => [{ id: path }];

const member = {
  id: '42',
  username: 'viewer',
  internalUserId: 'g-42',
};

function answer(path: string): Promise<Answer> {
  const body = path === '/api/v{version}/channels/{channelId}/community/{userId}'
    ? member
    : row(path);

  return Promise.resolve({
    data: { data: body },
    response: ok,
  });
}

const pages = [
  ['dashboard-commands', [commandsKey('c1'), builtinsKey('c1'), pipelinesKey('c1')]],
  [
    'dashboard-music',
    [
      musicQueueKey('c1'),
      musicConfigKey('c1'),
      blockedTracksKey('c1'),
      musicHistoryKey('c1'),
      musicTopRequestersKey('c1'),
      connectionsKey('c1'),
    ],
  ],
  ['dashboard-channel-points', [rewardsKey('c1'), redemptionsKey('c1'), pipelinesKey('c1')]],
  ['dashboard-supporters', [supporterEventsKey('c1'), supporterActivityKey('c1'), homeStatsKey('c1')]],
  ['dashboard-settings-discord', [discordConnectionsKey('c1')]],
  ['dashboard-tts', [ttsConfigKey('c1')]],
  ['dashboard-overlays', [widgetsKey('c1'), requiredOverlaysKey('c1')]],
  ['dashboard-obs', [obsConnectionKey('c1'), obsBridgeKey('c1'), widgetsKey('c1'), requiredOverlaysKey('c1')]],
  ['dashboard-polls', [pollHistoryKey('c1')]],
  ['dashboard-pipelines', [pipelinesKey('c1')]],
  ['dashboard-scripts', [codeScriptsKey]],
  ['dashboard-stream', [streamInfoKey('c1'), streamHistoryKey('c1'), botStatusKey('c1'), rewardsKey('c1')]],
] as const satisfies ReadonlyArray<readonly [string, readonly KeyParts[]]>;

let queries: QueryClient;

function cachedStatus(key: readonly unknown[]): string | undefined {
  return queries.getQueryState(key)?.status;
}

describe('prefetchRouteData', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    localStorage.clear();
    http.GET.mockImplementation(answer);
    queries = new QueryClient();
  });

  it.each(pages)('puts every read of %s in the cache', async (route, keys) => {
    expect(keys.map(cachedStatus)).toEqual(keys.map(() => undefined));

    await prefetchRouteData(queries, route, 'c1');

    expect(keys.map(cachedStatus)).toEqual(keys.map(() => 'success'));
  });

  it('puts the answer of the request itself in the cache, not a placeholder', async () => {
    await prefetchRouteData(queries, 'dashboard-polls', 'c1');

    expect(queries.getQueryData(pollHistoryKey('c1'))).toEqual(
      row('/api/v{version}/channels/{channelId}/live-ops/history'),
    );
  });

  it('warms the analytics overview for the range the page remembers', async () => {
    localStorage.setItem('nnz.analytics.range', 'r90');

    await prefetchRouteData(queries, 'dashboard-analytics', 'c1');

    const remembered = analyticsOverviewOptions('c1', 'r90').map((options) => options.queryKey);
    const fallback = analyticsOverviewOptions('c1', 'r30').map((options) => options.queryKey);

    expect(remembered.map(cachedStatus)).toEqual(['success', 'success', 'success']);
    expect(fallback.map(cachedStatus)).toEqual([undefined, undefined, undefined]);
  });

  it('asks nothing for a page that reads nothing on open', async () => {
    await prefetchRouteData(queries, 'dashboard-settings', 'c1');

    expect(http.GET).not.toHaveBeenCalled();
  });
});

describe('prefetchProfile', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    http.GET.mockImplementation(answer);
    queries = new QueryClient();
  });

  it('puts the Emotes tab and the first Chat tab page in the cache', async () => {
    await prefetchProfile(queries, 'c1', '42');

    const chat = queries.getQueryData<{ pages: unknown[] }>(userChatKey('c1', 'g-42'));

    expect(cachedStatus(personEmotesKey('c1', '42'))).toBe('success');
    expect(chat?.pages).toHaveLength(1);
  });
});
