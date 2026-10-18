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

export type IamPrincipal = components['schemas']['IamPrincipalSummary'];
export type IamRole = components['schemas']['IamRole'];
export type IamAssignment = components['schemas']['IamRoleAssignment'];

export type AssignRoleInput = {
  principalId: string;
  roleId: string;
  reason: string | null;
};

export type ReasonedTarget = {
  id: string;
  reason: string | null;
};

const version = '1';

export const accessPrefix: KeyParts = ['admin', 'access'];

const principalsKey: KeyParts = ['admin', 'access', 'principals'];
const rolesKey: KeyParts = ['admin', 'access', 'roles'];

export function useIamPrincipals(): UseQueryReturnType<IamPrincipal[], Error> {
  return useQuery({
    queryKey: principalsKey,
    queryFn: async (): Promise<IamPrincipal[]> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/platform/iam/principals',
        { params: { path: { version } } },
      );

      if (error !== undefined)
        throw problemError('access principals', response.status, error);

      return data?.data ?? [];
    },
  });
}

export function useIamRoles(): UseQueryReturnType<IamRole[], Error> {
  return useQuery({
    queryKey: rolesKey,
    queryFn: async (): Promise<IamRole[]> => {
      const { data, error, response } = await client.GET('/api/v{version}/platform/iam/roles', {
        params: { path: { version } },
      });

      if (error !== undefined)
        throw problemError('access roles', response.status, error);

      return data?.data ?? [];
    },
  });
}

export function useAssignIamRole() {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async ({ principalId, roleId, reason }: AssignRoleInput): Promise<void> => {
      const { error, response } = await client.POST('/api/v{version}/platform/iam/assignments', {
        params: { path: { version } },
        body: {
          principalId,
          roleId,
          reason,
        },
      });

      if (error !== undefined)
        throw problemError('access assign', response.status, error);
    },
    onSuccess: () => queries.invalidateQueries({ queryKey: accessPrefix }),
  });
}

export function useRevokeIamAssignment() {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async ({ id, reason }: ReasonedTarget): Promise<void> => {
      const { error, response } = await client.DELETE(
        '/api/v{version}/platform/iam/assignments/{assignmentId}',
        {
          params: {
            path: { version, assignmentId: id },
            query: { reason: reason ?? undefined },
          },
        },
      );

      if (error !== undefined)
        throw problemError('access revoke', response.status, error);
    },
    onSuccess: () => queries.invalidateQueries({ queryKey: accessPrefix }),
  });
}

export function useDeactivateIamPrincipal() {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async ({ id, reason }: ReasonedTarget): Promise<void> => {
      const { error, response } = await client.POST(
        '/api/v{version}/platform/iam/principals/{principalId}/deactivate',
        {
          params: {
            path: { version, principalId: id },
            query: { reason: reason ?? undefined },
          },
        },
      );

      if (error !== undefined)
        throw problemError('access deactivate', response.status, error);
    },
    onSuccess: () => queries.invalidateQueries({ queryKey: accessPrefix }),
  });
}

export function useReactivateIamPrincipal() {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async (principalId: string): Promise<void> => {
      const { error, response } = await client.POST(
        '/api/v{version}/platform/iam/principals/{principalId}/reactivate',
        { params: { path: { version, principalId } } },
      );

      if (error !== undefined)
        throw problemError('access reactivate', response.status, error);
    },
    onSuccess: () => queries.invalidateQueries({ queryKey: accessPrefix }),
  });
}

export function useEndTenantAccess() {
  return useMutation({
    mutationFn: async (accessGrantId: string): Promise<void> => {
      const { error, response } = await client.DELETE(
        '/api/v{version}/admin/access/{accessGrantId}',
        { params: { path: { version, accessGrantId } } },
      );

      if (error !== undefined)
        throw problemError('access end', response.status, error);
    },
  });
}
