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

import type { UseQueryReturnType } from '@tanstack/vue-query';
import type { MaybeRefOrGetter } from 'vue';

import type { KeyParts } from '@/lib/array';

import { channelQuery, failed } from './channelQuery';
import { client } from './client';
import type { components } from './schema';

// The read side of a channel's limits: what the plan allows, how much of each limited resource is in use,
// and the metered usage of the current period. Changing the plan belongs to the Billing page.
export type ResourceUsage = components['schemas']['ResourceUsage'];
export type UsageMetric = components['schemas']['UsageMetric'];
export type Entitlement = components['schemas']['Entitlement'];

// The bot sends `ResourceClass` as its enum number: 0 is a cost-driving resource, 1 is a near-free one.
export const costDrivingClass = 0;

// No hub event announces a count change, so the numbers refresh on their own while the page is open.
const refreshMs = 30_000;

const version = '1';

export const resourceLimitsKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'billing',
  'limits',
];

export const usageMetricsKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'billing',
  'usage',
];

export const entitlementKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'billing',
  'entitlement',
];

async function readLimits(channelId: string): Promise<ResourceUsage[]> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/billing/limits',
    { params: { path: { version, channelId } } },
  );

  const found = data?.data;

  if (error !== undefined || found === undefined || found === null)
    throw failed('resource limits', response.status, error);

  return found;
}

async function readUsage(channelId: string): Promise<UsageMetric[]> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/billing/usage',
    { params: { path: { version, channelId } } },
  );

  const found = data?.data;

  if (error !== undefined || found === undefined || found === null)
    throw failed('plan usage', response.status, error);

  return found;
}

async function readEntitlement(channelId: string): Promise<Entitlement> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/billing/entitlement',
    { params: { path: { version, channelId } } },
  );

  const found = data?.data;

  if (error !== undefined || found === undefined || found === null)
    throw failed('plan entitlement', response.status, error);

  return found;
}

export function useResourceLimits(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<ResourceUsage[], Error> {
  return channelQuery(channelId, resourceLimitsKey, readLimits, refreshMs);
}

export function useUsageMetrics(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<UsageMetric[], Error> {
  return channelQuery(channelId, usageMetricsKey, readUsage, refreshMs);
}

export function useEntitlement(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<Entitlement, Error> {
  return channelQuery(channelId, entitlementKey, readEntitlement);
}
