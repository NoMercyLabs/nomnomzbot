<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Twitch's own AutoMod levels, one row per category. Twitch keeps the levels, so the panel reads them live and
// Save sends all eight. Save is outline: the page's Save belongs to the bot's own filters above.
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { toast } from 'vue-sonner';

import {
  TWITCH_AUTOMOD_CATEGORIES,
  TWITCH_AUTOMOD_LEVELS,
  useSaveTwitchAutomod,
  useTwitchAutomod,
  type TwitchAutomodLevels,
} from '@/api/twitchAutomod';
import OptionSelect, { type Choice } from '@/components/layout/OptionSelect.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import RowText from '@/components/layout/RowText.vue';
import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';

const { t } = useI18n();
const { can } = useCan();
const { channelId } = useCurrentChannel();

const settings = useTwitchAutomod(channelId);
const save = useSaveTwitchAutomod(channelId);
const state = usePanelState(settings);

const canManage = computed<boolean>(() => can('moderation:automod:twitch:manage'));
const levels = ref<TwitchAutomodLevels | null>(null);

function readLevels(): TwitchAutomodLevels | null {
  const row = settings.data.value;

  if (!row)
    return null;

  return Object.fromEntries(
    TWITCH_AUTOMOD_CATEGORIES.map((category) => [category, Number(row[category])]),
  ) as TwitchAutomodLevels;
}

const stored = computed<TwitchAutomodLevels | null>(readLevels);

// A fresh read replaces the levels only when the person has not edited them.
watch(
  stored,
  (next, previous) => {
    if (next === null) return;

    const untouched = levels.value === null ||
      !previous ||
      JSON.stringify(levels.value) === JSON.stringify(previous);

    if (untouched) {
      levels.value = { ...next };
    }
  },
  { immediate: true },
);

const dirty = computed<boolean>(
  () => levels.value !== null && JSON.stringify(levels.value) !== JSON.stringify(stored.value),
);

const choices = computed<Choice[]>(() =>
  TWITCH_AUTOMOD_LEVELS.map((level: number) => ({
    value: String(level),
    label: t(`dashboard.settings.automod.twitch.levels.${level}`),
  })),
);

function setLevel(category: keyof TwitchAutomodLevels, value: string): void {
  if (levels.value) {
    levels.value[category] = Number(value);
  }
}

function submit(): void {
  if (levels.value === null) return;

  save.mutate(levels.value, {
    onSuccess: () => toast.success(t('dashboard.settings.automod.twitch.saved')),
    onError: (error: Error) =>
      toast.error(t('dashboard.settings.automod.twitch.saveError', { reason: error.message })),
  });
}
</script>

<template>
  <PanelCard
    tier="quiet"
    pad
    data-group="twitch"
    :title="$t('dashboard.settings.automod.twitch.title')"
    :description="$t('dashboard.settings.automod.twitch.description')"
    :state="state"
    :error-text="$t('dashboard.settings.automod.twitch.loadError')"
    @retry="settings.refetch()"
  >
    <RowGroup v-if="levels">
      <div
        v-for="category in TWITCH_AUTOMOD_CATEGORIES"
        :key="category"
        :data-category="category"
        class="flex min-h-11 items-center gap-3 rounded-[8px] px-3 py-2"
      >
        <RowText
          class="min-w-0 flex-1"
          :title="$t(`dashboard.settings.automod.twitch.categories.${category}`)"
        />
        <OptionSelect
          class="w-40"
          :model-value="String(levels[category])"
          :options="choices"
          :disabled="!canManage"
          :aria-label="$t(`dashboard.settings.automod.twitch.categories.${category}`)"
          @update:model-value="(value: string) => setLevel(category, value)"
        />
      </div>
    </RowGroup>
    <div v-if="canManage" class="pt-3">
      <Button
        variant="outline"
        size="sm"
        data-action="save-twitch"
        :disabled="!dirty || save.isPending.value"
        @click="submit"
      >
        {{ $t("dashboard.settings.automod.twitch.save") }}
      </Button>
    </div>
  </PanelCard>
</template>
