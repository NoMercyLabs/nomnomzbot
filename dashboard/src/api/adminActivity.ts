// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import {
  keepPreviousData,
  useMutation,
  useQuery,
  useQueryClient,
  type UseQueryReturnType,
} from '@tanstack/vue-query';
import { computed, toValue, type MaybeRefOrGetter } from 'vue';

import { client } from './client';
import { problemError } from './errors';
import type { components } from './schema';
import { merge, type KeyParts } from '@/lib/array';

export type AdminEventSubTenantHealth = components['schemas']['AdminEventSubTenantHealth'];
export type AdminEventSubTopicHealth = components['schemas']['AdminEventSubTopicHealth'];
export type AdminEventSubHealthPage =
  components['schemas']['PaginatedResponseOfAdminEventSubTenantHealth'];

export type AdminPageParams = {
  page: number;
  take: number;
};

const version = '1';

export const adminEventSubHealthPrefix: KeyParts = ['admin', 'eventsub-health'];

function eventSubHealthKey(params: AdminPageParams): KeyParts {
  const joined = merge(adminEventSubHealthPrefix, [{ ...params }]);

  return Array.from(joined);
}

export function useAdminEventSubHealth(
  params: MaybeRefOrGetter<AdminPageParams>,
): UseQueryReturnType<AdminEventSubHealthPage, Error> {
  return useQuery({
    queryKey: computed((): KeyParts => eventSubHealthKey(toValue(params))),
    queryFn: async (): Promise<AdminEventSubHealthPage> => {
      const { page, take } = toValue(params);
      const { data, error, response } = await client.GET('/api/v{version}/admin/eventsub/health', {
        params: { path: { version }, query: { Page: page, Take: take } },
      });

      // A refusal is an error state, not an empty registry.
      if (error !== undefined)
        throw problemError('admin eventsub health', response.status, error);

      return data ?? {};
    },
    placeholderData: keepPreviousData,
  });
}

export type AdminWebhookDelivery = components['schemas']['AdminWebhookDelivery'];
export type AdminWebhookDeliveriesPage =
  components['schemas']['PaginatedResponseOfAdminWebhookDelivery'];
export type AdminWebhookReplayResult = components['schemas']['AdminWebhookReplayResult'];

export const adminWebhookDeliveriesPrefix: KeyParts = ['admin', 'webhook-deliveries'];

function webhookDeliveriesKey(params: AdminPageParams): KeyParts {
  const joined = merge(adminWebhookDeliveriesPrefix, [{ ...params }]);

  return Array.from(joined);
}

export function useAdminWebhookDeliveries(
  params: MaybeRefOrGetter<AdminPageParams>,
): UseQueryReturnType<AdminWebhookDeliveriesPage, Error> {
  return useQuery({
    queryKey: computed((): KeyParts => webhookDeliveriesKey(toValue(params))),
    queryFn: async (): Promise<AdminWebhookDeliveriesPage> => {
      const { page, take } = toValue(params);
      const { data, error, response } = await client.GET(
        '/api/v{version}/admin/webhooks/deliveries',
        { params: { path: { version }, query: { Page: page, Take: take } } },
      );

      if (error !== undefined)
        throw problemError('admin webhook deliveries', response.status, error);

      return data ?? {};
    },
    placeholderData: keepPreviousData,
  });
}

// A replay appends a new attempt row, so the log is read again afterwards.
export function useAdminWebhookReplay() {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async (deliveryId: number): Promise<AdminWebhookReplayResult | null> => {
      const { data, error, response } = await client.POST(
        '/api/v{version}/admin/webhooks/deliveries/{deliveryId}/replay',
        { params: { path: { version, deliveryId } } },
      );

      if (error !== undefined)
        throw problemError('admin webhook replay', response.status, error);

      return data?.data ?? null;
    },
    onSuccess: () => queries.invalidateQueries({ queryKey: adminWebhookDeliveriesPrefix }),
  });
}
