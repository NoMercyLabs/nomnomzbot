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
  type UseMutationReturnType,
  type UseQueryReturnType,
} from '@tanstack/vue-query';
import { computed, toValue, type MaybeRefOrGetter } from 'vue';

import { client } from './client';
import { problemError, type ApiOperation } from './errors';
import type { components } from './schema';
import { merge, type KeyPart, type KeyParts } from '@/lib/array';

type Schemas = components['schemas'];

export type BillingTier = Schemas['Tier'];
export type TierChangePreview = Schemas['TierChangePreview'];
export type UpdateTierRequest = Schemas['UpdateTierRequest'];
export type CreateTierRequest = Schemas['CreateTierRequest'];
export type InviteCode = Schemas['InviteCode'];
export type CreateInviteCodeRequest = Schemas['CreateInviteCodeRequest'];
export type BillingInvoice = Schemas['Invoice'];
export type EntitlementGrant = Schemas['EntitlementGrant'];
export type EntitlementGrantPreview = Schemas['EntitlementGrantPreview'];
export type IssueEntitlementGrantRequest = Schemas['IssueEntitlementGrantRequest'];
export type PricedUnit = Schemas['PricedUnit'];
export type AuthorPricedUnitRequest = Schemas['AuthorPricedUnitRequest'];

const version = '1';

export const billingPrefix: KeyParts = ['admin', 'billing'];

export const billingKey = (...parts: readonly KeyPart[]) => merge(billingPrefix, parts);

export function useBillingTiers(): UseQueryReturnType<BillingTier[], Error> {
  return useQuery({
    queryKey: billingKey('tiers'),
    queryFn: async (): Promise<BillingTier[]> => {
      const { data, error, response } = await client.GET('/api/v{version}/admin/billing/tiers', {
        params: { path: { version } },
      });

      if (error !== undefined)
        throw problemError('billing tiers', response.status, error);

      return data?.data ?? [];
    },
  });
}

export function useBillingInvites(): UseQueryReturnType<InviteCode[], Error> {
  return useQuery({
    queryKey: billingKey('invites'),
    queryFn: async (): Promise<InviteCode[]> => {
      const { data, error, response } = await client.GET('/api/v{version}/admin/billing/invites', {
        params: { path: { version }, query: { Page: 1, Take: 50 } },
      });

      if (error !== undefined)
        throw problemError('billing invites', response.status, error);

      return data?.data ?? [];
    },
  });
}

export function useBillingPricedUnits(): UseQueryReturnType<PricedUnit[], Error> {
  return useQuery({
    queryKey: billingKey('priced-units'),
    queryFn: async (): Promise<PricedUnit[]> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/admin/billing/priced-units',
        { params: { path: { version } } },
      );

      if (error !== undefined)
        throw problemError('billing priced units', response.status, error);

      return data?.data ?? [];
    },
  });
}

// The channel reads wait for a channel id, so nothing loads until an operator picks one.
export function useBillingInvoices(
  broadcasterId: MaybeRefOrGetter<string>,
): UseQueryReturnType<BillingInvoice[], Error> {
  return useQuery({
    queryKey: computed(() => billingKey('invoices', toValue(broadcasterId))),
    queryFn: async (): Promise<BillingInvoice[]> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/admin/billing/channels/{broadcasterId}/invoices',
        { params: { path: { version, broadcasterId: toValue(broadcasterId) } } },
      );

      if (error !== undefined)
        throw problemError('billing invoices', response.status, error);

      return data?.data ?? [];
    },
    enabled: computed(() => toValue(broadcasterId).trim() !== ''),
  });
}

export function useBillingGrants(
  broadcasterId: MaybeRefOrGetter<string>,
): UseQueryReturnType<EntitlementGrant[], Error> {
  return useQuery({
    queryKey: computed(() => billingKey('grants', toValue(broadcasterId))),
    queryFn: async (): Promise<EntitlementGrant[]> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/admin/billing/channels/{broadcasterId}/grants',
        { params: { path: { version, broadcasterId: toValue(broadcasterId) } } },
      );

      if (error !== undefined)
        throw problemError('billing grants', response.status, error);

      return data?.data ?? [];
    },
    enabled: computed(() => toValue(broadcasterId).trim() !== ''),
  });
}

// The counted blast radius of a tier edit, read before the edit is saved.
export function useTierPreview(): UseMutationReturnType<
  TierChangePreview,
  Error,
  string,
  undefined
