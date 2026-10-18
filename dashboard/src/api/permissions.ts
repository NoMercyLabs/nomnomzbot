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

export type Membership = components['schemas']['ChannelMembership'];
export type ActionPermission = components['schemas']['ActionPermission'];
export type PersonGrant = components['schemas']['Permission'];

// The ladder, lowest first. The bot compares the numbers; the page only ever shows the names.
export const rungs = [
  { name: 'Everyone', level: 0 },
  { name: 'Subscriber', level: 2 },
  { name: 'Vip', level: 4 },
  { name: 'Artist', level: 6 },
  { name: 'Moderator', level: 10 },
  { name: 'LeadModerator', level: 20 },
  { name: 'Editor', level: 30 },
  { name: 'Broadcaster', level: 40 },
] as const;

export type RungName = (typeof rungs)[number]['name'];

// The rung names alone, lowest first: what a select of "who may use this" lists.
export const rungNames: readonly string[] = rungs.map((rung) => rung.name);

// The roles a person can be given on the team. The order is the bot's role order.
export const teamRoles = ['Moderator', 'LeadModerator', 'Editor', 'Broadcaster'] as const;

export type TeamRole = (typeof teamRoles)[number];

// The highest rung at or below a level, as the bot reads it.
export function rungOf(level: number | string): RungName {
  const value = Number(level);
  const reached = rungs.filter((rung) => rung.level <= value);

  return reached[reached.length - 1]?.name ?? 'Everyone';
}

export function levelOf(name: RungName): number {
  return rungs.find((rung) => rung.name === name)?.level ?? 0;
}

export function teamRoleOf(role: number | string): TeamRole {
  return teamRoles[Number(role)] ?? 'Moderator';
}

const version = '1';
const pageSize = 100;
const maxPages = 20;

export const membersKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'management-roles',
];

export const actionMatrixKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'action-permissions',
];

export const personGrantsKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'person-grants',
];

async function readMembers(channelId: string): Promise<Membership[]> {
  const rows: Membership[] = [];

  for (let page = 1; page <= maxPages; page += 1) {
    const { data, error, response } = await client.GET('/api/v{version}/channels/{channelId}/roles', {
      params: { path: { version, channelId }, query: { page, pageSize } },
    });

    if (error !== undefined)
      throw failed('management roles', response.status, error);

    rows.push(...(data?.data ?? []));

    if (data?.hasMore !== true) break;
  }

  return rows;
}

async function readMatrix(channelId: string): Promise<ActionPermission[]> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/action-permissions',
    { params: { path: { version, channelId } } },
  );

  const rows = data?.data;

  if (error !== undefined || rows === undefined || rows === null)
    throw failed('action permission matrix', response.status, error);

  return rows;
}

async function readGrants(channelId: string): Promise<PersonGrant[]> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/permissions',
    { params: { path: { version, channelId } } },
  );

  const rows = data?.data;

  if (error !== undefined || rows === undefined || rows === null)
    throw failed('person grants', response.status, error);

  return rows;
}

// The bot publishes PermissionChanged when a person's grant changes (realtime/liveSync.ts).
export function useMembers(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<Membership[], Error> {
  return channelQuery(channelId, membersKey, readMembers);
}

export function useActionMatrix(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<ActionPermission[], Error> {
  return channelQuery(channelId, actionMatrixKey, readMatrix);
}

export function usePersonGrants(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<PersonGrant[], Error> {
  return channelQuery(channelId, personGrantsKey, readGrants);
}

export type SetMemberRole = { userId: string; role: TeamRole };

export function useSetMemberRole(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, SetMemberRole, undefined> {
  const queries = useQueryClient();

  return useMutation<void, Error, SetMemberRole, undefined>({
    mutationFn: async ({ userId, role }: SetMemberRole): Promise<void> => {
      const { error, response } = await client.PUT('/api/v{version}/channels/{channelId}/roles', {
        params: { path: { version, channelId: toValue(channelId) as string } },
        body: { userId, role: teamRoles.indexOf(role) },
      });

      if (error !== undefined)
        throw refusal('set management role', error, response.status);
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: membersKey(toValue(channelId)) });
    },
  });
}

export function useRemoveMember(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, string, undefined> {
  const queries = useQueryClient();

  return useMutation<void, Error, string, undefined>({
    mutationFn: async (userId: string): Promise<void> => {
      const { error, response } = await client.DELETE('/api/v{version}/channels/{channelId}/roles/{userId}', {
        params: { path: {
          version,
          channelId: toValue(channelId) as string,
          userId,
        } },
      });

      if (error !== undefined)
        throw refusal('remove management role', error, response.status);
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: membersKey(toValue(channelId)) });
    },
  });
}

export type SetActionLevel = { actionKey: string; rung: RungName };

// The page picks a rung name; the bot gets the ladder value it compares. The bot clamps it to the action's floor.
export function useSetActionLevel(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, SetActionLevel, undefined> {
  const queries = useQueryClient();

  return useMutation<void, Error, SetActionLevel, undefined>({
    mutationFn: async ({ actionKey, rung }: SetActionLevel): Promise<void> => {
      const { error, response } = await client.PUT(
        '/api/v{version}/channels/{channelId}/action-permissions/{actionKey}',
        {
          params: { path: {
            version,
            channelId: toValue(channelId) as string,
            actionKey,
          } },
          body: { level: levelOf(rung) },
        },
      );

      if (error !== undefined)
        throw refusal('set action level', error, response.status);
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: actionMatrixKey(toValue(channelId)) });
    },
  });
}

export function useResetActionLevel(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, string, undefined> {
  const queries = useQueryClient();

  return useMutation<void, Error, string, undefined>({
    mutationFn: async (actionKey: string): Promise<void> => {
      const { error, response } = await client.DELETE(
        '/api/v{version}/channels/{channelId}/action-permissions/{actionKey}',
        { params: { path: {
          version,
          channelId: toValue(channelId) as string,
          actionKey,
        } } },
      );

      if (error !== undefined)
        throw refusal('reset action level', error, response.status);
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: actionMatrixKey(toValue(channelId)) });
    },
  });
}

export type GrantCapability = { userId: string; permission: string };

export function useGrantCapability(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, GrantCapability, undefined> {
  const queries = useQueryClient();

  return useMutation<void, Error, GrantCapability, undefined>({
    mutationFn: async (grant: GrantCapability): Promise<void> => {
      const { error, response } = await client.POST('/api/v{version}/channels/{channelId}/permissions', {
        params: { path: { version, channelId: toValue(channelId) as string } },
        body: grant,
      });

      if (error !== undefined)
        throw refusal('grant person capability', error, response.status);
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: personGrantsKey(toValue(channelId)) });
    },
  });
}

export function useRevokeCapability(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, GrantCapability, undefined> {
  const queries = useQueryClient();

  return useMutation<void, Error, GrantCapability, undefined>({
    mutationFn: async ({ userId, permission }: GrantCapability): Promise<void> => {
      const { error, response } = await client.DELETE(
        '/api/v{version}/channels/{channelId}/permissions/{userId}',
        {
          params: {
            path: {
              version,
              channelId: toValue(channelId) as string,
              userId,
            },
            query: { permission },
          },
        },
      );

      if (error !== undefined)
        throw refusal('revoke person capability', error, response.status);
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: personGrantsKey(toValue(channelId)) });
    },
  });
}
