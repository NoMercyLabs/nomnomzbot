<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The channel's rewards: what each costs, whether it is on, and what it runs. A role without reward:manage reads only.
import { computed, ref } from 'vue';

import Gem from '~icons/nnz/gem';

import { useChannelAccess } from '@/api/access';
import { usePipelines, type PipelineListItem } from '@/api/pipelines';
import { useRewardAvailability, useRewards, useUpdateReward, type Reward } from '@/api/rewards';
import PanelCard from '@/components/layout/PanelCard.vue';
import ReadOnlyScope from '@/components/layout/ReadOnlyScope.vue';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Button } from '@/components/ui/button';
import { Switch } from '@/components/ui/switch';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';

import RewardEditDialog from './RewardEditDialog.vue';

const { channelId } = useCurrentChannel();
const access = useChannelAccess(channelId);

const query = useRewards(channelId);
const pipelines = usePipelines(channelId);
const update = useUpdateReward(channelId);

const availability = useRewardAvailability(channelId);
const unavailable = computed(() =>
  availability.data.value?.channelPointsAvailable === false);

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

const editing = ref<string | null>(null);
const editingReward = computed(() =>
  query.data.value?.find((reward: Reward) => reward.id === editing.value));
const editorOpen = computed({
  get: () => editing.value !== null,
  set: (value: boolean) => {
    if (!value) {
      editing.value = null;
    }
  },
});

function toggle(reward: Reward, enabled: boolean): void {
  update.mutate({ rewardId: reward.id, change: { isEnabled: enabled } });
}
</script>

<template>
  <PanelCard
    :title="$t('dashboard.channelPoints.rewards.title')"
    :state="state"
    :empty-text="$t('dashboard.channelPoints.rewards.empty')"
    :error-text="$t('dashboard.channelPoints.rewards.error')"
    @retry="query.refetch()"
  >
    <template #actions>
      <span class="text-sm text-muted-foreground tabular-nums">
        {{ $t('dashboard.channelPoints.rewards.count', { count: query.data.value?.length ?? 0 }) }}
      </span>
    </template>
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
            class="flex size-9 flex-none items-center justify-center overflow-hidden rounded-[6px] bg-muted text-muted-foreground"
            :style="
              reward.backgroundColor
                ? { backgroundColor: reward.backgroundColor }
                : undefined
            "
            aria-hidden="true"
          >
            <img
              v-if="reward.imageUrl"
              :src="reward.imageUrl"
              alt=""
              class="size-full object-cover"
            >
            <Gem v-else class="size-5" />
          </span>
        </template>
        <template #end>
          <StatusBadge
            v-if="!reward.isOnTwitch"
            tone="off"
            :label="$t('dashboard.channelPoints.rewards.notOnTwitch')"
            data-reward-not-on-twitch
          />
          <StatusBadge
            v-else-if="reward.isPaused"
            tone="off"
            :label="$t('dashboard.channelPoints.rewards.paused')"
          />
        </template>
        <template #trailing>
          <span class="text-sm text-muted-foreground tabular-nums">
            {{ $t('dashboard.channelPoints.rewards.costValue', { count: Number(reward.cost) }) }}
          </span>
          <Switch
            v-if="canManage"
            :model-value="reward.isEnabled"
            :disabled="!reward.isManageable || unavailable"
            :aria-label="$t('dashboard.channelPoints.rewards.enabled', { title: reward.title })"
            data-reward-enabled
            @update:model-value="(value) => toggle(reward, value === true)"
          />
          <Button
            v-if="canManage"
            variant="outline"
            size="sm"
            :aria-label="$t('dashboard.channelPoints.rewards.edit', { title: reward.title })"
            data-reward-edit
            @click="editing = reward.id"
          >
            {{ $t('dashboard.channelPoints.rewards.editButton') }}
          </Button>
        </template>
      </SummaryRow>
    </ReadOnlyScope>
    <RewardEditDialog
      v-if="editingReward"
      :key="editingReward.id"
      v-model:open="editorOpen"
      :reward="editingReward"
    />
  </PanelCard>
</template>
