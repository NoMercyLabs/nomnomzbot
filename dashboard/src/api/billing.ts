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

import { channelQuery, failed, refusal } from './channelQuery';
import { client } from './client';
import type { components } from './schema';

// A channel's own billing: the plan it is on, the plans on offer, its invoices, the founders badge and the
// invite codes. The limits, usage and entitlement of the plan are read by resourceLimits.ts. A change shows
// on every open dashboard through the `billing` config change of the hub, which refetches all of these keys.
export type Subscription = components['schemas']['Subscription'];
export type BillingTier = components['schemas']['Tier'];
export type ChannelInvoice = components['schemas']['Invoice'];
export type FoundersBadge = components['schemas']['FoundersBadge'];
export type InviteCheck = components['schemas']['InviteCodeValidation'];
export type InviteRedeemed = components['schemas']['RedeemInviteCodeResult'];
export type TierChange = components['schemas']['ChangeTierRequest'];
export type PlanCancel = components['schemas']['CancelSubscriptionRequest'];

export type InvoicePage = {
  invoices: ChannelInvoice[];
  hasMore: boolean;
  total: number | null;
};

const version = '1';

export const invoicePageSize = 10;

export const billingKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'billing',
];

export const subscriptionKey = (channelId: string | null): KeyParts => [
  ...billingKey(channelId),
  'subscription',
];

export const tiersKey = (channelId: string | null): KeyParts => [
  ...billingKey(channelId),
  'tiers',
];

export const foundersBadgeKey = (channelId: string | null): KeyParts => [
  ...billingKey(channelId),
  'founders-badge',
];

export const invoicesKey = (channelId: string | null, page: number): KeyParts => [
  ...billingKey(channelId),
  'invoices',
  page,
];

async function readSubscription(channelId: string): Promise<Subscription> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/billing/subscription',
    { params: { path: { version, channelId } } },
  );

  const found = data?.data;

  if (error !== undefined || found === undefined || found === null)
    throw failed('billing subscription', response.status, error);

  return found;
}

async function readTiers(channelId: string): Promise<BillingTier[]> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/billing/tiers',
    { params: { path: { version, channelId } } },
  );

  const found = data?.data;

  if (error !== undefined || found === undefined || found === null)
    throw failed('channel billing tiers', response.status, error);

  return found;
}

// A channel without the badge answers with no data, which is a normal answer and not a failure.
async function readFoundersBadge(channelId: string): Promise<FoundersBadge | null> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/billing/founders-badge',
    { params: { path: { version, channelId } } },
  );

  if (error !== undefined)
    throw failed('billing founders badge', response.status, error);

  return data?.data ?? null;
}

async function readInvoices(channelId: string, page: number): Promise<InvoicePage> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/billing/invoices',
    {
      params: {
        path: { version, channelId },
        query: { Page: page, PageSize: invoicePageSize },
      },
    },
  );

  if (error !== undefined || data === undefined)
    throw failed('channel billing invoices', response.status, error);

  return {
    invoices: data.data ?? [],
    hasMore: data.hasMore ?? false,
    total: data.total === undefined || data.total === null
      ? null
      : Number(data.total),
  };
}

