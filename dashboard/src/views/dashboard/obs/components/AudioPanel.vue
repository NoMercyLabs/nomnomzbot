<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The OBS audio mixer: one row per input that carries audio, a mute button and a level slider in dB.
// The rows follow OBS live. No button here is primary: the mixer is a set of equal controls.
// A slider drag shows at once; one write goes out when the drag pauses, and a refusal puts the old level back.
import { useDebounceFn } from '@vueuse/core';
import { computed, ref } from 'vue';
import { toastObsError } from '../obsToast';

import {
  useObsInputs,
  useObsSetInputMute,
  useObsSetInputVolume,
  type ObsInput,
} from '@/api/obs';
import PanelCard from '@/components/layout/PanelCard.vue';
import type { PanelState } from '@/composables/usePanelState';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Button } from '@/components/ui/button';
import { Slider } from '@/components/ui/slider';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';
import Muted from '~icons/nnz/volume-muted';
import Loud from '~icons/nnz/volume-two';

const props = defineProps<{ offline: boolean }>();

// OBS levels run from -100 dB (silent) up to +26 dB.
const minDb = -100;
const maxDb = 26;
const writeDelayMs = 150;

const { channelId } = useCurrentChannel();

const inputs = useObsInputs(channelId, () =>
  !props.offline);
const setMute = useObsSetInputMute(channelId);
const setVolume = useObsSetInputVolume(channelId);
const queryState = usePanelState(inputs);

const mixer = computed<ObsInput[]>(() =>
  (inputs.data.value ?? []).filter((input) => input.volumeDb != null));

const panel = computed<PanelState>(() => {
  if (props.offline)
    return 'empty';

  if (queryState.value !== 'ready')
    return queryState.value;

  return mixer.value.length === 0
    ? 'empty'
    : 'ready';
});

const emptyText = computed<string>(() =>
  props.offline
    ? 'dashboard.obs.audio.offline'
    : 'dashboard.obs.audio.empty');

// The level a drag shows before OBS has it, by input name.
const dragging = ref<Record<string, number>>({});
const pending = new Map<string, number>();

function level(input: ObsInput): number {
  return dragging.value[input.name] ?? Number(input.volumeDb);
}

function fail(error: Error): void {
  toastObsError(error);
}

function toggleMute(input: ObsInput): void {
  setMute.mutate({
    inputName: input.name,
    muted: input.muted !== true,
  }, { onError: fail });
}

function release(name: string): void {
  const { [name]: _gone, ...rest } = dragging.value;

  dragging.value = rest;
}

// The cache changes here, so the drag value is dropped once the write has settled (accepted or rolled back).
const flush = useDebounceFn((): void => {
  for (const [name, volumeDb] of pending) {
    setVolume.mutate({
      inputName: name,
      volumeDb,
    }, {
      onError: fail,
      onSettled: () => release(name),
    });
  }

  pending.clear();
}, writeDelayMs);

function moveSlider(input: ObsInput, value: number[] | undefined): void {
  const next = value?.[0];

  if (next === undefined) return;

  dragging.value = {
    ...dragging.value,
    [input.name]: next,
  };

  pending.set(input.name, next);
  void flush();
}

function decibels(value: number): string {
  return value.toFixed(1);
}
</script>

<template>
  <PanelCard
    :title="$t('dashboard.obs.audio.title')"
    :state="panel"
    :empty-text="$t(emptyText)"
    :error-text="$t('dashboard.obs.audio.loadError')"
    @retry="inputs.refetch()"
  >
    <div class="px-1 py-0.5">
      <div v-for="input in mixer" :key="input.name" :data-obs-input="input.name">
        <SummaryRow :title="input.name">
          <template #leading>
            <Button
              variant="ghost"
              size="icon"
              :data-obs-mute="input.name"
              :aria-pressed="input.muted === true"
              :aria-label="$t(input.muted === true
                ? 'dashboard.obs.audio.unmute'
                : 'dashboard.obs.audio.mute', { name: input.name })"
              @click="toggleMute(input)"
            >
              <Muted v-if="input.muted === true" class="size-5 text-destructive" />
              <Loud v-else class="size-5" />
            </Button>
          </template>
          <template #trailing>
            <Slider
              class="w-40"
              :model-value="[level(input)]"
              :min="minDb"
              :max="maxDb"
              :step="0.5"
              :disabled="input.muted === true"
              :aria-label="$t('dashboard.obs.audio.level', { name: input.name })"
              @update:model-value="moveSlider(input, $event)"
            />
            <span class="w-20 text-right text-sm tabular-nums text-muted-foreground">
              {{ $t('dashboard.obs.audio.decibels', { value: decibels(level(input)) }) }}
            </span>
          </template>
        </SummaryRow>
      </div>
    </div>
  </PanelCard>
</template>
