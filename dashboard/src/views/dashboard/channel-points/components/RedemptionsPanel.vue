<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The pending-rewards queue, laid out like Twitch's own reward queue: on the left a filter (All, then every
// reward with its pending count), on the right the unfulfilled requests of that filter. Complete marks a
// request done; reject refunds the viewer's points, so it asks first.
import { useLocalStorage } from '@vueuse/core';
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import Check from '~icons/nnz/check';
import CheckDouble from '~icons/nnz/check-double';
import Cross from '~icons/nnz/cross';
import Gem from '~icons/nnz/gem';
import Inbox from '~icons/nnz/inbox';

import { useChannelAccess } from '@/api/access';
import {
  useRedemptionDecision,
  useRedemptions,
  useRewards,
  type Redemption,
  type RedemptionAction,
  type Reward,
} from '@/api/rewards';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import UserName from '@/components/layout/UserName.vue';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';
import { agoText } from '@/lib/time';

const allRewards = 'all';

const { t, locale } = useI18n();
const { channelId } = useCurrentChannel();
const access = useChannelAccess(channelId);

const queue = useRedemptions(channelId);
const rewards = useRewards(channelId);
const decision = useRedemptionDecision(channelId);

const canManage = computed(() => access.can('reward:manage'));
const state = usePanelState(queue);

// The chosen filter is a view preference, so it is kept across reloads and page switches.
const chosen = useLocalStorage('nnz.channelPoints.redemptionFilter', allRewards);
const selected = ref<string[]>([]);
const rejecting = ref<string[]>([]);

const pending = computed<Redemption[]>(() =>
  (queue.data.value ?? []).filter((row: Redemption) =>
    row.status === 'unfulfilled'));

type Filter = {
  id: string;
  title: string;
  count: number;
  reward: Reward | null;
};

// A redemption carries Twitch's reward id and the reward list carries ours, so the two meet on the title,
// which Twitch keeps unique per channel.
function rowsOf(reward: Reward): Redemption[] {
  return pending.value.filter((row: Redemption) =>
    row.rewardTitle === reward.title);
}

const filters = computed<Filter[]>(() => {
  const perReward = (reward: Reward): Filter => ({
    id: reward.id,
    title: reward.title,
    count: rowsOf(reward).length,
    reward,
  });
  const everything: Filter = {
    id: allRewards,
    title: t('dashboard.channelPoints.redemptions.all'),
    count: pending.value.length,
    reward: null,
  };

  return [everything, ...(rewards.data.value ?? []).map(perReward)];
});

// A remembered reward that no longer exists falls back to All.
const filter = computed<string>(() =>
  filters.value.some((entry: Filter) =>
    entry.id === chosen.value)
    ? chosen.value
    : allRewards);

const visible = computed<Redemption[]>(() => {
  const reward = filters.value.find((entry: Filter) =>
    entry.id === filter.value)?.reward;

  return reward
    ? rowsOf(reward)
    : pending.value;
});

const chosenIds = computed<string[]>(() =>
  visible.value
    .map((row: Redemption) => row.redemptionId)
    .filter((id: string) =>
      selected.value.includes(id)));

// Like Twitch, the section's Reject all / Complete all stay in place: they act on the ticked rows, or on
// every row of the open section when nothing is ticked.
const bulkIds = computed<string[]>(() =>
  chosenIds.value.length > 0
    ? chosenIds.value
    : visible.value.map((row: Redemption) =>
        row.redemptionId));

const allChosen = computed<boolean | 'indeterminate'>(() => {
  if (chosenIds.value.length === 0)
    return false;

  return chosenIds.value.length === visible.value.length
    ? true
    : 'indeterminate';
});

function pick(id: string): void {
  chosen.value = id;
  selected.value = [];
}

function toggle(id: string, on: boolean | 'indeterminate'): void {
  const others = selected.value.filter((entry: string) =>
    entry !== id);

  selected.value = on === true
    ? [...others, id]
    : others;
}

