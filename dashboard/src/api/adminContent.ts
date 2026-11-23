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
  type UseQueryReturnType,
} from '@tanstack/vue-query';
import { computed, toValue, type MaybeRefOrGetter } from 'vue';

import { client } from './client';
import { problemError, refused } from './errors';
import type { components } from './schema';
import type { KeyParts } from '@/lib/array';

export type ContentDefinition = components['schemas']['PlatformContentDefinition'];
export type ContentDefinitionDetail = components['schemas']['PlatformContentDefinitionDetail'];
export type ContentVersion = components['schemas']['PlatformContentVersion'];
export type CreateContentDefinitionBody = components['schemas']['CreateContentDefinitionRequest'];
export type PublishPreview = components['schemas']['PublishPreview'];
export type PublishJob = components['schemas']['PlatformContentPublishJob'];

export type DraftTarget = {
  definitionId: string;
  payloadJson: string;
};

export type VersionTarget = {
  definitionId: string;
  versionId: string;
};

export type PreviewTarget = VersionTarget & {
  mode: string;
};

export type PublishTarget = PreviewTarget & {
  confirmedCount: number;
};

// The three publish modes, the safest first. Force is the one that can overwrite a channel's own edits.
export const PUBLISH_MODES = [
  'publish_as_new',
  'update_in_place_where_untouched',
  'force',
] as const;

export type PublishMode = (typeof PUBLISH_MODES)[number];

const version = '1';
const DEFINITION_TAKE: number = 100;
const JOB_POLL_MS = 2000;

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

      return data?.data ?? [];
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

export function useContentVersion(
  definitionId: MaybeRefOrGetter<string | null>,
  versionId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<ContentVersion | null, Error> {
  return useQuery({
    queryKey: computed((): KeyParts => ['admin', 'content', 'version', toValue(definitionId), toValue(versionId)]),
    enabled: computed(() => toValue(definitionId) !== null && toValue(versionId) !== null),
    queryFn: async (): Promise<ContentVersion | null> => {
      const id = toValue(definitionId);
      const pickedVersion = toValue(versionId);

      if (id === null || pickedVersion === null)
        return null;

      const { data, error, response } = await client.GET(
        '/api/v{version}/platform/content/definitions/{id}/versions/{versionId}',
        { params: { path: {
          version,
          id,
          versionId: pickedVersion,
        } } },
      );

      if (error !== undefined)
        throw problemError('content version', response.status, error);

      return data?.data ?? null;
    },
  });
}

export function usePublishJob(
  jobId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<PublishJob | null, Error> {
  return useQuery({
    queryKey: computed((): KeyParts => ['admin', 'content', 'job', toValue(jobId)]),
    enabled: computed(() => toValue(jobId) !== null),
    queryFn: async (): Promise<PublishJob | null> => {
      const id = toValue(jobId);

      if (id === null)
        return null;

      const { data, error, response } = await client.GET(
        '/api/v{version}/platform/content/publish-jobs/{id}',
        { params: { path: { version, id } } },
      );

      if (error !== undefined)
        throw problemError('content publish job', response.status, error);

      return data?.data ?? null;
    },
    // A running publish is read again until it ends.
    refetchInterval: (query) =>
      query.state.data?.status === 'running'
        ? JOB_POLL_MS
        : false,
  });
}

export function usePublishPreview() {
  return useMutation({
    mutationFn: async (target: PreviewTarget): Promise<PublishPreview> => {
      const { definitionId, versionId, mode } = target;
      const { data, error, response } = await client.POST(
        '/api/v{version}/platform/content/definitions/{id}/versions/{versionId}/publish-preview',
        { params: { path: {
          version,
          id: definitionId,
          versionId,
        } }, body: { mode } },
      );

      if (error !== undefined)
        throw problemError('content publish preview', response.status, error);

      if (!data?.data)
        throw refused('content publish preview');

      return data.data;
    },
  });
}

export function usePublish() {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async (target: PublishTarget): Promise<PublishJob> => {
      const { definitionId, versionId, mode, confirmedCount } = target;
      const { data, error, response } = await client.POST(
        '/api/v{version}/platform/content/definitions/{id}/versions/{versionId}/publish',
        {
          params: { path: {
            version,
            id: definitionId,
            versionId,
          } },
          body: { mode, confirmedPreviewAffectedCount: confirmedCount },
        },
      );

      if (error !== undefined)
        throw problemError('content publish', response.status, error);

      if (!data?.data)
        throw refused('content publish');

      return data.data;
    },
    onSuccess: () => queries.invalidateQueries({ queryKey: contentPrefix }),
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
