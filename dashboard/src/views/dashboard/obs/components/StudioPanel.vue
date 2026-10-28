<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Studio mode and transitions. The transition picker always works. With studio mode on, a preview scene
// waits next to the program scene and the one primary button, Transition, cuts it to air.
import { computed, ref } from 'vue';

import {
  useObsSetPreviewScene,
  useObsSetStudioMode,
  useObsSetTransition,
  useObsStudioMode,
  useObsStudioTransition,
  useObsTransitions,
  type ObsScene,
  type ObsTransition,
} from '@/api/obs';
import LabeledSwitch from '@/components/layout/LabeledSwitch.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

import { toastObsError } from '../obsToast';

defineProps<{ scenes: readonly ObsScene[] | undefined }>();

const { channelId } = useCurrentChannel();

const studio = useObsStudioMode(channelId, true);
const transitions = useObsTransitions(channelId, true);
const setStudio = useObsSetStudioMode(channelId);
const setPreview = useObsSetPreviewScene(channelId);
const setTransition = useObsSetTransition(channelId);
const cut = useObsStudioTransition(channelId);

const studioOn = computed<boolean>(() => studio.data.value === true);
const activeTransition = computed<string>(
  () => transitions.data.value?.find((item: ObsTransition) => item.isCurrent)?.name ?? '',
);

const previewScene = ref<string>('');

// The streamer types seconds; OBS takes milliseconds. An empty box keeps the transition's own length.
const seconds = ref<string>('');

function fail(error: Error): void {
  toastObsError(error);
}

function toggleStudio(enabled: boolean): void {
  setStudio.mutate(enabled, { onError: fail });
}

function pickPreview(scene: string): void {
  const before = previewScene.value;

  previewScene.value = scene;

  setPreview.mutate(scene, {
    onError: (error: Error) => {
      previewScene.value = before;
      fail(error);
    },
  });
}

function pickTransition(name: string): void {
  setTransition.mutate({ transitionName: name }, { onError: fail });
}

function runTransition(): void {
  const value = Number.parseFloat(seconds.value);

  cut.mutate(Number.isNaN(value)
    ? null
    : Math.round(value * 1000), { onError: fail });
}
</script>

<template>
  <PanelCard :title="$t('dashboard.obs.studio.title')">
    <template #actions>
      <LabeledSwitch
        data-obs-studio-toggle
        :model-value="studioOn"
        :label="$t('dashboard.obs.studio.on')"
        @update:model-value="toggleStudio"
      />
    </template>
    <div v-if="transitions.data.value?.length || studioOn" data-obs-studio-body class="grid gap-4 p-3">
      <div v-if="transitions.data.value?.length" class="grid gap-1.5">
        <Label for="obs-transition">{{ $t('dashboard.obs.studio.transition') }}</Label>
        <Select
          :model-value="activeTransition"
          @update:model-value="pickTransition(String($event))"
        >
          <SelectTrigger id="obs-transition" data-obs-picker="transition" class="w-full">
            <SelectValue />
          </SelectTrigger>
          <SelectContent position="popper">
            <SelectGroup>
              <SelectItem
                v-for="item in transitions.data.value"
                :key="item.name"
                :value="item.name"
              >
                {{ item.name }}
              </SelectItem>
            </SelectGroup>
          </SelectContent>
        </Select>
      </div>

      <template v-if="studioOn">
        <div class="grid gap-1.5">
          <Label for="obs-preview">{{ $t('dashboard.obs.studio.preview') }}</Label>
          <Select :model-value="previewScene" @update:model-value="pickPreview(String($event))">
            <SelectTrigger id="obs-preview" data-obs-picker="preview" class="w-full">
              <SelectValue :placeholder="$t('dashboard.obs.studio.pickPreview')" />
            </SelectTrigger>
            <SelectContent position="popper">
              <SelectGroup>
                <SelectItem v-for="scene in scenes" :key="scene.name" :value="scene.name">
                  {{ scene.name }}
                </SelectItem>
              </SelectGroup>
            </SelectContent>
          </Select>
        </div>

        <div class="flex items-end gap-3">
          <div class="grid gap-1.5">
            <Label for="obs-duration">{{ $t('dashboard.obs.studio.duration') }}</Label>
            <Input
              id="obs-duration"
              v-model="seconds"
              data-obs-duration
              type="number"
              min="0"
              step="0.1"
              class="w-28"
            />
          </div>
          <Button data-obs-transition-button :disabled="cut.isPending.value" @click="runTransition">
            {{ $t('dashboard.obs.studio.cut') }}
          </Button>
        </div>
      </template>
    </div>
  </PanelCard>
</template>
