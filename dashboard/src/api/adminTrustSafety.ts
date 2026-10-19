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

export type TrustSafetyReviewItem = components['schemas']['TrustSafetyReviewItem'];
export type CrossTenantAbuseSignal = components['schemas']['CrossTenantAbuseSignal'];
export type NetworkBlock = components['schemas']['NetworkBlock'];
export type NetworkBlockPreview = components['schemas']['NetworkBlockPreview'];

// Every read and action of the desk is audited with the operator's reason, so each call carries it.
export type ReviewTarget = {
  detectionId: string;
  justification: string;
};

export type LiftTarget = {
  blockId: string;
  justification: string;
};

export type ApplyBlockTarget = {
  targetTwitchUserId: string;
  reason: string;
  justification: string;
  confirmedTenantCount: number;
};

export type PreviewParams = {
  targetTwitchUserId: string;
  justification: string;
};

const version = '1';
const REVIEW_TAKE: number = 50;

export const trustSafetyPrefix: KeyParts = ['admin', 'trust-safety'];

export function useReviewQueue(
  justification: MaybeRefOrGetter<string>,
  allowed: MaybeRefOrGetter<boolean>,
): UseQueryReturnType<TrustSafetyReviewItem[], Error> {
  return useQuery({
    queryKey: computed((): KeyParts => [
      'admin',
      'trust-safety',
      'review-queue',
      toValue(justification),
    ]),
    enabled: computed(() => toValue(allowed) && toValue(justification).trim() !== ''),
    queryFn: async (): Promise<TrustSafetyReviewItem[]> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/admin/trust-safety/review-queue',
        {
          params: {
            path: { version },
            query: { justification: toValue(justification), Take: REVIEW_TAKE },
          },
        },
      );

      if (error !== undefined)
        throw problemError('trust safety review queue', response.status, error);

      return data?.data ?? [];
    },
  });
}

export function useCrossTenantSignals(
  justification: MaybeRefOrGetter<string>,
  allowed: MaybeRefOrGetter<boolean>,
): UseQueryReturnType<CrossTenantAbuseSignal[], Error> {
  return useQuery({
    queryKey: computed((): KeyParts => [
      'admin',
      'trust-safety',
      'signals',
      toValue(justification),
    ]),
    enabled: computed(() => toValue(allowed) && toValue(justification).trim() !== ''),
    queryFn: async (): Promise<CrossTenantAbuseSignal[]> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/admin/trust-safety/cross-tenant-signals',
        { params: { path: { version }, query: { justification: toValue(justification) } } },
      );

      if (error !== undefined)
        throw problemError('trust safety signals', response.status, error);

      return data?.data ?? [];
    },
  });
}

export function useNetworkBlocks(
  justification: MaybeRefOrGetter<string>,
  allowed: MaybeRefOrGetter<boolean>,
): UseQueryReturnType<NetworkBlock[], Error> {
  return useQuery({
    queryKey: computed((): KeyParts => ['admin', 'trust-safety', 'blocks', toValue(justification)]),
    enabled: computed(() => toValue(allowed) && toValue(justification).trim() !== ''),
    queryFn: async (): Promise<NetworkBlock[]> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/admin/trust-safety/network-blocks',
        { params: { path: { version }, query: { justification: toValue(justification) } } },
      );

      if (error !== undefined)
        throw problemError('trust safety network blocks', response.status, error);

      return data?.data ?? [];
    },
  });
}

// The blast radius a block would touch. The bot recomputes it on apply, so the page confirms this exact count.
export function useBlockPreview(
  params: MaybeRefOrGetter<PreviewParams | null>,
): UseQueryReturnType<NetworkBlockPreview | null, Error> {
  return useQuery({
    queryKey: computed((): KeyParts => ['admin', 'trust-safety', 'preview', toValue(params)]),
    enabled: computed(() => toValue(params) !== null),
    staleTime: 0,
    queryFn: async (): Promise<NetworkBlockPreview | null> => {
      const target = toValue(params);

      if (target === null)
        return null;

      const { data, error, response } = await client.GET(
        '/api/v{version}/admin/trust-safety/network-blocks/preview',
        { params: { path: { version }, query: target } },
      );

      if (error !== undefined)
        throw problemError('trust safety block preview', response.status, error);

      return data?.data ?? null;
    },
  });
}

export function useReviewDecision(decision: 'confirm' | 'overturn') {
  const queries = useQueryClient();
  const path =
    decision === 'confirm'
      ? '/api/v{version}/admin/trust-safety/review-queue/{detectionId}/confirm'
      : '/api/v{version}/admin/trust-safety/review-queue/{detectionId}/overturn';

  return useMutation({
    mutationFn: async ({ detectionId, justification }: ReviewTarget): Promise<void> => {
      const { error, response } = await client.POST(path, {
        params: { path: { version, detectionId }, query: { justification } },
      });

      if (error !== undefined)
        throw problemError(`trust safety ${decision}`, response.status, error);
    },
    onSuccess: () => queries.invalidateQueries({ queryKey: trustSafetyPrefix }),
  });
}

export function useApplyNetworkBlock() {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async (body: ApplyBlockTarget): Promise<void> => {
      const { error, response } = await client.POST(
        '/api/v{version}/admin/trust-safety/network-blocks',
        { params: { path: { version } }, body },
      );

      if (error !== undefined)
        throw problemError('trust safety block apply', response.status, error);
    },
    onSuccess: () => queries.invalidateQueries({ queryKey: trustSafetyPrefix }),
  });
}

export function useLiftNetworkBlock() {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async ({ blockId, justification }: LiftTarget): Promise<void> => {
      const { error, response } = await client.POST(
        '/api/v{version}/admin/trust-safety/network-blocks/{blockId}/lift',
        { params: { path: { version, blockId }, query: { justification } } },
      );

      if (error !== undefined)
        throw problemError('trust safety block lift', response.status, error);
    },
    onSuccess: () => queries.invalidateQueries({ queryKey: trustSafetyPrefix }),
  });
}
