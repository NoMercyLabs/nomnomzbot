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
  type UseQueryReturnType,
} from '@tanstack/vue-query';

import { client } from './client';
import { problemError } from './errors';
import type { components } from './schema';
import type { KeyParts } from '@/lib/array';

export type ActionDefault = components['schemas']['ActionDefault'];
export type PlatformDefaultBlastRadius = components['schemas']['PlatformDefaultBlastRadius'];
export type SetActionDefaultRequest = components['schemas']['SetActionDefaultRequest'];

const version = '1';

// Every platform-defaults query lives under this prefix, so one invalidation (a save here, or a live
// event handler later) refreshes every section of the page.
export const adminPlatformDefaultsKey: KeyParts = ['admin', 'platform-defaults'];

export const adminActionDefaultsKey: KeyParts = [...adminPlatformDefaultsKey, 'actions'];

// The default role each action asks for, with the shipped value, the platform's own value and the floor.
export function useAdminActionDefaults(): UseQueryReturnType<ActionDefault[], Error> {
  return useQuery({
    queryKey: adminActionDefaultsKey,
    queryFn: async (): Promise<ActionDefault[]> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/admin/platform-defaults/actions',
        { params: { path: { version } } },
      );

      if (error !== undefined)
        throw problemError('platform defaults actions', response.status, error);

      return [...(data?.data ?? [])];
    },
  });
}

// The counted blast radius of moving an action's default to a level; no level previews the shipped default.
export async function previewActionDefault(
  actionKey: string,
  level: number | null,
): Promise<PlatformDefaultBlastRadius> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/admin/platform-defaults/actions/{actionKey}/blast-radius',
    {
      params: {
        path: { version, actionKey },
        query: level === null
          ? {}
          : { level },
      },
    },
  );

  if (error !== undefined)
    throw problemError('platform defaults actions', response.status, error);

  if (!data?.data)
    throw problemError('platform defaults actions', response.status, { message: 'empty' });

  return data.data;
}

export type SaveActionDefault = { actionKey: string } & SetActionDefaultRequest;

// A saved default changes the list and every count behind it, so the whole page prefix is refetched.
export function useSaveActionDefault() {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async ({ actionKey, ...body }: SaveActionDefault): Promise<void> => {
      const { error, response } = await client.PUT(
        '/api/v{version}/admin/platform-defaults/actions/{actionKey}',
        { params: { path: { version, actionKey } }, body },
      );

      if (error !== undefined)
        throw problemError('platform defaults actions', response.status, error);
    },
    onSuccess: () => queries.invalidateQueries({ queryKey: adminPlatformDefaultsKey }),
  });
}
