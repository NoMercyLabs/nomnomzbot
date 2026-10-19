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

import {
  useEngagementConfig,
  useEngagementDefaults,
  useSaveEngagementConfig,
} from '@/api/engagement';
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
const defaults = useEngagementDefaults(channelId);
const save = useSaveEngagementConfig(channelId);

const firstTime = ref(false);
const returning = ref(false);
const streak = ref(false);
const cooldown = ref(Number.NaN);
const milestones = ref<number[]>([]);
const error = ref<string | undefined>(undefined);

watch(
  () => config.data.value,
  (row) => {
    if (row === undefined) return;

    firstTime.value = row.firstTimeChatterEnabled;
    returning.value = row.returningChatterEnabled;
    streak.value = row.watchStreakEnabled;
    cooldown.value = row.greetCooldownSeconds;
    milestones.value = [...row.streakMilestones];
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

  return (
    firstTime.value !== row.firstTimeChatterEnabled ||
    returning.value !== row.returningChatterEnabled ||
    streak.value !== row.watchStreakEnabled ||
    cooldown.value !== row.greetCooldownSeconds ||
    milestones.value.join(',') !== row.streakMilestones.join(',')
  );
});

const canSave = computed(
  () => changed.value && cooldownValid.value && milestonesValid.value && !save.isPending.value,
);

const atDefaults = computed(() => {
  const row = defaults.data.value;

  if (row === undefined)
    return true;

  return (
    firstTime.value === row.firstTimeChatterEnabled &&
    returning.value === row.returningChatterEnabled &&
    streak.value === row.watchStreakEnabled &&
    cooldown.value === row.greetCooldownSeconds &&
    milestones.value.join(',') === row.streakMilestones.join(',')
  );
});

// Puts the defaults in the form. Nothing is sent until the person saves.
function resetToDefaults(): void {
  const row = defaults.data.value;

  if (row === undefined) return;

  firstTime.value = row.firstTimeChatterEnabled;
  returning.value = row.returningChatterEnabled;
  streak.value = row.watchStreakEnabled;
  cooldown.value = row.greetCooldownSeconds;
  milestones.value = [...row.streakMilestones];
}

// Every default sentence reads the server's value, so a changed default needs no dashboard change.
const defaultOnOff = (on: boolean | undefined): string => {
  if (on === undefined)
    return '';

  return t(
    on
      ? 'dashboard.settings.engagement.defaultOn'
      : 'dashboard.settings.engagement.defaultOff',
  );
};

const momentHelp = (key: string, on: boolean | undefined): string =>
  `${t(`dashboard.settings.engagement.${key}.description`)} ${defaultOnOff(on)}`.trim();

const cooldownHelp = computed(() => {
  const row = defaults.data.value;
  const base = t('dashboard.settings.engagement.cooldown.description');

  if (row === undefined)
    return base;

  const seconds = row.greetCooldownSeconds;

  return `${base} ${t('dashboard.settings.engagement.cooldown.default', { seconds })}`;
});

const milestonesHelp = computed(() => {
  const row = defaults.data.value;
  const base = t('dashboard.settings.engagement.milestones.description');

  if (row === undefined)
    return base;

  const counts = row.streakMilestones.join(', ');
  const fallback =
    row.streakMilestones.length === 0
      ? t('dashboard.settings.engagement.milestones.defaultNone')
      : t('dashboard.settings.engagement.milestones.defaultList', { counts });

  return `${base} ${fallback}`;
});

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
    class="shrink-0"
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
            {{ $t("dashboard.settings.engagement.moments.title") }}
          </h3>
        </div>
        <RowGroup>
          <SettingRow
            v-model="firstTime"
            :disabled="!can('engagement:write')"
            :title="$t('dashboard.settings.engagement.firstTime.title')"
            :description="momentHelp('firstTime', defaults.data.value?.firstTimeChatterEnabled)"
          />
          <SettingRow
            v-model="returning"
            :disabled="!can('engagement:write')"
            :title="$t('dashboard.settings.engagement.returning.title')"
            :description="momentHelp('returning', defaults.data.value?.returningChatterEnabled)"
          />
          <SettingRow
            v-model="streak"
            :disabled="!can('engagement:write')"
            :title="$t('dashboard.settings.engagement.streak.title')"
            :description="momentHelp('streak', defaults.data.value?.watchStreakEnabled)"
          />
        </RowGroup>
      </section>

      <section class="grid gap-2" aria-labelledby="eg-cooldown">
        <div class="px-3">
          <h3 id="eg-cooldown" class="text-sm font-semibold">
            {{ $t("dashboard.settings.engagement.cooldown.section") }}
          </h3>
        </div>
        <RowGroup>
          <ReadabilityRow
            :title="$t('dashboard.settings.engagement.cooldown.title')"
            :description="cooldownHelp"
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
              $t("dashboard.settings.engagement.cooldown.unit")
            }}</span>
          </ReadabilityRow>
        </RowGroup>
      </section>

      <section class="grid gap-2" aria-labelledby="eg-milestones">
        <div class="px-3">
          <h3 id="eg-milestones" class="text-sm font-semibold">
            {{ $t("dashboard.settings.engagement.milestones.title") }}
          </h3>
          <p class="text-sm text-muted-foreground">
            {{ milestonesHelp }}
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
              :aria-label="
                $t('dashboard.settings.engagement.milestones.item', { number: index + 1 })
              "
              @update:model-value="setMilestone(index, $event)"
            />
            <span class="text-sm text-muted-foreground">{{
              $t("dashboard.settings.engagement.milestones.unit")
            }}</span>
            <Button
              v-if="can('engagement:write')"
              type="button"
              variant="ghost"
              size="icon"
              data-remove-milestone
              :aria-label="
                $t('dashboard.settings.engagement.milestones.remove', { number: index + 1 })
              "
              @click="removeMilestone(index)"
            >
              <Trash class="size-5" />
            </Button>
          </ReadabilityRow>
          <p v-if="milestones.length === 0" class="px-3 py-2 text-sm text-muted-foreground">
            {{ $t("dashboard.settings.engagement.milestones.empty") }}
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
            {{ $t("dashboard.settings.engagement.milestones.add") }}
          </Button>
        </div>
      </section>

      <p v-if="error" role="alert" class="px-3 text-sm text-destructive">{{ error }}</p>
    </form>
    <template v-if="can('engagement:write')" #footer>
      <span
        v-if="changed && !(cooldownValid && milestonesValid)"
        class="mr-auto text-sm text-destructive"
      >{{ $t("dashboard.settings.engagement.invalid") }}</span>
      <Button
        type="button"
        variant="outline"
        size="sm"
        data-reset-defaults
        :disabled="atDefaults"
        @click="resetToDefaults"
      >
        {{ $t("dashboard.settings.engagement.reset") }}
      </Button>
      <Button type="submit" form="engagement-form" size="sm" :disabled="!canSave">
        {{ $t("dashboard.settings.engagement.save") }}
      </Button>
    </template>
  </PanelCard>
</template>
