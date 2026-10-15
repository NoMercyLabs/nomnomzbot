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

import type { KeyParts } from '@/lib/array';

import { client } from './client';
import { failed } from './errors';
import type { components } from './schema';

export type DiscordConnection = components['schemas']['DiscordGuildConnectionDto'];
export type DiscordRule = components['schemas']['DiscordNotificationConfigDto'];
export type DiscordRuleChange = components['schemas']['UpdateDiscordNotificationConfigRequest'];
export type DiscordRuleCreate = components['schemas']['CreateDiscordNotificationConfigRequest'];
export type DiscordEmbed = components['schemas']['DiscordEmbedDto'];
export type DiscordEmbedField = components['schemas']['DiscordEmbedFieldDto'];
export type DiscordPreview = components['schemas']['DiscordNotificationPreviewDto'];
export type DiscordChannel = components['schemas']['DiscordPostableChannelDto'];
export type DiscordRole = components['schemas']['DiscordGuildRoleDto'];

const version = '1';

export const discordConnectionsKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'discord-connections',
];
export const discordRulesKey = (
  channelId: string | null,
  connectionId: string | null,
): KeyParts => [
  'channels',
  channelId,
  'discord-rules',
  connectionId,
];
const discordPickersKey = (
  channelId: string | null,
  connectionId: string | null,
  kind: string,
): KeyParts => [
  'channels',
  channelId,
  'discord-picker',
  connectionId,
  kind,
];

export function useDiscordConnections(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<DiscordConnection[], Error> {
  return useQuery({
    queryKey: computed(() => discordConnectionsKey(toValue(channelId))),
    queryFn: async (): Promise<DiscordConnection[]> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/channels/{channelId}/discord/connections',
        { params: { path: { version, channelId: toValue(channelId) as string } } },
      );

      if (error !== undefined)
        throw failed('discord connections', response.status, error);

      return data?.data ?? [];
    },
    enabled: computed(() => toValue(channelId) !== null),
  });
}

export function useDiscordRules(
  channelId: MaybeRefOrGetter<string | null>,
  connectionId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<DiscordRule[], Error> {
  return useQuery({
    queryKey: computed(() => discordRulesKey(toValue(channelId), toValue(connectionId))),
    queryFn: async (): Promise<DiscordRule[]> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/channels/{channelId}/discord/connections/{connectionId}/configs',
        {
          params: {
            path: {
              version,
              channelId: toValue(channelId) as string,
              connectionId: toValue(connectionId) as string,
            },
          },
        },
      );

      if (error !== undefined)
        throw failed('discord rules', response.status, error);

      return data?.data ?? [];
    },
    enabled: computed(() => toValue(channelId) !== null && toValue(connectionId) !== null),
  });
}

export function useDiscordChannels(
  channelId: MaybeRefOrGetter<string | null>,
  connectionId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<DiscordChannel[], Error> {
  return useQuery({
    queryKey: computed(() => discordPickersKey(toValue(channelId), toValue(connectionId), 'channels')),
    queryFn: async (): Promise<DiscordChannel[]> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/channels/{channelId}/discord/connections/{connectionId}/guild/channels/postable',
        {
          params: {
            path: {
              version,
              channelId: toValue(channelId) as string,
              connectionId: toValue(connectionId) as string,
            },
          },
        },
      );

      if (error !== undefined)
        throw failed('discord channels', response.status, error);

      return data?.data ?? [];
    },
    enabled: computed(() => toValue(channelId) !== null && toValue(connectionId) !== null),
  });
}

export function useDiscordRoles(
  channelId: MaybeRefOrGetter<string | null>,
  connectionId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<DiscordRole[], Error> {
  return useQuery({
    queryKey: computed(() => discordPickersKey(toValue(channelId), toValue(connectionId), 'roles')),
    queryFn: async (): Promise<DiscordRole[]> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/channels/{channelId}/discord/connections/{connectionId}/guild/roles',
        {
          params: {
            path: {
              version,
              channelId: toValue(channelId) as string,
              connectionId: toValue(connectionId) as string,
            },
          },
        },
      );

      if (error !== undefined)
        throw failed('discord roles', response.status, error);

      return data?.data ?? [];
    },
    enabled: computed(() => toValue(channelId) !== null && toValue(connectionId) !== null),
  });
}

export type RuleSave = { configId: string; change: DiscordRuleChange };

export function useSaveDiscordRule(
  channelId: MaybeRefOrGetter<string | null>,
  connectionId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<DiscordRule | null, Error, RuleSave, undefined> {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async ({ configId, change }: RuleSave): Promise<DiscordRule | null> => {
      const { data, error, response } = await client.PUT(
        '/api/v{version}/channels/{channelId}/discord/configs/{configId}',
        {
          params: { path: {
            version,
            channelId: toValue(channelId) as string,
            configId,
          } },
          body: change,
        },
      );

      if (error !== undefined)
        throw failed('save discord rule', response.status, error);

      return data?.data ?? null;
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({
        queryKey: discordRulesKey(toValue(channelId), toValue(connectionId)),
      });
    },
  });
}

export function useCreateDiscordRule(
  channelId: MaybeRefOrGetter<string | null>,
  connectionId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<DiscordRule | null, Error, DiscordRuleCreate, undefined> {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async (body: DiscordRuleCreate): Promise<DiscordRule | null> => {
      const { data, error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/discord/connections/{connectionId}/configs',
        {
          params: {
            path: {
              version,
              channelId: toValue(channelId) as string,
              connectionId: toValue(connectionId) as string,
            },
          },
          body,
        },
      );

      if (error !== undefined)
        throw failed('create discord rule', response.status, error);

      return data?.data ?? null;
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({
        queryKey: discordRulesKey(toValue(channelId), toValue(connectionId)),
      });
    },
  });
}

// The preview renders the rule as stored, so the screen saves the draft first and then asks.
export function useDiscordPreview(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<DiscordPreview | null, Error, string, undefined> {
  return useMutation({
    mutationFn: async (configId: string): Promise<DiscordPreview | null> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/channels/{channelId}/discord/configs/{configId}/preview',
        { params: { path: {
          version,
          channelId: toValue(channelId) as string,
          configId,
        } } },
      );

      if (error !== undefined)
        throw failed('discord preview', response.status, error);

      return data?.data ?? null;
    },
  });
}
