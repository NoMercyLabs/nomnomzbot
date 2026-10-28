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

import type { components } from './schema';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

import { useCurrentUser } from './auth';
import { client } from './client';

export type TemplateHelper = components['schemas']['TemplateHelper'];
export type TemplateContext = 'command' | 'eventResponse' | 'timer' | 'pipeline' | 'discord' | 'webhook';

async function fetchHelpers(
  context: TemplateContext,
  eventType: string | undefined,
  channelId: string | null,
): Promise<TemplateHelper[]> {
  const { data } = await client.GET('/api/v{version}/templates/helpers', {
    params: { path: { version: '1' }, query: { context, eventType } },
    // The route carries no channel, so the tenant goes in the header the bot reads it from.
    headers: channelId
      ? { 'X-Channel-Id': channelId }
      : undefined,
  });

  return [...(data?.data ?? [])];
}

// The helpers a template may use in one place. The examples show the signed-in user and the current channel, so the answer is kept per user and channel.
export function useTemplateHelpers(
  context: MaybeRefOrGetter<TemplateContext>,
  eventType?: MaybeRefOrGetter<string | undefined>,
): UseQueryReturnType<TemplateHelper[], Error> {
  const place = computed(() => toValue(context));
  const event = computed(() => toValue(eventType) ?? null);
  const { channelId } = useCurrentChannel();
  const user = useCurrentUser();
  const userId = computed(() => user.data.value?.id ?? null);

  return useQuery({
    queryKey: ['templates', 'helpers', place, event, channelId, userId],
    queryFn: () => fetchHelpers(place.value, event.value ?? undefined, channelId.value),
    staleTime: Infinity,
  });
}
