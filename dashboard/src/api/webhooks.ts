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

import {
  useMutation,
  useQuery,
  useQueryClient,
  type UseMutationReturnType,
  type UseQueryReturnType,
} from '@tanstack/vue-query';
import { computed, toValue, type MaybeRefOrGetter } from 'vue';

import { merge, type KeyParts } from '@/lib/array';

import { channelQuery, failed, refusal } from './channelQuery';
import { client } from './client';
import type { components } from './schema';

// An outbound webhook posts the channel's business events to an outside address; an inbound one takes a
// provider's calls and starts a pipeline or event (webhooks.md).
export type OutboundWebhook = components['schemas']['OutboundWebhookEndpoint'];
export type OutboundWebhookDraft = components['schemas']['CreateOutboundWebhookRequest'];
export type InboundWebhook = components['schemas']['InboundWebhookEndpoint'];
// cspell:disable-next-line -- the generated schema name carries the British spelling
type CatalogEntrySchema = components['schemas']['OutboundWebhookEventCatalogueEntry'];

// A server older than the resource-key change sends plain label and category text instead of the keys; the
// dialog reads either, so both are optional here.
export type WebhookEventEntry = Pick<CatalogEntrySchema, 'eventType'> &
  Partial<Pick<CatalogEntrySchema, 'labelKey' | 'categoryKey'>> & {
    label?: string;
    category?: string;
  };

export type WebhookBlastRadius = components['schemas']['BlastRadius'];
export type InboundWebhookDraft = components['schemas']['CreateInboundWebhookRequest'];
export type OutboundDelivery = components['schemas']['OutboundWebhookDelivery'];

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

      const answer =
        id === null
          ? await client.POST('/api/v{version}/channels/{channelId}/webhooks/outbound', {
              params: { path: { version, channelId: channel } },
              body: draft,
            })
          : await client.PUT(
              '/api/v{version}/channels/{channelId}/webhooks/outbound/{endpointId}',
              {
                params: {
                  path: {
                    version,
                    channelId: channel,
                    endpointId: id,
                  },
                },
                body: {
                  name: draft.name,
                  subscribedEventTypes: draft.subscribedEventTypes,
                  bodyTemplate: draft.bodyTemplate,
                  bodyIsJson: draft.bodyIsJson,
                  customHeaders: draft.customHeaders,
                  isEnabled: draft.isEnabled,
                },
              },
            );

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
        {
          params: {
            path: {
              version,
              channelId: toValue(channelId) as string,
              endpointId,
            },
          },
        },
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
        {
          params: {
            path: {
              version,
              channelId: toValue(channelId) as string,
              endpointId,
            },
          },
        },
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
          params: {
            path: {
              version,
              channelId: toValue(channelId) as string,
              endpointId: id,
            },
          },
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
    {
      params: {
        path: {
          version,
          channelId,
          endpointId,
        },
      },
    },
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
    {
      params: {
        path: {
          version,
          channelId,
          endpointId,
        },
      },
    },
  );

  if (error !== undefined || !data?.data)
    throw failed('outbound webhook blast radius', response.status, error);

  return data.data;
}

export type SaveInboundWebhook = { id: string | null; draft: InboundWebhookDraft };

// No id creates the endpoint; an id sends the editable fields. The adapter is fixed once made, and a secret
// left empty on an edit is not sent, so the stored one stays.
export function useSaveInboundWebhook(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, SaveInboundWebhook, undefined> {
  const queries = useQueryClient();

  return useMutation<void, Error, SaveInboundWebhook, undefined>({
    mutationFn: async ({ id, draft }: SaveInboundWebhook): Promise<void> => {
      const channel = toValue(channelId) as string;

      const answer =
        id === null
          ? await client.POST('/api/v{version}/channels/{channelId}/webhooks/inbound', {
              params: { path: { version, channelId: channel } },
              body: draft,
            })
          : await client.PUT('/api/v{version}/channels/{channelId}/webhooks/inbound/{endpointId}', {
              params: {
                path: {
                  version,
                  channelId: channel,
                  endpointId: id,
                },
              },
              body: {
                name: draft.name,
                ...(draft.verificationSecret === ''
                  ? {}
                  : { verificationSecret: draft.verificationSecret }),
                targetPipelineId: draft.targetPipelineId,
                targetEventType: draft.targetEventType,
                genericConfig: draft.genericConfig,
                isEnabled: draft.isEnabled,
              },
            });

      if (answer.error !== undefined)
        throw refusal('save inbound webhook', answer.error, answer.response.status);
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: inboundWebhooksKey(toValue(channelId)) });
    },
  });
}

