// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
