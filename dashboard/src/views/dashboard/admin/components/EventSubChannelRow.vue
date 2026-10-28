<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// One channel: a verdict (working or how many need attention), the first thing that is broken and what to do,
// and the per-topic detail behind a toggle. A channel with no topic says so and is never shown as healthy.
import { computed } from 'vue';

import type {
  AdminEventSubTenantHealth,
  AdminEventSubTopicHealth,
} from '@/api/adminActivity';
import DateTime from '@/components/layout/DateTime.vue';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from '@/components/ui/collapsible';
import { humanize } from '@/lib/text';
import ChevronDown from '~icons/nnz/chevron-down';

const props = defineProps<{ tenant: AdminEventSubTenantHealth }>();

const isBroken = (topic: AdminEventSubTopicHealth): boolean =>
  !topic.enabled || Boolean(topic.lastError);

const topics = computed<AdminEventSubTopicHealth[]>(() =>
  [...props.tenant.topics].sort((a, b) =>
    Number(isBroken(b)) - Number(isBroken(a))));
const broken = computed(() => topics.value.filter(isBroken));
const firstBroken = computed(() => broken.value[0] ?? null);
const needsSignIn = computed(() =>
  broken.value.some((topic) => topic.status === 'authorization_revoked'));
</script>

<template>
  <Collapsible data-channel-row class="border-b border-border py-3 last:border-b-0">
    <div class="flex flex-wrap items-center gap-x-4 gap-y-1">
      <span class="w-36 shrink-0 truncate text-sm font-medium md:w-48">{{ tenant.channelDisplayName }}</span>
      <span class="shrink-0 md:w-56">
        <span v-if="topics.length === 0" class="text-sm text-muted-foreground">
          {{ $t('dashboard.admin.eventsubHealth.noSubscriptions') }}
        </span>
        <StatusBadge
          v-else-if="broken.length === 0"
          tone="ok"
          :label="$t('dashboard.admin.eventsubHealth.working')"
        />
        <StatusBadge
          v-else
          tone="bad"
          :label="$t('dashboard.admin.eventsubHealth.needAttention', { broken: broken.length, total: topics.length })"
        />
      </span>
      <span class="order-last min-w-0 basis-full text-sm text-muted-foreground truncate empty:hidden md:order-none md:flex-1 md:basis-0">
        <template v-if="firstBroken">
          {{ needsSignIn ? $t('dashboard.admin.eventsubHealth.signInAgain') : humanize(firstBroken.status) }}
        </template>
        <template v-else-if="topics.length > 0">
          {{ $t('dashboard.admin.eventsubHealth.subscriptions', { count: topics.length }, topics.length) }}
        </template>
      </span>
      <CollapsibleTrigger
        v-if="topics.length > 0"
        data-details-toggle
        class="group ml-auto flex min-h-9 min-w-9 shrink-0 items-center justify-center gap-1 rounded-md px-2 py-1 text-sm text-muted-foreground outline-none hover:text-foreground focus-visible:ring-3 focus-visible:ring-ring/50"
      >
        {{ $t('dashboard.admin.eventsubHealth.details') }}
        <ChevronDown
          class="size-4 transition-transform group-data-[state=open]:rotate-180 motion-reduce:transition-none"
          aria-hidden="true"
        />
      </CollapsibleTrigger>
    </div>
    <CollapsibleContent>
      <ul class="mt-3 divide-y divide-border rounded-md border border-border">
        <li class="flex items-center gap-4 px-3 py-2 text-xs text-muted-foreground max-md:hidden">
          <span class="min-w-0 flex-1">{{ $t('dashboard.admin.eventsubHealth.columns.topic') }}</span>
          <span class="w-32 shrink-0">{{ $t('dashboard.admin.eventsubHealth.columns.state') }}</span>
          <span class="w-28 shrink-0">{{ $t('dashboard.admin.eventsubHealth.columns.confirmed') }}</span>
          <span class="w-56 shrink-0">{{ $t('dashboard.admin.eventsubHealth.columns.error') }}</span>
        </li>
        <li
          v-for="topic in topics"
          :key="topic.subscriptionId"
          data-topic-row
          class="flex items-center gap-4 px-3 py-2 text-sm max-md:flex-wrap max-md:gap-y-1"
        >
          <span class="min-w-0 flex-1 max-md:basis-full">{{ humanize(topic.eventType) }}</span>
          <span class="shrink-0 md:w-32">
            <StatusBadge :tone="isBroken(topic) ? 'bad' : 'ok'" :label="humanize(topic.status)" />
          </span>
          <span class="shrink-0 text-muted-foreground md:w-28">
            <DateTime :value="topic.lastConfirmedAt" variant="date" />
          </span>
          <span class="min-w-0 text-muted-foreground max-md:basis-full max-md:empty:hidden md:w-56 md:shrink-0">{{ topic.lastError }}</span>
        </li>
      </ul>
    </CollapsibleContent>
  </Collapsible>
</template>