function toggleAll(on: boolean | 'indeterminate'): void {
  selected.value = on === true
    ? visible.value.map((row: Redemption) =>
        row.redemptionId)
    : [];
}

function decide(ids: string[], action: RedemptionAction): void {
  decision.mutate(
    { ids, action },
    {
      onError: () => {
        void queue.refetch();
      },
    },
  );

  selected.value = selected.value.filter((id: string) =>
    !ids.includes(id));
}

function confirmReject(): void {
  decide(rejecting.value, 'refund');
  rejecting.value = [];
}

function when(row: Redemption): string {
  return agoText(row.redeemedAt, new Date(), locale.value);
}

function rejectOpen(open: boolean): void {
  if (!open) {
    rejecting.value = [];
  }
}
</script>

<template>
  <PanelCard
    tier="lead"
    :title="$t('dashboard.channelPoints.redemptions.title')"
    :state="state"
    :error-text="$t('dashboard.channelPoints.redemptions.error')"
    @retry="queue.refetch()"
  >
    <template #actions>
      <span class="text-sm text-muted-foreground tabular-nums">
        {{ $t('dashboard.channelPoints.redemptions.range', { count: visible.length }) }}
      </span>
    </template>

    <div
      class="grid h-full min-h-0 grid-cols-[minmax(10rem,14rem)_minmax(0,1fr)] grid-rows-[minmax(0,1fr)] gap-2"
    >
      <nav
        class="flex min-h-0 flex-col gap-0.5 overflow-y-auto"
        :aria-label="$t('dashboard.channelPoints.redemptions.filterLabel')"
      >
        <button
          v-for="entry in filters"
          :key="entry.id"
          type="button"
          :data-redemption-filter="entry.id"
          :aria-pressed="filter === entry.id"
          class="flex min-h-11 items-center gap-2 rounded-[12px] px-2 py-1 text-left text-sm outline-none
            hover:bg-accent focus-visible:ring-3 focus-visible:ring-ring/50
            aria-pressed:bg-accent aria-pressed:font-medium"
          @click="pick(entry.id)"
        >
          <span
            class="grid size-9 flex-none place-content-center overflow-hidden rounded-[6px] bg-muted text-muted-foreground"
            :style="entry.reward?.backgroundColor
              ? { backgroundColor: entry.reward.backgroundColor }
              : undefined"
          >
            <Inbox v-if="entry.reward === null" class="size-5" aria-hidden="true" />
            <img
              v-else-if="entry.reward.imageUrl"
              :src="entry.reward.imageUrl"
              alt=""
              class="size-full object-cover"
            >
            <Gem v-else class="size-5" aria-hidden="true" />
          </span>
          <span class="min-w-0 flex-1 truncate">{{ entry.title }}</span>
          <span data-redemption-count class="flex-none text-sm text-muted-foreground tabular-nums">
            {{ entry.count }}
          </span>
        </button>
      </nav>

      <div class="flex min-h-0 min-w-0 flex-col">
        <p
          v-if="visible.length === 0"
          class="flex min-h-0 flex-1 items-center justify-center p-6 text-center text-sm text-muted-foreground"
        >
          {{ $t(filter === 'all'
            ? 'dashboard.channelPoints.redemptions.empty'
            : 'dashboard.channelPoints.redemptions.emptyFilter') }}
        </p>

        <div v-if="canManage && visible.length > 0" class="flex min-h-9 flex-none items-center gap-3 px-3">
          <Checkbox
            data-redemption-select-all
            :model-value="allChosen"
            :aria-label="$t('dashboard.channelPoints.redemptions.selectAll')"
            @update:model-value="toggleAll"
          />
          <span class="text-sm text-muted-foreground">
            {{ $t('dashboard.channelPoints.redemptions.selectAll') }}
          </span>
        </div>

        <ul v-if="visible.length > 0" class="flex min-h-0 flex-1 flex-col overflow-y-auto">
          <li
            v-for="row in visible"
            :key="row.redemptionId"
            :data-redemption-item="row.redemptionId"
            class="flex min-h-14 items-center gap-3 rounded-[8px] px-3 py-2 hover:bg-accent/50"
          >
            <Checkbox
              v-if="canManage"
              data-redemption-select
              :model-value="selected.includes(row.redemptionId)"
              :aria-label="$t('dashboard.channelPoints.redemptions.selectRow', { title: row.rewardTitle, name: row.userDisplayName })"
              @update:model-value="toggle(row.redemptionId, $event)"
            />
            <div class="min-w-0 flex-1">
              <div class="flex items-baseline gap-2">
                <span class="min-w-0 truncate text-sm font-medium">{{ row.rewardTitle }}</span>
                <span class="flex-none text-sm text-muted-foreground">
                  {{ when(row) }}
                </span>
              </div>
              <div class="flex items-center gap-1.5 text-sm text-muted-foreground">
                <UserName
                  :user-id="row.userId"
                  :name="row.userDisplayName"
                  :color="row.userColor"
                />
                <span aria-hidden="true">{{ $t('dashboard.channelPoints.redemptions.separator') }}</span>
                <span>{{ $t('dashboard.channelPoints.redemptions.cost', { count: Number(row.cost) }) }}</span>
              </div>
              <p v-if="row.userInput" class="mt-1 text-sm break-words">
                {{ row.userInput }}
              </p>
            </div>
            <div v-if="canManage" class="flex flex-none items-center gap-1.5">
              <Tooltip>
                <TooltipTrigger as-child>
                  <Button
                    variant="ghost"
                    size="icon"
                    :aria-label="$t('dashboard.channelPoints.redemptions.rejectRow', { title: row.rewardTitle, name: row.userDisplayName })"
                    data-redemption-reject
                    @click="rejecting = [row.redemptionId]"
                  >
                    <Cross class="size-5" aria-hidden="true" />
                  </Button>
                </TooltipTrigger>
                <TooltipContent>{{ $t('dashboard.channelPoints.redemptions.reject') }}</TooltipContent>
              </Tooltip>
              <Tooltip>
                <TooltipTrigger as-child>
                  <Button
                    variant="ghost"
                    size="icon"
                    :aria-label="$t('dashboard.channelPoints.redemptions.completeRow', { title: row.rewardTitle, name: row.userDisplayName })"
                    data-redemption-complete
                    @click="decide([row.redemptionId], 'fulfill')"
                  >
                    <Check class="size-5" aria-hidden="true" />
                  </Button>
                </TooltipTrigger>
                <TooltipContent>{{ $t('dashboard.channelPoints.redemptions.complete') }}</TooltipContent>
              </Tooltip>
            </div>
          </li>
        </ul>

      </div>
    </div>

    <template v-if="canManage" #footer>
      <div class="ml-auto flex items-center gap-2">
        <Button
          variant="outline"
          size="sm"
          data-redemption-bulk-reject
          :disabled="bulkIds.length === 0"
          @click="rejecting = bulkIds"
        >
          <Cross aria-hidden="true" />
          {{ $t('dashboard.channelPoints.redemptions.rejectAll', { count: bulkIds.length }) }}
        </Button>
        <Button
          size="sm"
          data-redemption-bulk-complete
          :disabled="bulkIds.length === 0"
          @click="decide(bulkIds, 'fulfill')"
        >
          <CheckDouble aria-hidden="true" />
          {{ $t('dashboard.channelPoints.redemptions.completeAll', { count: bulkIds.length }) }}
        </Button>
      </div>
    </template>
  </PanelCard>

  <ConfirmDialog
    :open="rejecting.length > 0"
    :title="$t('dashboard.channelPoints.redemptions.rejectTitle', { count: rejecting.length })"
    :description="$t('dashboard.channelPoints.redemptions.rejectDescription')"
    :consequences="[$t('dashboard.channelPoints.redemptions.rejectRefund')]"
    :action="$t('dashboard.channelPoints.redemptions.rejectConfirm', { count: rejecting.length })"
    @update:open="rejectOpen"
    @confirm="confirmReject"
  />
</template>
