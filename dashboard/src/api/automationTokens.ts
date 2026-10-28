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
  useQueryClient,
  type UseMutationReturnType,
  type UseQueryReturnType,
} from '@tanstack/vue-query';
import { toValue, type MaybeRefOrGetter } from 'vue';

import type { KeyParts } from '@/lib/array';

import { channelQuery, failed, refusal } from './channelQuery';
import { client } from './client';
import type { components } from './schema';

// An external tool's key to this channel (Stream Deck, Touch Portal, scripts). Only a prefix of the
// secret is ever listed; the full secret shows once, when the token is made or rotated.
export type AutomationToken = components['schemas']['AutomationToken'];
export type IssuedToken = components['schemas']['IssuedAutomationToken'];
export type NewAutomationToken = components['schemas']['CreateAutomationTokenRequest'];
export type PairingCode = components['schemas']['PairingCode'];
export type PairingRequest = components['schemas']['MintPairingCodeRequest'];
export type AutomationEvent = components['schemas']['AutomationEventCatalogItem'];

// The data-plane scopes the bot accepts on a token. `chat` lets the tool speak as the bot.
export const automationScopes = ['invoke', 'read', 'events', 'chat'] as const;

const version = '1';
const pageSize = 100;
const maxPages = 20;
const listRefresh = 30_000;

export const automationTokensKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'automation-tokens',
];

export const automationEventsKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'automation-events',
];

async function readTokens(channelId: string): Promise<AutomationToken[]> {
  const rows: AutomationToken[] = [];

  for (let page = 1; page <= maxPages; page += 1) {
    const { data, error, response } = await client.GET(
      '/api/v{version}/channels/{channelId}/automation/tokens',
      { params: { path: { version, channelId }, query: { Page: page, PageSize: pageSize } } },
    );

    if (error !== undefined)
      throw failed('automation tokens', response.status, error);

    rows.push(...(data?.data ?? []));

    if (data?.hasMore !== true) break;
  }

  return rows;
}

// The server sends no hub event for token changes, so the list re-reads every 30 seconds and on every
// save made here.
export function useAutomationTokens(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<AutomationToken[], Error> {
  return channelQuery(channelId, automationTokensKey, readTokens, listRefresh);
}

async function readEvents(channelId: string): Promise<AutomationEvent[]> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/automation/events/catalog',
    { params: { path: { version, channelId } } },
  );

  const rows = data?.data;

  if (error !== undefined || rows === undefined || rows === null)
    throw failed('automation event catalog', response.status, error);

  return rows;
}

// The public event names a token with the `events` scope may stream.
export function useAutomationEvents(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<AutomationEvent[], Error> {
  return channelQuery(channelId, automationEventsKey, readEvents);
}

export function useCreateAutomationToken(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<IssuedToken, Error, NewAutomationToken, undefined> {
  const queries = useQueryClient();

  return useMutation<IssuedToken, Error, NewAutomationToken, undefined>({
    mutationFn: async (body: NewAutomationToken): Promise<IssuedToken> => {
      const { data, error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/automation/tokens',
        { params: { path: { version, channelId: toValue(channelId) as string } }, body },
      );

      const issued = data?.data;

      if (error !== undefined || issued === undefined || issued === null)
        throw refusal('create automation token', error, response.status);

      return issued;
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: automationTokensKey(toValue(channelId)) });
    },
  });
}

// The old secret stops working at once; the new one shows once.
export function useRotateAutomationToken(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<IssuedToken, Error, string, undefined> {
  const queries = useQueryClient();

  return useMutation<IssuedToken, Error, string, undefined>({
    mutationFn: async (tokenId: string): Promise<IssuedToken> => {
      const { data, error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/automation/tokens/{tokenId}/rotate',
        { params: { path: {
          version,
          channelId: toValue(channelId) as string,
          tokenId,
        } } },
      );

      const issued = data?.data;

      if (error !== undefined || issued === undefined || issued === null)
        throw refusal('rotate automation token', error, response.status);

      return issued;
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: automationTokensKey(toValue(channelId)) });
    },
  });
}

// Revoking a paired device unpairs it. The row stays in the list, marked revoked.
export function useRevokeAutomationToken(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, string, undefined> {
  const queries = useQueryClient();

  return useMutation<void, Error, string, undefined>({
    mutationFn: async (tokenId: string): Promise<void> => {
      const { error, response } = await client.DELETE(
        '/api/v{version}/channels/{channelId}/automation/tokens/{tokenId}',
        { params: { path: {
          version,
          channelId: toValue(channelId) as string,
          tokenId,
        } } },
      );

      if (error !== undefined)
        throw refusal('revoke automation token', error, response.status);
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: automationTokensKey(toValue(channelId)) });
    },
  });
}

// A short-lived single-use code the device types in to pair itself with this channel.
export function useMintPairingCode(): UseMutationReturnType<
  PairingCode,
  Error,
  PairingRequest,
  undefined
> {
  return useMutation<PairingCode, Error, PairingRequest, undefined>({
    mutationFn: async (body: PairingRequest): Promise<PairingCode> => {
      const { data, error, response } = await client.POST('/api/v{version}/automation/pair-codes', {
        params: { path: { version } },
        body,
      });

      const minted = data?.data;

      if (error !== undefined || minted === undefined || minted === null)
        throw refusal('mint pairing code', error, response.status);

      return minted;
    },
  });
}

// A device that started pairing shows a code on its own screen; typing it here approves that device.
export function useApproveDevice(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, string, undefined> {
  const queries = useQueryClient();

  return useMutation<void, Error, string, undefined>({
    mutationFn: async (userCode: string): Promise<void> => {
      const { error, response } = await client.POST(
        '/api/v{version}/automation/pair/device/approve',
        { params: { path: { version } }, body: { userCode } },
      );

      if (error !== undefined)
        throw refusal('approve device', error, response.status);
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: automationTokensKey(toValue(channelId)) });
    },
  });
}
