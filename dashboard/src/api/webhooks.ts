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
  useMutation,
  useQueryClient,
  type UseMutationReturnType,
  type UseQueryReturnType,
} from '@tanstack/vue-query';
import { toValue, type MaybeRefOrGetter } from 'vue';

import type { KeyParts } from '@/lib/array';

import { channelQuery, failed, refusal } from './channelQuery';
import { client } from './client';
import type { components } from './schema';

// An outbound webhook posts the channel's business events to an outside address; an inbound one takes a
// provider's calls and starts a pipeline or event (webhooks.md).
export type OutboundWebhook = components['schemas']['OutboundWebhookEndpoint'];
export type OutboundWebhookDraft = components['schemas']['CreateOutboundWebhookRequest'];
export type InboundWebhook = components['schemas']['InboundWebhookEndpoint'];
// cspell:disable-next-line -- the generated schema name carries the British spelling
export type WebhookEventEntry = components['schemas']['OutboundWebhookEventCatalogueEntry'];
export type WebhookBlastRadius = components['schemas']['BlastRadius'];

const version = '1';
const pageSize = 100;

export const outboundWebhooksKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'webhooks-outbound',
];

export const inboundWebhooksKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'webhooks-inbound',
];

export const webhookEventCatalogKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'webhooks-event-catalog',
];

async function readOutbound(channelId: string): Promise<OutboundWebhook[]> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/webhooks/outbound',
    { params: { path: { version, channelId }, query: { PageSize: pageSize } } },
  );

  if (error !== undefined)
    throw failed('outbound webhooks', response.status, error);

  return data?.data ?? [];
}

async function readInbound(channelId: string): Promise<InboundWebhook[]> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/webhooks/inbound',
    { params: { path: { version, channelId }, query: { PageSize: pageSize } } },
  );

  if (error !== undefined)
    throw failed('inbound webhooks', response.status, error);

  return data?.data ?? [];
}

async function readCatalog(channelId: string): Promise<WebhookEventEntry[]> {
  const { data, error, response } = await client.GET(
    // cspell:disable-next-line -- the route path carries the British spelling
    '/api/v{version}/channels/{channelId}/webhooks/outbound/event-catalogue',
    { params: { path: { version, channelId } } },
  );

  const rows = data?.data;

  if (error !== undefined || rows === undefined || rows === null)
    throw failed('webhook event catalog', response.status, error);

  return rows;
}

// The bot publishes ConfigChanged "webhooks" for every create, edit and delete (realtime/configDomains.ts).
export function useOutboundWebhooks(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<OutboundWebhook[], Error> {
  return channelQuery(channelId, outboundWebhooksKey, readOutbound);
}

export function useInboundWebhooks(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<InboundWebhook[], Error> {
  return channelQuery(channelId, inboundWebhooksKey, readInbound);
}

export function useWebhookEventCatalog(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<WebhookEventEntry[], Error> {
  return channelQuery(channelId, webhookEventCatalogKey, readCatalog);
}

export type SaveOutboundWebhook = { id: string | null; draft: OutboundWebhookDraft };

type CreatedAnswer = components['schemas']['StatusResponseOfObject'];

// The create answer carries the signing secret once; the schema types its data as untyped.
function signingSecretOf(answer: CreatedAnswer | undefined): string | null {
  const created = answer?.data as { signingSecret?: string } | null | undefined;

  return created?.signingSecret ?? null;
}

// No id creates the endpoint and returns its signing secret (shown once, never readable again); an id sends
// the editable fields (the address of an endpoint is fixed once made) and returns null.
export function useSaveOutboundWebhook(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<string | null, Error, SaveOutboundWebhook, undefined> {
  const queries = useQueryClient();

  return useMutation<string | null, Error, SaveOutboundWebhook, undefined>({
    mutationFn: async ({ id, draft }: SaveOutboundWebhook): Promise<string | null> => {
      const channel = toValue(channelId) as string;

      const answer = id === null
        ? await client.POST('/api/v{version}/channels/{channelId}/webhooks/outbound', {
            params: { path: { version, channelId: channel } },
            body: draft,
          })
        : await client.PUT('/api/v{version}/channels/{channelId}/webhooks/outbound/{endpointId}', {
            params: { path: {
              version,
              channelId: channel,
              endpointId: id,
            } },
            body: {
              name: draft.name,
              subscribedEventTypes: draft.subscribedEventTypes,
              bodyTemplate: draft.bodyTemplate,
              bodyIsJson: draft.bodyIsJson,
              customHeaders: draft.customHeaders,
              isEnabled: draft.isEnabled,
            },
          });

      if (answer.error !== undefined)
        throw refusal('save outbound webhook', answer.error, answer.response.status);

      return id === null
        ? signingSecretOf(answer.data)
        : null;
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: outboundWebhooksKey(toValue(channelId)) });
    },
  });
}

export function useDeleteOutboundWebhook(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, string, undefined> {
  const queries = useQueryClient();

  return useMutation<void, Error, string, undefined>({
    mutationFn: async (endpointId: string): Promise<void> => {
      const { error, response } = await client.DELETE(
        '/api/v{version}/channels/{channelId}/webhooks/outbound/{endpointId}',
        { params: { path: {
          version,
          channelId: toValue(channelId) as string,
          endpointId,
        } } },
      );

      if (error !== undefined)
        throw refusal('delete outbound webhook', error, response.status);
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: outboundWebhooksKey(toValue(channelId)) });
    },
  });
}

// An endpoint the bot switched off after repeated failures stays off until it is turned back on here.
export function useReenableOutboundWebhook(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, string, undefined> {
  const queries = useQueryClient();

  return useMutation<void, Error, string, undefined>({
    mutationFn: async (endpointId: string): Promise<void> => {
      const { error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/webhooks/outbound/{endpointId}/reenable',
        { params: { path: {
          version,
          channelId: toValue(channelId) as string,
          endpointId,
        } } },
      );

      if (error !== undefined)
        throw refusal('reenable outbound webhook', error, response.status);
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: outboundWebhooksKey(toValue(channelId)) });
    },
  });
}

