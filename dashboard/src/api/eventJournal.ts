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
} from '@tanstack/vue-query';
import { toValue, type MaybeRefOrGetter } from 'vue';

import { refusal } from './channelQuery';
import { client } from './client';
import type { components } from './schema';

export type JournalImportSummary = components['schemas']['EventJournalImportSummary'];
export type LegacyImportResult = components['schemas']['LegacyImportResult'];
export type ProjectionRebuild = components['schemas']['ProjectionRebuildResult'];

export type JournalFile = { text: string; lines: number };

const version = '1';

type ChannelId = MaybeRefOrGetter<string | null>;

const pathFor = (channelId: ChannelId): { path: { version: string; channelId: string } } => ({
  path: { version, channelId: toValue(channelId) as string },
});

// Replaying or rebuilding changes what the read models hold, so every query of this channel is read again.
function useRefreshChannel(channelId: ChannelId): () => Promise<void> {
  const queries = useQueryClient();

  return () =>
    queries.invalidateQueries({ queryKey: ['channels', toValue(channelId)] });
}

export function useExportJournal(
  channelId: ChannelId,
): UseMutationReturnType<JournalFile, Error, void, undefined> {
  return useMutation<JournalFile, Error, void, undefined>({
    mutationFn: async (): Promise<JournalFile> => {
      const { data, error, response } = await client.POST(
        '/api/v{version}/event-store/channels/{channelId}/export',
        { params: pathFor(channelId), parseAs: 'text' },
      );

      if (error !== undefined || typeof data !== 'string')
        throw refusal('export event journal', error, response.status);

      const lines = data.split('\n').filter((line) => line.trim() !== '').length;

      return { text: data, lines };
    },
  });
}

// The multipart body is built here: the generated type names the file part, the form carries the real file.
export function useImportJournal(
  channelId: ChannelId,
): UseMutationReturnType<JournalImportSummary, Error, File, undefined> {
  const refresh = useRefreshChannel(channelId);

  return useMutation<JournalImportSummary, Error, File, undefined>({
    mutationFn: async (file: File): Promise<JournalImportSummary> => {
      const form = new FormData();

      form.append('file', file);

      const { data, error, response } = await client.POST(
        '/api/v{version}/event-store/channels/{channelId}/import',
        {
          params: pathFor(channelId),
          body: { file: '' },
          bodySerializer: () => form,
        },
      );

      const summary = data?.data;

      if (error !== undefined || summary === undefined || summary === null)
        throw refusal('import event journal', error, response.status);

      return summary;
    },
    onSuccess: refresh,
  });
}

export function useReplayImportedEvents(
  channelId: ChannelId,
): UseMutationReturnType<number, Error, void, undefined> {
  const refresh = useRefreshChannel(channelId);

  return useMutation<number, Error, void, undefined>({
    mutationFn: async (): Promise<number> => {
      const { data, error, response } = await client.POST(
        '/api/v{version}/event-store/channels/{channelId}/replay',
        { params: pathFor(channelId) },
      );

      if (error !== undefined || data?.data === undefined)
        throw refusal('replay imported events', error, response.status);

      return Number(data.data);
    },
    onSuccess: refresh,
  });
}

export function useImportLegacyHistory(
  channelId: ChannelId,
): UseMutationReturnType<LegacyImportResult, Error, void, undefined> {
  const refresh = useRefreshChannel(channelId);

  return useMutation<LegacyImportResult, Error, void, undefined>({
    mutationFn: async (): Promise<LegacyImportResult> => {
      const { data, error, response } = await client.POST(
        '/api/v{version}/event-store/channels/{channelId}/import-legacy',
        { params: pathFor(channelId) },
      );

      const result = data?.data;

      if (error !== undefined || result === undefined || result === null)
        throw refusal('import legacy history', error, response.status);

      return result;
    },
    onSuccess: refresh,
  });
}

export function useRebuildProjections(
  channelId: ChannelId,
): UseMutationReturnType<ProjectionRebuild[], Error, void, undefined> {
  const refresh = useRefreshChannel(channelId);

  return useMutation<ProjectionRebuild[], Error, void, undefined>({
    mutationFn: async (): Promise<ProjectionRebuild[]> => {
      const { data, error, response } = await client.POST(
        '/api/v{version}/event-store/channels/{channelId}/rebuild-projections',
        { params: pathFor(channelId) },
      );

      const rows = data?.data;

      if (error !== undefined || rows === undefined || rows === null)
        throw refusal('rebuild projections', error, response.status);

      return [...rows];
    },
    onSuccess: refresh,
  });
}
