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
  useQuery,
  useQueryClient,
  type UseMutationReturnType,
  type UseQueryReturnType,
} from '@tanstack/vue-query';
import { computed, toValue, type MaybeRefOrGetter } from 'vue';

import { channelQuery, failed, refusal } from './channelQuery';
import { client } from './client';
import { problemError } from './errors';
import type { components } from './schema';
import type { KeyParts } from '@/lib/array';

export type CurrencyConfig = components['schemas']['CurrencyConfig'];

export const currencyConfigKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'economy',
  'config',
];

// The channel's own currency: its name and whether it is switched on. Null when the channel has none.
export function useCurrencyConfig(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<CurrencyConfig | null, Error> {
  return useQuery({
    queryKey: computed(() => currencyConfigKey(toValue(channelId))),
    queryFn: async (): Promise<CurrencyConfig | null> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/channels/{channelId}/economy/config',
        { params: { path: { version: '1', channelId: toValue(channelId) as string } } },
      );

      if (error !== undefined)
        throw problemError('currency config', response.status, error);

      return data?.data ?? null;
    },
    enabled: computed(() => toValue(channelId) !== null),
  });
}

export type CurrencyChange = components['schemas']['UpsertCurrencyConfigRequest'];
export type EarningRule = components['schemas']['EarningRule'];
export type EarningRuleChange = components['schemas']['UpsertEarningRuleRequest'];
export type SavingsJar = components['schemas']['SavingsJar'];
export type NewSavingsJar = components['schemas']['CreateSavingsJarRequest'];
export type SavingsJarChange = components['schemas']['UpdateSavingsJarRequest'];

const version = '1';

export const earningRulesKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'economy',
  'earning-rules',
];

export const savingsJarsKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'economy',
  'jars',
];

// The write hooks all end the same way: the bot accepted the change, so the list it changes is read again.
function useEconomyWrite<TVariables>(
  channelId: MaybeRefOrGetter<string | null>,
  key: (channelId: string | null) => KeyParts,
  send: (channelId: string, variables: TVariables) => Promise<void>,
): UseMutationReturnType<void, Error, TVariables, undefined> {
  const queries = useQueryClient();

  return useMutation<void, Error, TVariables, undefined>({
    mutationFn: (variables: TVariables): Promise<void> =>
      send(toValue(channelId) as string, variables),
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: key(toValue(channelId)) });
    },
  });
}

export function useSaveCurrencyConfig(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, CurrencyChange, undefined> {
  return useEconomyWrite(
    channelId,
    currencyConfigKey,
    async (id: string, change: CurrencyChange): Promise<void> => {
      const { error, response } = await client.PUT(
        '/api/v{version}/channels/{channelId}/economy/config',
        { params: { path: { version, channelId: id } }, body: change },
      );

      if (error !== undefined)
        throw refusal('save currency', error, response.status);
    },
  );
}

export function useEarningRules(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<readonly EarningRule[], Error> {
  return channelQuery(
    channelId,
    earningRulesKey,
    async (id: string): Promise<readonly EarningRule[]> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/channels/{channelId}/economy/earning-rules',
        { params: { path: { version, channelId: id } } },
      );

      if (error !== undefined)
        throw failed('earning rules', response.status, error);

      return data?.data ?? [];
    },
  );
}

// The bot keeps one rule per source: saving a source that exists changes it, a new source adds a rule.
export function useSaveEarningRule(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, EarningRuleChange, undefined> {
  return useEconomyWrite(
    channelId,
    earningRulesKey,
    async (id: string, change: EarningRuleChange): Promise<void> => {
      const { error, response } = await client.PUT(
        '/api/v{version}/channels/{channelId}/economy/earning-rules',
        { params: { path: { version, channelId: id } }, body: change },
      );

      if (error !== undefined)
        throw refusal('save earning rule', error, response.status);
    },
  );
}

export function useDeleteEarningRule(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, string, undefined> {
  return useEconomyWrite(
    channelId,
    earningRulesKey,
    async (id: string, ruleId: string): Promise<void> => {
      const { error, response } = await client.DELETE(
        '/api/v{version}/channels/{channelId}/economy/earning-rules/{ruleId}',
        {
          params: {
            path: {
              version,
              channelId: id,
              ruleId,
            },
          },
        },
      );

      if (error !== undefined)
        throw refusal('delete earning rule', error, response.status);
    },
  );
}

export function useSavingsJars(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<readonly SavingsJar[], Error> {
  return channelQuery(
    channelId,
    savingsJarsKey,
    async (id: string): Promise<readonly SavingsJar[]> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/channels/{channelId}/economy/jars',
        { params: { path: { version, channelId: id } } },
      );

      if (error !== undefined)
        throw failed('savings jars', response.status, error);

      return data?.data ?? [];
    },
  );
}

export function useCreateSavingsJar(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, NewSavingsJar, undefined> {
  return useEconomyWrite(
    channelId,
    savingsJarsKey,
    async (id: string, jar: NewSavingsJar): Promise<void> => {
      const { error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/economy/jars',
        { params: { path: { version, channelId: id } }, body: jar },
      );

      if (error !== undefined)
        throw refusal('create savings jar', error, response.status);
    },
  );
}

export function useUpdateSavingsJar(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, { jarId: string; change: SavingsJarChange }, undefined> {
  return useEconomyWrite(
    channelId,
    savingsJarsKey,
    async (
      id: string,
      { jarId, change }: { jarId: string; change: SavingsJarChange },
    ): Promise<void> => {
      const { error, response } = await client.PATCH(
        '/api/v{version}/channels/{channelId}/economy/jars/{jarId}',
        {
          params: {
            path: {
              version,
              channelId: id,
              jarId,
            },
          },
          body: change,
        },
      );

      if (error !== undefined)
        throw refusal('save savings jar', error, response.status);
    },
  );
}

export function useDeleteSavingsJar(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, string, undefined> {
  return useEconomyWrite(
    channelId,
    savingsJarsKey,
    async (id: string, jarId: string): Promise<void> => {
      const { error, response } = await client.DELETE(
        '/api/v{version}/channels/{channelId}/economy/jars/{jarId}',
        {
          params: {
            path: {
              version,
              channelId: id,
              jarId,
            },
          },
        },
      );

      if (error !== undefined)
        throw refusal('delete savings jar', error, response.status);
    },
  );
}