export function useSubscription(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<Subscription, Error> {
  return channelQuery(channelId, subscriptionKey, readSubscription);
}

export function useBillingTiers(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<BillingTier[], Error> {
  return channelQuery(channelId, tiersKey, readTiers);
}

export function useFoundersBadge(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<FoundersBadge | null, Error> {
  return channelQuery(channelId, foundersBadgeKey, readFoundersBadge);
}

export function useInvoices(
  channelId: MaybeRefOrGetter<string | null>,
  page: MaybeRefOrGetter<number>,
): UseQueryReturnType<InvoicePage, Error> {
  return useQuery({
    queryKey: computed(() => invoicesKey(toValue(channelId), toValue(page))),
    queryFn: () => readInvoices(toValue(channelId) as string, toValue(page)),
    enabled: computed(() => toValue(channelId) !== null),
  });
}

// Every billing write ends the same way: all billing queries of the channel are stale.
function useBillingRefresh(channelId: MaybeRefOrGetter<string | null>): () => Promise<void> {
  const queries = useQueryClient();

  return () => queries.invalidateQueries({ queryKey: billingKey(toValue(channelId)) });
}

// The page where the person pays. The caller sends the browser there.
export function useStartCheckout(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<string, Error, string, undefined> {
  return useMutation<string, Error, string, undefined>({
    mutationFn: async (tierKey: string): Promise<string> => {
      const { data, error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/billing/checkout',
        {
          params: { path: { version, channelId: toValue(channelId) as string } },
          body: { tierKey },
        },
      );

      const session = data?.data;

      if (error !== undefined)
        throw refusal('start checkout', error, response.status);

      if (session === undefined || session === null)
        throw failed('start checkout', response.status);

      return session.checkoutUrl;
    },
  });
}

export function useChangePlan(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, TierChange, undefined> {
  const refresh = useBillingRefresh(channelId);

  return useMutation<void, Error, TierChange, undefined>({
    mutationFn: async (change: TierChange): Promise<void> => {
      const { error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/billing/change-tier',
        {
          params: { path: { version, channelId: toValue(channelId) as string } },
          body: change,
        },
      );

      if (error !== undefined)
        throw refusal('change plan', error, response.status);
    },
    onSuccess: refresh,
  });
}

export function useCancelPlan(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, PlanCancel, undefined> {
  const refresh = useBillingRefresh(channelId);

  return useMutation<void, Error, PlanCancel, undefined>({
    mutationFn: async (cancel: PlanCancel): Promise<void> => {
      const { error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/billing/cancel',
        {
          params: { path: { version, channelId: toValue(channelId) as string } },
          body: cancel,
        },
      );

      if (error !== undefined)
        throw refusal('cancel plan', error, response.status);
    },
    onSuccess: refresh,
  });
}

export function useResumePlan(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, void, undefined> {
  const refresh = useBillingRefresh(channelId);

  return useMutation<void, Error, void, undefined>({
    mutationFn: async (): Promise<void> => {
      const { error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/billing/resume',
        { params: { path: { version, channelId: toValue(channelId) as string } } },
      );

      if (error !== undefined)
        throw refusal('resume plan', error, response.status);
    },
    onSuccess: refresh,
  });
}

// The page where the person manages the card and the receipts at the payment provider.
export function useOpenBillingPortal(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<string, Error, void, undefined> {
  return useMutation<string, Error, void, undefined>({
    mutationFn: async (): Promise<string> => {
      const { data, error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/billing/portal',
        { params: { path: { version, channelId: toValue(channelId) as string } } },
      );

      const portal = data?.data;

      if (error !== undefined)
        throw refusal('billing portal', error, response.status);

      if (portal === undefined || portal === null)
        throw failed('billing portal', response.status);

      return portal.portalUrl;
    },
  });
}

// A look at a code without using it up.
export function useCheckInviteCode(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<InviteCheck, Error, string, undefined> {
  return useMutation<InviteCheck, Error, string, undefined>({
    mutationFn: async (code: string): Promise<InviteCheck> => {
      const { data, error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/billing/invite/validate',
        {
          params: { path: { version, channelId: toValue(channelId) as string } },
          body: { code },
        },
      );

      const check = data?.data;

      if (error !== undefined)
        throw refusal('check invite code', error, response.status);

      if (check === undefined || check === null)
        throw failed('check invite code', response.status);

      return check;
    },
  });
}

export function useRedeemInviteCode(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<InviteRedeemed, Error, string, undefined> {
  const refresh = useBillingRefresh(channelId);

  return useMutation<InviteRedeemed, Error, string, undefined>({
    mutationFn: async (code: string): Promise<InviteRedeemed> => {
      const { data, error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/billing/invite/redeem',
        {
          params: { path: { version, channelId: toValue(channelId) as string } },
          body: { code },
        },
      );

      const redeemed = data?.data;

      if (error !== undefined)
        throw refusal('redeem invite code', error, response.status);

      if (redeemed === undefined || redeemed === null)
        throw failed('redeem invite code', response.status);

      return redeemed;
    },
    onSuccess: refresh,
  });
}
