<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Which moments the bot greets (first message ever, back after a break, watch streak), how long it waits
// between greetings, and the streak counts it celebrates. One save sends the whole config.
import Add from '~icons/nnz/add';
import Trash from '~icons/nnz/trash';
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import { useEngagementConfig, useSaveEngagementConfig } from '@/api/engagement';
import PanelCard from '@/components/layout/PanelCard.vue';
import type { PanelState } from '@/composables/usePanelState';
import RowGroup from '@/components/layout/RowGroup.vue';
import SettingRow from '@/components/layout/SettingRow.vue';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { errorText, fieldErrorLines } from '@/lib/errors';

import ReadabilityRow from './components/ReadabilityRow.vue';

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const { can } = useCan();
const config = useEngagementConfig(channelId);
const save = useSaveEngagementConfig(channelId);

const firstTime = ref(false);
const returning = ref(false);
const streak = ref(false);
const cooldown = ref(0);
const milestones = ref<number[]>([]);
const error = ref<string | undefined>(undefined);

watch(
  () => config.data.value,
  (row) => {
    firstTime.value = row?.firstTimeChatterEnabled ?? false;
    returning.value = row?.returningChatterEnabled ?? false;
    streak.value = row?.watchStreakEnabled ?? false;
    cooldown.value = row?.greetCooldownSeconds ?? 0;
    milestones.value = [...(row?.streakMilestones ?? [])];
  },
  { immediate: true },
);

const state = computed<PanelState>(() => {
  if (config.isError.value)
    return 'error';

  return config.isPending.value
    ? 'loading'
    : 'ready';
});

const isCountValid = (value: number): boolean => Number.isInteger(value) && value > 0;
const cooldownValid = computed(() => Number.isInteger(cooldown.value) && cooldown.value >= 0);
const milestonesValid = computed(() => milestones.value.every(isCountValid));

const changed = computed(() => {
  const row = config.data.value;

  if (row === undefined)
    return false;

  return firstTime.value !== row.firstTimeChatterEnabled ||
    returning.value !== row.returningChatterEnabled ||
    streak.value !== row.watchStreakEnabled ||
    cooldown.value !== row.greetCooldownSeconds ||
    milestones.value.join(',') !== row.streakMilestones.join(',');
});

const canSave = computed(
  () => changed.value && cooldownValid.value && milestonesValid.value && !save.isPending.value,
);

function setCooldown(value: string | number): void {
  cooldown.value = value === ''
    ? Number.NaN
    : Number(value);
}

function setMilestone(index: number, value: string | number): void {
  milestones.value[index] = value === ''
    ? Number.NaN
    : Number(value);
}

function addMilestone(): void {
  const last = milestones.value.at(-1) ?? 0;

  milestones.value.push(last + 5);
}

function removeMilestone(index: number): void {
  milestones.value.splice(index, 1);
}

async function submit(): Promise<void> {
  error.value = undefined;

  try {
    await save.mutateAsync({
      firstTimeChatterEnabled: firstTime.value,
      returningChatterEnabled: returning.value,
      watchStreakEnabled: streak.value,
      streakMilestones: milestones.value,
      greetCooldownSeconds: cooldown.value,
    });
  } catch (failure) {
    error.value = [errorText(failure, t), ...fieldErrorLines(failure)].join(' ');
  }
}
</script>

