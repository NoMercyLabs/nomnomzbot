<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The channel's master and TTS volume. Each row has a slider and a number box; both commit on release,
// blur or Enter, and one write carries both volumes. The default and the limits come from the server.
import { computed, reactive, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import { audioMixRefusals, useAudioMix, useSaveAudioMix, type AudioMixChange } from '@/api/audioMix';
import { ApiError } from '@/api/errors';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import { Input } from '@/components/ui/input';
import { Slider } from '@/components/ui/slider';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import type { PanelState } from '@/composables/usePanelState';
import { lookup } from '@/lib/array';
import { errorText } from '@/lib/errors';
import ReadabilityRow from '@/views/dashboard/settings/components/ReadabilityRow.vue';

type Field = keyof AudioMixChange;

// In the drawer the sheet frames the panel, so it drops its own border.
defineProps<{ flush?: boolean }>();

const fields = [
  { field: 'masterVolume', text: 'master' },
  { field: 'ttsVolume', text: 'tts' },
  { field: 'musicDuckPercent', text: 'musicDuck' },
] as const satisfies readonly { field: Field; text: string }[];

// Ducking is a share of the music volume, so it always runs 0 to 100 whatever limits the volumes have.
const duckLowest = 0;
const duckHighest = 100;

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const { can } = useCan();
const mix = useAudioMix(channelId);
const save = useSaveAudioMix(channelId);

const stored = computed(() => mix.data.value ?? null);
const writable = computed(() => can('sounds:write'));
// A server that predates ducking sends no percent, so the row stays hidden instead of showing a made-up number.
const ducks = computed(() => stored.value?.musicDuckPercent !== undefined);
const rows = computed(() => fields.filter((row) => row.field !== 'musicDuckPercent' || ducks.value));
const draft = reactive<Record<Field, number>>({
  masterVolume: 0,
  ttsVolume: 0,
  musicDuckPercent: 0,
});

const volumeLowest = computed(() => Number(stored.value?.minVolume ?? 0));
const volumeHighest = computed(() => Number(stored.value?.maxVolume ?? 100));

function lowestOf(field: Field): number {
  return field === 'musicDuckPercent'
    ? duckLowest
    : volumeLowest.value;
}

function highestOf(field: Field): number {
  return field === 'musicDuckPercent'
    ? duckHighest
    : volumeHighest.value;
}

const state = computed<PanelState>(() => {
  if (mix.isError.value)
    return 'error';

  return mix.isPending.value
    ? 'loading'
    : 'ready';
});

// The saved numbers are what the draft shows again after a refusal.
function showStored(): void {
  draft.masterVolume = Number(stored.value?.masterVolume ?? 0);
  draft.ttsVolume = Number(stored.value?.ttsVolume ?? 0);
  draft.musicDuckPercent = Number(stored.value?.musicDuckPercent ?? 0);
}

watch(stored, showStored, { immediate: true });

function defaultOf(field: Field): number {
  const standards = {
    masterVolume: stored.value?.defaultMasterVolume,
    ttsVolume: stored.value?.defaultTtsVolume,
    musicDuckPercent: stored.value?.defaultMusicDuckPercent,
  } as const satisfies Record<Field, number | string | undefined>;

  return Number(standards[field]);
}

function hintOf(field: Field): string {
  return stored.value
    ? t('dashboard.overlays.audioMix.defaultHint', { value: defaultOf(field) })
    : '';
}

function clamp(field: Field, raw: number): number {
  const whole = Math.trunc(raw);
  const floor = lowestOf(field);
  const ceiling = highestOf(field);

  return Number.isFinite(whole)
    ? Math.min(ceiling, Math.max(floor, whole))
    : floor;
}

function commit(field: Field): void {
  const current = stored.value;

  if (!current) return;

  draft[field] = clamp(field, draft[field]);

  if (draft[field] === Number(current[field])) return;

  const change = {
    masterVolume: Number(current.masterVolume),
    ttsVolume: Number(current.ttsVolume),
    musicDuckPercent: ducks.value
      ? Number(current.musicDuckPercent)
      : undefined,
    [field]: draft[field],
  } as const satisfies AudioMixChange;

  save.mutate(change, { onError: showStored });
}

function slide(field: Field, values: number[] | undefined): void {
  draft[field] = values?.[0] ?? draft[field];
}

// The failed save: the field a refusal names ('general' for any other failure) and the text to show.
const failure = computed(() => {
  const error = save.error.value;

  if (!error)
    return null;

  const detail = error instanceof ApiError
    ? error.detail
    : undefined;
  const field = lookup(audioMixRefusals, detail);

  return field === undefined
    ? { field: 'general', text: errorText(error, t) }
    : { field, text: t(`dashboard.overlays.audioMix.errors.${detail}`) };
});
</script>

<template>
  <PanelCard
    data-view="audio-mix"
    pad
    :flush="flush"
    :title="$t('dashboard.overlays.audioMix.title')"
    :description="$t('dashboard.overlays.audioMix.description')"
    :state="state"
    :error-text="$t('dashboard.overlays.audioMix.loadError')"
    @retry="mix.refetch()"
  >
    <RowGroup v-if="stored">
      <div v-for="row in rows" :key="row.field">
        <ReadabilityRow
          :title="$t(`dashboard.overlays.audioMix.${row.text}.title`)"
          :description="$t(`dashboard.overlays.audioMix.${row.text}.description`)"
          :hint="hintOf(row.field)"
        >
          <Slider
            class="w-40"
            :model-value="[draft[row.field]]"
            :min="lowestOf(row.field)"
            :max="highestOf(row.field)"
            :step="1"
            :disabled="!writable"
            :thumb-label="$t(`dashboard.overlays.audioMix.${row.text}.title`)"
            :aria-label="$t(`dashboard.overlays.audioMix.${row.text}.title`)"
            @update:model-value="slide(row.field, $event)"
            @value-commit="commit(row.field)"
          />
          <Input
            :model-value="draft[row.field]"
            type="number"
            :min="lowestOf(row.field)"
            :max="highestOf(row.field)"
            class="w-20"
            :data-audio-mix="row.field"
            :disabled="!writable"
            :aria-label="$t(`dashboard.overlays.audioMix.${row.text}.title`)"
            @update:model-value="draft[row.field] = Number($event)"
            @blur="commit(row.field)"
            @keydown.enter="commit(row.field)"
          />
        </ReadabilityRow>
        <p
          v-if="failure?.field === row.field"
          role="alert"
          class="px-3 pb-2 text-sm text-destructive"
          :data-audio-mix-error="row.field"
        >
          {{ failure?.text }}
        </p>
      </div>
      <p
        v-if="failure?.field === 'general'"
        role="alert"
        class="px-3 py-2 text-sm text-destructive"
        data-audio-mix-error="general"
      >
        {{ failure?.text }}
      </p>
    </RowGroup>
  </PanelCard>
</template>