> {
  return useMutation({
    mutationFn: async (tierId: string): Promise<TierChangePreview> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/admin/billing/tiers/{tierId}/preview',
        { params: { path: { version, tierId } } },
      );

      if (error !== undefined)
        throw problemError('billing tier preview', response.status, error);

      return data?.data ?? { affectedTenantCount: 0, sampleChannelNames: [] };
    },
  });
}

export type GrantPreviewTarget = {
  broadcasterId: string;
  tierId: string;
};

// The limits a comp would change, read before the comp is issued.
export function useGrantPreview(): UseMutationReturnType<
  EntitlementGrantPreview,
  Error,
  GrantPreviewTarget,
  undefined
> {
  return useMutation({
    mutationFn: async ({
      broadcasterId,
      tierId,
    }: GrantPreviewTarget): Promise<EntitlementGrantPreview> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/admin/billing/channels/{broadcasterId}/grants/preview',
        { params: { path: { version, broadcasterId }, query: { tierId } } },
      );

      if (error !== undefined)
        throw problemError('billing grant preview', response.status, error);

      return data?.data ?? {
        currentTierKey: '',
        grantedTierKey: '',
        changedLimitCount: 0,
        changedLimitKeys: [],
      };
    },
  });
}

export type TierEdit = {
  tierId: string;
  body: UpdateTierRequest;
};

export type GrantIssue = {
  broadcasterId: string;
  body: IssueEntitlementGrantRequest;
};

export type TierGrant = {
  broadcasterId: string;
  tierId: string;
};

// Every write is followed by the server's billing push, which reloads the lists; the prefix
// invalidation here covers the operator's own tab before the push arrives.
function useBillingWrite<V>(
  run: (variables: V) => Promise<{ error?: object; response: Response }>,
  label: ApiOperation,
): UseMutationReturnType<void, Error, V, undefined> {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async (variables: V): Promise<void> => {
      const { error, response } = await run(variables);

      if (error !== undefined)
        throw problemError(label, response.status, error);
    },
    onSuccess: () => queries.invalidateQueries({ queryKey: billingPrefix }),
  });
}

export const useSaveTier = () =>
  useBillingWrite<TierEdit>(
    ({ tierId, body }) =>
      client.PUT('/api/v{version}/admin/billing/tiers/{tierId}', {
        params: { path: { version, tierId } },
        body,
      }),
    'billing tier save',
  );

export const useCreateTier = () =>
  useBillingWrite<CreateTierRequest>(
    (body) =>
      client.POST('/api/v{version}/admin/billing/tiers', { params: { path: { version } }, body }),
    'billing tier create',
  );

export const useCreateInvite = () =>
  useBillingWrite<CreateInviteCodeRequest>(
    (body) =>
      client.POST('/api/v{version}/admin/billing/invites', { params: { path: { version } }, body }),
    'billing invite create',
  );

export const useRevokeInvite = () =>
  useBillingWrite<string>(
    (inviteCodeId) =>
      client.POST('/api/v{version}/admin/billing/invites/{inviteCodeId}/revoke', {
        params: { path: { version, inviteCodeId } },
      }),
    'billing invite revoke',
  );

export const useGrantTier = () =>
  useBillingWrite<TierGrant>(
    ({ broadcasterId, tierId }) =>
      client.POST('/api/v{version}/admin/billing/channels/{broadcasterId}/grant-tier', {
        params: { path: { version, broadcasterId } },
        body: { tierId, isInviteOnlyGrant: false },
      }),
    'billing tier grant',
  );

export const useGrantFounder = () =>
  useBillingWrite<string>(
    (broadcasterId) =>
      client.POST('/api/v{version}/admin/billing/channels/{broadcasterId}/grant-founder', {
        params: { path: { version, broadcasterId } },
      }),
    'billing founder grant',
  );

export const useRefundInvoice = () =>
  useBillingWrite<string>(
    (invoiceId) =>
      client.POST('/api/v{version}/admin/billing/invoices/{invoiceId}/refund', {
        params: { path: { version, invoiceId } },
      }),
    'billing refund',
  );

export const useIssueGrant = () =>
  useBillingWrite<GrantIssue>(
    ({ broadcasterId, body }) =>
      client.POST('/api/v{version}/admin/billing/channels/{broadcasterId}/grants', {
        params: { path: { version, broadcasterId } },
        body,
      }),
    'billing grant issue',
  );

export const useAuthorPricedUnit = () =>
  useBillingWrite<AuthorPricedUnitRequest>(
    (body) =>
      client.POST('/api/v{version}/admin/billing/priced-units', {
        params: { path: { version } },
        body,
      }),
    'billing priced unit',
  );
