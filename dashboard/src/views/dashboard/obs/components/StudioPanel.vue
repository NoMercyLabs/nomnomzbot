<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Studio mode and transitions. The transition picker always works. With studio mode on, a preview scene
// waits next to the program scene and the one primary button, Transition, cuts it to air.
import { computed, ref } from 'vue';
import { toastObsError } from '../obsToast';

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
import PanelCard from '@/components/layout/PanelCard.vue';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Switch } from '@/components/ui/switch';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

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
const duration = ref<string>('');

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
  setTransition.mutate(name, { onError: fail });
}

function runTransition(): void {
  const ms = Number.parseInt(duration.value, 10);

  cut.mutate(Number.isNaN(ms)
    ? null
    : ms, { onError: fail });
}
</script>

<template>
  <PanelCard :title="$t('dashboard.obs.studio.title')">
    <div class="grid gap-4 p-3">
      <label class="flex items-center justify-between gap-3 text-sm font-medium">
        {{ $t('dashboard.obs.studio.mode') }}
        <Switch
          data-obs-studio-toggle
          :model-value="studioOn"
          :aria-label="$t('dashboard.obs.studio.mode')"
          @update:model-value="toggleStudio"
        />
      </label>

      <div v-if="transitions.data.value?.length" class="grid gap-2">
        <span class="text-sm text-muted-foreground">{{ $t('dashboard.obs.studio.transition') }}</span>
        <div class="flex flex-wrap gap-2">
          <Button
            v-for="item in transitions.data.value"
            :key="item.name"
            variant="outline"
            :data-obs-transition-pick="item.name"
            :aria-pressed="item.name === activeTransition"
            :class="item.name === activeTransition && 'bg-accent text-accent-foreground'"
            @click="pickTransition(item.name)"
          >
            {{ item.name }}
          </Button>
        </div>
      </div>

      <template v-if="studioOn">
        <div class="grid gap-2">
          <span class="text-sm text-muted-foreground">{{ $t('dashboard.obs.studio.preview') }}</span>
          <div class="flex flex-wrap gap-2">
            <Button
              v-for="scene in scenes"
              :key="scene.name"
              variant="outline"
              :data-obs-preview="scene.name"
              :aria-pressed="scene.name === previewScene"
              :class="scene.name === previewScene && 'bg-accent text-accent-foreground'"
              @click="pickPreview(scene.name)"
            >
              {{ scene.name }}
            </Button>
          </div>
        </div>

        <div class="flex items-center gap-3">
          <Input
            v-model="duration"
            data-obs-duration
            type="number"
            min="0"
            class="w-28"
            :placeholder="$t('dashboard.obs.studio.duration')"
            :aria-label="$t('dashboard.obs.studio.duration')"
          />
          <Button data-obs-transition-button :disabled="cut.isPending.value" @click="runTransition">
            {{ $t('dashboard.obs.studio.cut') }}
          </Button>
        </div>
      </template>
    </div>
  </PanelCard>
</template>