// The old link stops working at once; the list refetches and shows the new one.
export function useRotateInboundWebhookLink(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, string, undefined> {
  const queries = useQueryClient();

  return useMutation<void, Error, string, undefined>({
    mutationFn: async (endpointId: string): Promise<void> => {
      const { error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/webhooks/inbound/{endpointId}/rotate-token',
        {
          params: {
            path: {
              version,
              channelId: toValue(channelId) as string,
              endpointId,
            },
          },
        },
      );

      if (error !== undefined)
        throw refusal('rotate inbound webhook link', error, response.status);
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: inboundWebhooksKey(toValue(channelId)) });
    },
  });
}

// The new signing secret comes back once, like the one from a create.
export function useRotateOutboundWebhookSecret(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<string | null, Error, string, undefined> {
  const queries = useQueryClient();

  return useMutation<string | null, Error, string, undefined>({
    mutationFn: async (endpointId: string): Promise<string | null> => {
      const { data, error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/webhooks/outbound/{endpointId}/rotate-secret',
        {
          params: {
            path: {
              version,
              channelId: toValue(channelId) as string,
              endpointId,
            },
          },
        },
      );

      if (error !== undefined)
        throw refusal('rotate outbound webhook secret', error, response.status);

      return signingSecretOf(data);
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: outboundWebhooksKey(toValue(channelId)) });
    },
  });
}

export type WebhookTestResult = {
  delivered: boolean;
  responseCode: number | null;
  durationMs: number | null;
  error: string | null;
};

// A test sends one sample event to the address and reports what came back; it is not stored as a delivery to retry.
export function useTestOutboundWebhook(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<WebhookTestResult, Error, string, undefined> {
  return useMutation<WebhookTestResult, Error, string, undefined>({
    mutationFn: async (endpointId: string): Promise<WebhookTestResult> => {
      const { data, error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/webhooks/outbound/{endpointId}/test',
        {
          params: {
            path: {
              version,
              channelId: toValue(channelId) as string,
              endpointId,
            },
          },
        },
      );

      if (error !== undefined)
        throw refusal('test outbound webhook', error, response.status);

      // The snapshot lists no success body for this call, so the envelope is read through its known shape.
      const envelope = data as { data?: Partial<WebhookTestResult> | null } | undefined;
      const outcome = envelope?.data;

      return {
        delivered: outcome?.delivered ?? false,
        responseCode: outcome?.responseCode ?? null,
        durationMs: outcome?.durationMs ?? null,
        error: outcome?.error ?? null,
      };
    },
  });
}

export function outboundDeliveriesKey(
  channelId: string | null,
  endpointId: string | null,
): KeyParts {
  const joined = merge(outboundWebhooksKey(channelId), [endpointId, 'deliveries']);

  return Array.from(joined);
}

// The delivery log of one endpoint, newest first. The key sits under the outbound list key, so a webhooks
// change from the bot refetches it too.
export function useOutboundDeliveries(
  channelId: MaybeRefOrGetter<string | null>,
  endpointId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<OutboundDelivery[], Error> {
  return useQuery({
    queryKey: computed(() => outboundDeliveriesKey(toValue(channelId), toValue(endpointId))),
    queryFn: async (): Promise<OutboundDelivery[]> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/channels/{channelId}/webhooks/outbound/{endpointId}/deliveries',
        {
          params: {
            path: {
              version,
              channelId: toValue(channelId) as string,
              endpointId: toValue(endpointId) as string,
            },
            query: { PageSize: pageSize },
          },
        },
      );

      if (error !== undefined)
        throw failed('outbound webhook deliveries', response.status, error);

      return data?.data ?? [];
    },
    enabled: computed(() => toValue(channelId) !== null && toValue(endpointId) !== null),
  });
}

export type RetryDelivery = { endpointId: string; deliveryId: number };

export function useRetryOutboundDelivery(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, RetryDelivery, undefined> {
  const queries = useQueryClient();

  return useMutation<void, Error, RetryDelivery, undefined>({
    mutationFn: async ({ endpointId, deliveryId }: RetryDelivery): Promise<void> => {
      const { error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/webhooks/outbound/{endpointId}/deliveries/{deliveryId}/retry',
        {
          params: {
            path: {
              version,
              channelId: toValue(channelId) as string,
              endpointId,
              deliveryId,
            },
          },
        },
      );

      if (error !== undefined)
        throw refusal('retry outbound delivery', error, response.status);
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: outboundWebhooksKey(toValue(channelId)) });
    },
  });
}

export function useDeleteInboundWebhook(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, string, undefined> {
  const queries = useQueryClient();

  return useMutation<void, Error, string, undefined>({
    mutationFn: async (endpointId: string): Promise<void> => {
      const { error, response } = await client.DELETE(
        '/api/v{version}/channels/{channelId}/webhooks/inbound/{endpointId}',
        {
          params: {
            path: {
              version,
              channelId: toValue(channelId) as string,
              endpointId,
            },
          },
        },
      );

      if (error !== undefined)
        throw refusal('delete inbound webhook', error, response.status);
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: inboundWebhooksKey(toValue(channelId)) });
    },
  });
}
