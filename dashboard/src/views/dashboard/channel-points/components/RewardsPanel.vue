<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The channel's rewards: what each costs, whether it is on, and what it runs. A role without reward:manage reads only.
import { computed } from 'vue';

import Gem from '~icons/nnz/gem';

import { useChannelAccess } from '@/api/access';
import { usePipelines, type PipelineListItem } from '@/api/pipelines';
import { useRewards, useUpdateReward, type Reward } from '@/api/rewards';
import PanelCard from '@/components/layout/PanelCard.vue';
import ReadOnlyScope from '@/components/layout/ReadOnlyScope.vue';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Input } from '@/components/ui/input';
import { Switch } from '@/components/ui/switch';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';

const { channelId } = useCurrentChannel();
const access = useChannelAccess(channelId);

const query = useRewards(channelId);
const pipelines = usePipelines(channelId);
const update = useUpdateReward(channelId);

const canManage = computed(() => access.can('reward:manage'));
const state = usePanelState(query, (rows: Reward[]): boolean =>
  rows.length === 0);

const pipelineNames = computed(() =>
  new Map((pipelines.data.value ?? []).map((item: PipelineListItem) =>
    [item.id, item.name] as const)));

// A reward runs its pipeline, or else answers in chat, or else only lands in the queue.
function runs(reward: Reward): string {
  const pipeline = reward.pipelineId
    ? pipelineNames.value.get(reward.pipelineId)
    : undefined;

  if (pipeline)
    return pipeline;

  return reward.response ?? '';
}

function saveCost(reward: Reward, raw: string | number): void {
  const cost = Number(raw);

  if (!Number.isInteger(cost) || cost < 1 || cost === Number(reward.cost)) return;

  update.mutate({ rewardId: reward.id, change: { cost } });
}

function toggle(reward: Reward, enabled: boolean): void {
  update.mutate({ rewardId: reward.id, change: { isEnabled: enabled } });
}
</script>

<template>
  <PanelCard
    tier="lead"
    :title="$t('dashboard.channelPoints.rewards.title')"
    :description="$t('dashboard.channelPoints.rewards.count', { count: query.data.value?.length ?? 0 })"
    :state="state"
    :empty-text="$t('dashboard.channelPoints.rewards.empty')"
    :error-text="$t('dashboard.channelPoints.rewards.error')"
    @retry="query.refetch()"
  >
    <ReadOnlyScope :read-only="!canManage">
      <SummaryRow
        v-for="reward in query.data.value"
        :key="reward.id"
        :title="reward.title"
        :detail="runs(reward) || $t('dashboard.channelPoints.rewards.runsNothing')"
        data-reward-item
      >
        <template #leading>
          <span
            class="flex size-9 flex-none items-center justify-center rounded-md bg-muted text-muted-foreground"
            :style="
              reward.backgroundColor
                ? { backgroundColor: reward.backgroundColor }
                : undefined
            "
            aria-hidden="true"
          >
            <Gem class="size-5" />
          </span>
        </template>
        <template #end>
          <StatusBadge
            v-if="reward.isPaused"
            tone="off"
            :label="$t('dashboard.channelPoints.rewards.paused')"
          />
        </template>
        <template #trailing>
          <Input
            type="number"
            min="1"
            inputmode="numeric"
            class="h-8 w-24 text-right tabular-nums"
            :model-value="Number(reward.cost)"
            :disabled="!reward.isManageable"
            :aria-label="$t('dashboard.channelPoints.rewards.cost', { title: reward.title })"
            data-reward-cost
            @change="saveCost(reward, ($event.target as HTMLInputElement).value)"
          />
          <Switch
            :model-value="reward.isEnabled"
            :disabled="!reward.isManageable"
            :aria-label="$t('dashboard.channelPoints.rewards.enabled', { title: reward.title })"
            data-reward-enabled
            @update:model-value="(value) => toggle(reward, value === true)"
          />
        </template>
      </SummaryRow>
    </ReadOnlyScope>
  </PanelCard>
</template>
