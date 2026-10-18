// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { keepPreviousData, useQuery, type UseQueryReturnType } from '@tanstack/vue-query';
import { computed, toValue, type MaybeRefOrGetter } from 'vue';

import { client } from './client';
import { problemError } from './errors';
import type { components } from './schema';
import { merge, type KeyParts } from '@/lib/array';

export type AdminAuditEntry = components['schemas']['IamAuditEntry'];
export type AdminAuditPage = components['schemas']['PaginatedResponseOfIamAuditEntry'];

// An empty text filter is left out of the request, so the server never receives a blank.
export type AdminAuditParams = {
  page: number;
  take: number;
  permission: string;
  outcome: string;
  principalId: string;
  targetBroadcasterId: string;
  from: string;
  to: string;
};

const version = '1';

export const adminAuditPrefix: KeyParts = ['admin', 'audit'];

function auditKey(params: AdminAuditParams): KeyParts {
  const joined = merge(adminAuditPrefix, [{ ...params }]);

  return Array.from(joined);
}

function textOrUndefined(value: string): string | undefined {
  const trimmed = value.trim();

  return trimmed === ''
    ? undefined
    : trimmed;
}

// A date input gives a day; the range covers that whole day in UTC.
function startOfDay(day: string): string | undefined {
  return day === ''
    ? undefined
    : `${day}T00:00:00Z`;
}

function endOfDay(day: string): string | undefined {
  return day === ''
    ? undefined
    : `${day}T23:59:59Z`;
}

export function useAdminAudit(
  params: MaybeRefOrGetter<AdminAuditParams>,
): UseQueryReturnType<AdminAuditPage, Error> {
  return useQuery({
    queryKey: computed((): KeyParts => auditKey(toValue(params))),
    queryFn: async (): Promise<AdminAuditPage> => {
      const filters = toValue(params);
      const { data, error, response } = await client.GET('/api/v{version}/admin/audit', {
        params: {
          path: { version },
          query: {
            Page: filters.page,
            Take: filters.take,
            permission: textOrUndefined(filters.permission),
            outcome: textOrUndefined(filters.outcome),
            principalId: textOrUndefined(filters.principalId),
            targetBroadcasterId: textOrUndefined(filters.targetBroadcasterId),
            from: startOfDay(filters.from),
            to: endOfDay(filters.to),
          },
        },
      });

      if (error !== undefined)
        throw problemError('admin audit', response.status, error);

      return data ?? {};
    },
    placeholderData: keepPreviousData,
  });
}
