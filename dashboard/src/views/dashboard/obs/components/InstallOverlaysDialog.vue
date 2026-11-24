<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// One call puts the overlays into OBS. The person ticks the overlays and chooses, for each one, the scene it goes
// into and the name of its source. The scene at the top fills in every row at once. The bot makes no scene and
// adds no name prefix. An overlay not yet open in OBS starts ticked; one already loaded starts unticked.
// After the call the dialog lists each overlay with its result, and the primary button becomes Done.
import { computed, ref, watch } from 'vue';

import { useObsInstallOverlays, type ObsOverlayInstallResult, type ObsScene } from '@/api/obs';
import type { RequiredOverlay } from '@/api/widgets';
import FormDialog from '@/components/layout/FormDialog.vue';
import InlineNotice from '@/components/layout/InlineNotice.vue';
import OptionSelect from '@/components/layout/OptionSelect.vue';
import { Checkbox } from '@/components/ui/checkbox';
import { Field, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

import { toastObsError } from '../obsToast';

const props = defineProps<{
  scenes: readonly ObsScene[] | undefined;
  overlays: readonly RequiredOverlay[] | undefined;
}>();

const open = defineModel<boolean>('open', { required: true });

const { channelId } = useCurrentChannel();
const install = useObsInstallOverlays(channelId);

const scene = ref<string>('');
const ticked = ref<string[]>([]);
const sceneOf = ref<Record<string, string>>({});
const nameOf = ref<Record<string, string>>({});
const result = ref<ObsOverlayInstallResult | null>(null);

const choices = computed(() =>
  (props.scenes ?? []).map((item) => ({
    value: item.name,
    label: item.name,
  })));

// Each opening starts again: the on-air scene, the overlays not yet in OBS ticked, no result from an earlier run.
watch(
  open,
  (shown) => {
    if (!shown) return;

    scene.value = props.scenes?.find((item) => item.isCurrent)?.name ?? '';

    ticked.value = (props.overlays ?? [])
      .filter((overlay) => !overlay.widget.isAttached)
      .map((overlay) => overlay.key);

    const overlays = props.overlays ?? [];

    sceneOf.value = Object.fromEntries(overlays.map((overlay) => [overlay.key, scene.value]));

    nameOf.value = Object.fromEntries(
      overlays.map((overlay) => [overlay.key, overlay.widget.name]),
    );

    result.value = null;
  },
  { immediate: true },
);

// The scene at the top is a shortcut: picking it moves every overlay to that scene; each row can still differ.
function applySceneToAll(picked: string): void {
  scene.value = picked;
  sceneOf.value = Object.fromEntries(Object.keys(sceneOf.value).map((key) => [key, picked]));
}

function toggle(key: string, on: boolean | 'indeterminate'): void {
  ticked.value = on === true
    ? [...ticked.value, key]
    : ticked.value.filter((item) => item !== key);
}

const canSend = computed<boolean>(() =>
  ticked.value.length > 0 && ticked.value.every((key) => (sceneOf.value[key] ?? '') !== ''));

// The overlays go out in the order they are listed, not the order they were ticked.
const keysToSend = computed<string[]>(() =>
  (props.overlays ?? [])
    .map((overlay) => overlay.key)
    .filter((key) => ticked.value.includes(key)));

function submit(): void {
  if (result.value) {
    open.value = false;

    return;
  }

  const placements = keysToSend.value.map((key) => ({
    key,
    sceneName: sceneOf.value[key] ?? '',
    sourceName: (nameOf.value[key] ?? '').trim() || null,
  }));

  install.mutate({
    sceneName: placements[0]?.sceneName ?? '',
    overlayKeys: keysToSend.value,
    placements,
  }, {
    onSuccess: (done) => {
      result.value = done;
    },
    onError: toastObsError,
  });
}
</script>

<template>
  <FormDialog
    v-model:open="open"
    data-obs-install-dialog
    :title="$t('dashboard.obs.install.title')"
    :description="$t('dashboard.obs.install.description')"
    :action="result
      ? $t('dashboard.obs.install.done')
      : $t('dashboard.obs.install.submit')"
    :pending="install.isPending.value"
    :disabled="!result && !canSend"
    @submit="submit"
  >
    <template v-if="result">
      <ul class="grid gap-2" :aria-label="$t('dashboard.obs.install.results')">
        <li
          v-for="entry in result.overlays"
          :key="entry.key"
          :data-install-result="entry.key"
          class="grid gap-1 text-sm"
        >
          <span class="flex items-center justify-between gap-2">
            <span class="font-medium">{{ $t(`dashboard.obs.required.surfaces.${entry.key}.name`) }}</span>
            <span data-install-outcome class="text-muted-foreground">
              {{ $t(`dashboard.obs.install.outcome.${entry.outcome}`) }}
            </span>
          </span>
          <InlineNotice v-if="entry.error" tone="error" compact>{{ entry.error }}</InlineNotice>
        </li>
      </ul>
    </template>
    <template v-else>
      <Field>
        <FieldLabel>{{ $t('dashboard.obs.install.scene') }}</FieldLabel>
        <OptionSelect
          :model-value="scene"
          data-install-scene
          :options="choices"
          :placeholder="$t('dashboard.obs.install.scenePlaceholder')"
          @update:model-value="applySceneToAll"
        />
        <p class="text-sm text-muted-foreground">{{ $t('dashboard.obs.install.sceneHint') }}</p>
        <p v-if="choices.length === 0" class="text-sm text-muted-foreground">
          {{ $t('dashboard.obs.install.noScenes') }}
        </p>
      </Field>
      <fieldset class="grid gap-3">
        <legend class="mb-1 text-sm font-medium">{{ $t('dashboard.obs.install.overlays') }}</legend>
        <div
          v-for="overlay in overlays"
          :key="overlay.key"
          :data-install-row="overlay.key"
          class="grid gap-2"
        >
          <label class="flex items-center gap-2 text-sm">
            <Checkbox
              :data-install-overlay="overlay.key"
              :model-value="ticked.includes(overlay.key)"
              @update:model-value="toggle(overlay.key, $event)"
            />
            <span>{{ $t(`dashboard.obs.required.surfaces.${overlay.key}.name`) }}</span>
            <span v-if="overlay.widget.isAttached" class="text-muted-foreground">
              {{ $t('dashboard.obs.install.loaded') }}
            </span>
          </label>
          <div v-if="ticked.includes(overlay.key)" class="grid gap-2 pl-6 sm:grid-cols-2">
            <OptionSelect
              v-model="sceneOf[overlay.key]"
              :data-install-row-scene="overlay.key"
              :aria-label="$t('dashboard.obs.install.rowScene')"
              :options="choices"
              :placeholder="$t('dashboard.obs.install.scenePlaceholder')"
            />
            <Input
              v-model="nameOf[overlay.key]"
              :data-install-row-name="overlay.key"
              :aria-label="$t('dashboard.obs.install.rowName')"
              :placeholder="overlay.widget.name"
            />
          </div>
        </div>
      </fieldset>
    </template>
  </FormDialog>
</template>
