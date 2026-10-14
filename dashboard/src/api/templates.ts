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
import { client } from './client';

export type TemplateHelper = components['schemas']['TemplateHelperDto'];
export type TemplateContext = 'command' | 'eventResponse' | 'timer';

async function fetchHelpers(
  context: TemplateContext,
  eventType: string | undefined,
): Promise<TemplateHelper[]> {
  const { data } = await client.GET('/api/v{version}/templates/helpers', {
    params: { path: { version: '1' }, query: { context, eventType } },
  });

  return [...(data?.data ?? [])];
}

// The helpers a template may use in one place. The set only changes with a new bot version, so it is fetched once.
export function useTemplateHelpers(
  context: MaybeRefOrGetter<TemplateContext>,
  eventType?: MaybeRefOrGetter<string | undefined>,
): UseQueryReturnType<TemplateHelper[], Error> {
  const place = computed(() => toValue(context));
  const event = computed(() => toValue(eventType) ?? null);

  return useQuery({
    queryKey: ['templates', 'helpers', place, event],
    queryFn: () => fetchHelpers(place.value, event.value ?? undefined),
    staleTime: Infinity,
  });
}
