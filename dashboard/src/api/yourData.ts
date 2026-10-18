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

import type { KeyParts } from '@/lib/array';

import { failed, refusal } from './channelQuery';
import { client } from './client';
import type { components } from './schema';

// The streamer's own data rights (GDPR): the caller's export, erasure, opt-out and consents. Every call acts on the
// signed-in person only; the bot forces the subject to the sign-in.
export type DataExport = components['schemas']['DataExport'];
export type ErasurePreview = components['schemas']['ErasurePreview'];
export type DataRequest = components['schemas']['ErasureRequest'];
export type ConsentRecord = components['schemas']['ConsentRecord'];

const version = '1';
const pageSize = 100;
const maxPages = 10;

// Person-wide, not per channel: the builders take no channel (they still fit the live-sync key builder).
export const dataRequestsKey = (): KeyParts => ['gdpr', 'requests'];
export const consentsKey = (): KeyParts => ['gdpr', 'consents'];

async function readRequests(): Promise<DataRequest[]> {
  const rows: DataRequest[] = [];

  for (let page = 1; page <= maxPages; page += 1) {
    const { data, error, response } = await client.GET('/api/v{version}/gdpr/requests', {
      params: { path: { version }, query: { Page: page, PageSize: pageSize } },
    });

    if (error !== undefined)
      throw failed('data requests', response.status, error);

    rows.push(...(data?.data ?? []));

    if (data?.hasMore !== true) break;
  }

  return rows;
}

async function readConsents(): Promise<ConsentRecord[]> {
  const { data, error, response } = await client.GET('/api/v{version}/gdpr/consents', {
    params: { path: { version } },
  });

  const rows = data?.data;

  if (error !== undefined || rows === undefined || rows === null)
    throw failed('consents', response.status, error);

  return [...rows];
}

// The bot publishes DataRightsChanged for every request and consent change (realtime/liveSync.ts).
export function useDataRequests(): UseQueryReturnType<DataRequest[], Error> {
  return useQuery({ queryKey: dataRequestsKey(), queryFn: readRequests });
}

export function useConsents(): UseQueryReturnType<ConsentRecord[], Error> {
  return useQuery({ queryKey: consentsKey(), queryFn: readConsents });
}

// Counted by the bot: how many rows of each kind erasure would destroy. An empty list is the real answer "nothing".
export async function fetchErasurePreview(): Promise<ErasurePreview> {
  const { data, error, response } = await client.GET('/api/v{version}/gdpr/erasure/preview', {
    params: { path: { version } },
  });

  const preview = data?.data;

  if (error !== undefined || preview === undefined || preview === null)
    throw failed('erasure preview', response.status, error);

  return preview;
}

// The export is one JSON document; the page hands it to the browser as a file.
export function useExportData(): UseMutationReturnType<DataExport, Error, void, undefined> {
  const queries = useQueryClient();

  return useMutation<DataExport, Error, void, undefined>({
    mutationFn: async (): Promise<DataExport> => {
      const { data, error, response } = await client.GET('/api/v{version}/gdpr/export', {
        params: { path: { version } },
      });

      const exported = data?.data;

      if (error !== undefined || exported === undefined || exported === null)
        throw refusal('export data', error, response.status);

      return exported;
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: dataRequestsKey() });
    },
  });
}

// Irreversible. The page asks only after it shows the counted preview.
export function useRequestErasure(): UseMutationReturnType<DataRequest, Error, void, undefined> {
  const queries = useQueryClient();

  return useMutation<DataRequest, Error, void, undefined>({
    mutationFn: async (): Promise<DataRequest> => {
      const { data, error, response } = await client.POST('/api/v{version}/gdpr/erasure', {
        params: { path: { version } },
      });

      const request = data?.data;

      if (error !== undefined || request === undefined || request === null)
        throw refusal('request erasure', error, response.status);

      return request;
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: dataRequestsKey() });
    },
  });
}

// Stops marketing, leaderboards and analytics use of the data. The data stays.
export function useRequestOptOut(): UseMutationReturnType<DataRequest, Error, void, undefined> {
  const queries = useQueryClient();

  return useMutation<DataRequest, Error, void, undefined>({
    mutationFn: async (): Promise<DataRequest> => {
      const { data, error, response } = await client.POST('/api/v{version}/gdpr/opt-out', {
        params: { path: { version } },
      });

      const request = data?.data;

      if (error !== undefined || request === undefined || request === null)
        throw refusal('opt out of processing', error, response.status);

      return request;
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: dataRequestsKey() });
    },
  });
}

export type ConsentChange = { record: ConsentRecord; granted: boolean };

// Granting writes a new row with the same legal basis and version; withdrawing stamps the row and keeps it listed.
export function useChangeConsent(): UseMutationReturnType<void, Error, ConsentChange, undefined> {
  const queries = useQueryClient();

  return useMutation<void, Error, ConsentChange, undefined>({
    mutationFn: async ({ record, granted }: ConsentChange): Promise<void> => {
      const answer = granted
        ? await client.POST('/api/v{version}/gdpr/consents', {
            params: { path: { version } },
            body: {
              subjectUserId: record.subjectUserId,
              broadcasterId: record.broadcasterId ?? null,
              consentType: record.consentType,
              lawfulBasis: record.lawfulBasis,
              consentVersion: record.consentVersion ?? null,
              source: 'dashboard',
            },
          })
        : await client.DELETE('/api/v{version}/gdpr/consents/{consentType}', {
            params: {
              path: { version, consentType: record.consentType },
              query: { broadcasterId: record.broadcasterId ?? undefined },
            },
          });

      if (answer.error !== undefined)
        throw refusal('change consent', answer.error, answer.response.status);
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: consentsKey() });
    },
  });
}
