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
import { computed, toValue, type MaybeRefOrGetter } from 'vue';

import { client } from './client';
import { problemError } from './errors';
import type { components } from './schema';
import type { KeyParts } from '@/lib/array';

export type ContentDefinition = components['schemas']['PlatformContentDefinition'];
export type ContentDefinitionDetail = components['schemas']['PlatformContentDefinitionDetail'];
export type ContentVersion = components['schemas']['PlatformContentVersion'];
export type CreateContentDefinitionBody = components['schemas']['CreateContentDefinitionRequest'];

export type DraftTarget = {
  definitionId: string;
  payloadJson: string;
};

const version = '1';
const DEFINITION_TAKE: number = 100;

export const contentPrefix: KeyParts = ['admin', 'content'];

export function useContentDefinitions(
  kind: MaybeRefOrGetter<string>,
): UseQueryReturnType<ContentDefinition[], Error> {
  return useQuery({
    queryKey: computed((): KeyParts => ['admin', 'content', 'list', toValue(kind)]),
    queryFn: async (): Promise<ContentDefinition[]> => {
      const trimmed = toValue(kind).trim();
      const { data, error, response } = await client.GET(
        '/api/v{version}/platform/content/definitions',
        {
          params: {
            path: { version },
            query: { kind: trimmed === ''
              ? undefined
              : trimmed, Take: DEFINITION_TAKE },
          },
        },
      );

      if (error !== undefined)
        throw problemError('content definitions', response.status, error);

      return data?.data?.data ?? [];
    },
  });
}

export function useContentDefinition(
  id: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<ContentDefinitionDetail | null, Error> {
  return useQuery({
    queryKey: computed((): KeyParts => ['admin', 'content', 'detail', toValue(id)]),
    enabled: computed(() => toValue(id) !== null),
    queryFn: async (): Promise<ContentDefinitionDetail | null> => {
      const definitionId = toValue(id);

      if (definitionId === null)
        return null;

      const { data, error, response } = await client.GET(
        '/api/v{version}/platform/content/definitions/{id}',
        { params: { path: { version, id: definitionId } } },
      );

      if (error !== undefined)
        throw problemError('content definition', response.status, error);

      return data?.data ?? null;
    },
  });
}

export function useCreateContentDefinition() {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async (body: CreateContentDefinitionBody): Promise<void> => {
      const { error, response } = await client.POST(
        '/api/v{version}/platform/content/definitions',
        { params: { path: { version } }, body },
      );

      if (error !== undefined)
        throw problemError('content definition create', response.status, error);
    },
    onSuccess: () => queries.invalidateQueries({ queryKey: contentPrefix }),
  });
}

export function useDraftContentVersion() {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async ({ definitionId, payloadJson }: DraftTarget): Promise<void> => {
      const { error, response } = await client.POST(
        '/api/v{version}/platform/content/definitions/{id}/versions',
        { params: { path: { version, id: definitionId } }, body: { payloadJson } },
      );

      if (error !== undefined)
        throw problemError('content version draft', response.status, error);
    },
    onSuccess: () => queries.invalidateQueries({ queryKey: contentPrefix }),
  });
}

export function useRetireContentDefinition() {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async (definitionId: string): Promise<void> => {
      const { error, response } = await client.DELETE(
        '/api/v{version}/platform/content/definitions/{id}',
        { params: { path: { version, id: definitionId } } },
      );

      if (error !== undefined)
        throw problemError('content definition retire', response.status, error);
    },
    onSuccess: () => queries.invalidateQueries({ queryKey: contentPrefix }),
  });
}