<template>
  <PanelCard
    data-view="engagement"
    pad
    :title="$t('dashboard.settings.engagement.title')"
    :description="$t('dashboard.settings.engagement.description')"
    :state="state"
    :error-text="$t('dashboard.settings.engagement.error')"
    @retry="config.refetch()"
  >
    <form id="engagement-form" class="grid gap-6" @submit.prevent="submit">
      <section class="grid gap-2" aria-labelledby="eg-moments">
        <div class="px-3">
          <h3 id="eg-moments" class="text-sm font-semibold">
            {{ $t('dashboard.settings.engagement.moments.title') }}
          </h3>
        </div>
        <RowGroup>
          <SettingRow
            v-model="firstTime"
            :disabled="!can('engagement:write')"
            :title="$t('dashboard.settings.engagement.firstTime.title')"
            :description="$t('dashboard.settings.engagement.firstTime.description')"
          />
          <SettingRow
            v-model="returning"
            :disabled="!can('engagement:write')"
            :title="$t('dashboard.settings.engagement.returning.title')"
            :description="$t('dashboard.settings.engagement.returning.description')"
          />
          <SettingRow
            v-model="streak"
            :disabled="!can('engagement:write')"
            :title="$t('dashboard.settings.engagement.streak.title')"
            :description="$t('dashboard.settings.engagement.streak.description')"
          />
        </RowGroup>
      </section>

      <section class="grid gap-2" aria-labelledby="eg-cooldown">
        <div class="px-3">
          <h3 id="eg-cooldown" class="text-sm font-semibold">
            {{ $t('dashboard.settings.engagement.cooldown.section') }}
          </h3>
        </div>
        <RowGroup>
          <ReadabilityRow
            :title="$t('dashboard.settings.engagement.cooldown.title')"
            :description="$t('dashboard.settings.engagement.cooldown.description')"
          >
            <Input
              class="w-24"
              type="number"
              min="0"
              step="1"
              data-cooldown
              :disabled="!can('engagement:write')"
              :model-value="cooldown"
              :aria-invalid="!cooldownValid"
              :aria-label="$t('dashboard.settings.engagement.cooldown.title')"
              @update:model-value="setCooldown"
            />
            <span class="text-sm text-muted-foreground">{{
              $t('dashboard.settings.engagement.cooldown.unit')
            }}</span>
          </ReadabilityRow>
        </RowGroup>
      </section>

      <section class="grid gap-2" aria-labelledby="eg-milestones">
        <div class="px-3">
          <h3 id="eg-milestones" class="text-sm font-semibold">
            {{ $t('dashboard.settings.engagement.milestones.title') }}
          </h3>
          <p class="text-sm text-muted-foreground">
            {{ $t('dashboard.settings.engagement.milestones.description') }}
          </p>
        </div>
        <RowGroup>
          <ReadabilityRow
            v-for="(milestone, index) in milestones"
            :key="index"
            :title="$t('dashboard.settings.engagement.milestones.item', { number: index + 1 })"
          >
            <Input
              class="w-24"
              type="number"
              min="1"
              step="1"
              data-milestone
              :disabled="!can('engagement:write')"
              :model-value="milestone"
              :aria-invalid="!isCountValid(milestone)"
              :aria-label="$t('dashboard.settings.engagement.milestones.item', { number: index + 1 })"
              @update:model-value="setMilestone(index, $event)"
            />
            <span class="text-sm text-muted-foreground">{{
              $t('dashboard.settings.engagement.milestones.unit')
            }}</span>
            <Button
              v-if="can('engagement:write')"
              type="button"
              variant="ghost"
              size="icon"
              data-remove-milestone
              :aria-label="$t('dashboard.settings.engagement.milestones.remove', { number: index + 1 })"
              @click="removeMilestone(index)"
            >
              <Trash class="size-5" />
            </Button>
          </ReadabilityRow>
          <p
            v-if="milestones.length === 0"
            class="px-3 py-2 text-sm text-muted-foreground"
          >
            {{ $t('dashboard.settings.engagement.milestones.empty') }}
          </p>
        </RowGroup>
        <div v-if="can('engagement:write')" class="px-3">
          <Button
            type="button"
            variant="outline"
            size="sm"
            data-add-milestone
            @click="addMilestone"
          >
            <Add class="size-4" />
            {{ $t('dashboard.settings.engagement.milestones.add') }}
          </Button>
        </div>
      </section>

      <p v-if="error" role="alert" class="px-3 text-sm text-destructive">{{ error }}</p>
    </form>
    <template v-if="can('engagement:write')" #footer>
      <span v-if="changed && !(cooldownValid && milestonesValid)" class="mr-auto text-sm text-destructive">{{
        $t('dashboard.settings.engagement.invalid')
      }}</span>
      <Button type="submit" form="engagement-form" size="sm" :disabled="!canSave">
        {{ $t('dashboard.settings.engagement.save') }}
      </Button>
    </template>
  </PanelCard>
</template>