export type SetInboundEnabled = { id: string; isEnabled: boolean };

export function useSetInboundWebhookEnabled(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, SetInboundEnabled, undefined> {
  const queries = useQueryClient();

  return useMutation<void, Error, SetInboundEnabled, undefined>({
    mutationFn: async ({ id, isEnabled }: SetInboundEnabled): Promise<void> => {
      const { error, response } = await client.PUT(
        '/api/v{version}/channels/{channelId}/webhooks/inbound/{endpointId}',
        {
          params: { path: {
            version,
            channelId: toValue(channelId) as string,
            endpointId: id,
          } },
          body: { isEnabled },
        },
      );

      if (error !== undefined)
        throw refusal('save inbound webhook', error, response.status);
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: inboundWebhooksKey(toValue(channelId)) });
    },
  });
}

// The blast radius is counted by the bot before the confirm: how many event responses and pipelines use the endpoint.
export async function fetchInboundWebhookBlastRadius(
  channelId: string,
  endpointId: string,
): Promise<WebhookBlastRadius> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/webhooks/inbound/{endpointId}/blast-radius',
    { params: { path: {
      version,
      channelId,
      endpointId,
    } } },
  );

  if (error !== undefined || !data?.data)
    throw failed('inbound webhook blast radius', response.status, error);

  return data.data;
}

// The bot counts what an outbound webhook reaches before the confirm: pipeline steps that send to it and its deliveries.
export async function fetchOutboundWebhookBlastRadius(
  channelId: string,
  endpointId: string,
): Promise<WebhookBlastRadius> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/webhooks/outbound/{endpointId}/blast-radius',
    { params: { path: {
      version,
      channelId,
      endpointId,
    } } },
  );

  if (error !== undefined || !data?.data)
    throw failed('outbound webhook blast radius', response.status, error);

  return data.data;
}

export function useDeleteInboundWebhook(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, string, undefined> {
  const queries = useQueryClient();

  return useMutation<void, Error, string, undefined>({
    mutationFn: async (endpointId: string): Promise<void> => {
      const { error, response } = await client.DELETE(
        '/api/v{version}/channels/{channelId}/webhooks/inbound/{endpointId}',
        { params: { path: {
          version,
          channelId: toValue(channelId) as string,
          endpointId,
        } } },
      );

      if (error !== undefined)
        throw refusal('delete inbound webhook', error, response.status);
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: inboundWebhooksKey(toValue(channelId)) });
    },
  });
}
