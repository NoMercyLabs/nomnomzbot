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

import { useQuery, type UseQueryReturnType } from '@tanstack/vue-query';
import { computed, toValue, type MaybeRefOrGetter } from 'vue';

import { client } from './client';
import { problemError } from './errors';
import type { components } from './schema';
import type { KeyParts } from '@/lib/array';

export type ChannelDiagnostics = components['schemas']['ChannelDiagnostics'];
export type ChannelDiagnosticSummary = components['schemas']['ChannelDiagnosticSummary'];

// The sections the bot can answer. One read takes any of them, so a new tab asks for its section here
// and never adds a second call.
export type DiagnosticSection = 'summary' | 'runs';

const version = '1';

export const channelDiagnosticsKey = (
  id: string | null,
  sections: readonly DiagnosticSection[],
): KeyParts => ['admin', 'channel', id, 'diagnostics', sections.join(',')];

// The requested sections of one channel. Each read writes one audit row on the bot, so a page asks once
// and shares the answer through the cache.
export function useChannelDiagnostics(
  id: MaybeRefOrGetter<string | null>,
  sections: readonly DiagnosticSection[],
): UseQueryReturnType<ChannelDiagnostics | null, Error> {
  return useQuery({
    queryKey: computed(() => channelDiagnosticsKey(toValue(id), sections)),
    queryFn: async (): Promise<ChannelDiagnostics | null> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/admin/channels/{broadcasterId}/diagnostics',
        {
          params: {
            path: { version, broadcasterId: toValue(id)! },
            query: { sections: sections.join(',') },
          },
        },
      );

      if (error !== undefined)
        throw problemError('admin channel diagnostics', response.status, error);

      return data?.data ?? null;
    },
    enabled: computed(() => toValue(id) !== null),
  });
}
