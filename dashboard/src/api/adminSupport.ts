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

import { client } from './client';
import { problemError } from './errors';
import type { components } from './schema';
import type { KeyPart, KeyParts } from '@/lib/array';

export type SupportPerson = components['schemas']['SupportPersonSearchResult'];
export type SupportPeoplePage =
  components['schemas']['PaginatedResponseOfSupportPersonSearchResult'];
export type SupportPersonView = components['schemas']['SupportPersonView'];
export type SupportHistoryPage =
  components['schemas']['PaginatedResponseOfSupportPersonHistoryEntry'];
export type SupportCommandsPage = components['schemas']['PaginatedResponseOfCommandListItem'];
export type SupportPipelinesPage = components['schemas']['PaginatedResponseOfPipelineListItem'];

const version = '1';
const take = 25;

// Every support read is audited with its reason, so nothing is read until both the reason and the target exist.
// The audit is the point of the desk: a pushed update would be a read nobody asked for, so these reads
// refresh only on request.
export const supportKey = (...parts: readonly KeyPart[]): KeyParts => [
  'admin',
  'support',
  ...parts,
];

const filled = (value: string | null): boolean => (value ?? '').trim() !== '';

export function useSupportPeople(
  search: MaybeRefOrGetter<string>,
  justification: MaybeRefOrGetter<string>,
): UseQueryReturnType<SupportPeoplePage, Error> {
  return useQuery({
    queryKey: computed(() => supportKey('people', toValue(search), toValue(justification))),
    queryFn: async (): Promise<SupportPeoplePage> => {
      const { data, error, response } = await client.GET('/api/v{version}/admin/support/people', {
        params: {
          path: { version },
          query: {
            search: toValue(search).trim(),
            justification: toValue(justification).trim(),
            Page: 1,
            Take: take,
          },
        },
      });

      if (error !== undefined)
        throw problemError('support people', response.status, error);

      return data ?? {};
    },
    enabled: computed(() => filled(toValue(search)) && filled(toValue(justification))),
  });
}

export function useSupportPerson(
  id: MaybeRefOrGetter<string | null>,
  justification: MaybeRefOrGetter<string>,
): UseQueryReturnType<SupportPersonView | null, Error> {
  return useQuery({
    queryKey: computed(() => supportKey('person', toValue(id), toValue(justification))),
    queryFn: async (): Promise<SupportPersonView | null> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/admin/support/people/{subjectUserId}',
        {
          params: {
            path: { version, subjectUserId: toValue(id)! },
            query: { justification: toValue(justification).trim() },
          },
        },
      );

      if (error !== undefined)
        throw problemError('support person', response.status, error);

      return data?.data ?? null;
    },
    enabled: computed(() => filled(toValue(id)) && filled(toValue(justification))),
  });
}

export function useSupportHistory(
  id: MaybeRefOrGetter<string | null>,
  justification: MaybeRefOrGetter<string>,
): UseQueryReturnType<SupportHistoryPage, Error> {
  return useQuery({
    queryKey: computed(() => supportKey('history', toValue(id), toValue(justification))),
    queryFn: async (): Promise<SupportHistoryPage> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/admin/support/people/{subjectUserId}/history',
        {
          params: {
            path: { version, subjectUserId: toValue(id)! },
            query: {
              justification: toValue(justification).trim(),
              Page: 1,
              Take: take,
            },
          },
        },
      );

      if (error !== undefined)
        throw problemError('support history', response.status, error);

      return data ?? {};
    },
    enabled: computed(() => filled(toValue(id)) && filled(toValue(justification))),
  });
}

export type SupportTenantKind = 'commands' | 'pipelines';

// The custom commands or pipelines a channel built for itself. Both are one family: the kind picks the route.
export function useSupportTenantItems(
  kind: SupportTenantKind,
  channelId: MaybeRefOrGetter<string | null>,
  justification: MaybeRefOrGetter<string>,
): UseQueryReturnType<SupportCommandsPage | SupportPipelinesPage, Error> {
  return useQuery({
    queryKey: computed(() => supportKey(kind, toValue(channelId), toValue(justification))),
    queryFn: async (): Promise<SupportCommandsPage | SupportPipelinesPage> => {
      const options = {
        params: {
          path: { version, channelId: toValue(channelId)! },
          query: {
            justification: toValue(justification).trim(),
            Page: 1,
            Take: take,
          },
        },
      };
      const { data, error, response } =
        kind === 'commands'
          ? await client.GET('/api/v{version}/admin/support/tenants/{channelId}/commands', options)
          : await client.GET(
              '/api/v{version}/admin/support/tenants/{channelId}/pipelines',
              options,
            );

      if (error !== undefined)
        throw problemError(`support ${kind}`, response.status, error);

      return data ?? {};
    },
    enabled: computed(() => filled(toValue(channelId)) && filled(toValue(justification))),
  });
}
