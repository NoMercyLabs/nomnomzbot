<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// A still window onto one real dashboard page. The page runs in a small app of its own (previewApp), filled
// by the fake data layer, and is drawn at desktop width and scaled down to fit. On a phone it is not scaled:
// the page shows its own phone layout at the phone's width. Nothing inside can be clicked or focused.
import { useElementSize } from '@vueuse/core';
import { computed, onBeforeUnmount, onMounted, ref, useTemplateRef, watch } from 'vue';

import { Skeleton } from '@/components/ui/skeleton';

import { startFakeApi } from './fakeApi';
import { createPreviewApp, type PreviewApp } from './previewApp';

const props = withDefaults(
  defineProps<{
    // The dashboard path the window shows: /app/pipelines.
    path: string;
    label: string;
    shell?: boolean;
    // The visible height of the window, in rows of the page at its drawn size.
    height?: number;
  }>(),
  {
    shell: false,
    height: 640,
  },
);

// The width a page is drawn at before it is scaled to the column.
const drawnWidth = 1280;
const scaleFrom = 640;

const box = useTemplateRef<HTMLElement>('box');
const host = useTemplateRef<HTMLElement>('host');
const { width } = useElementSize(box);
const scale = computed(() =>
  width.value >= scaleFrom
    ? width.value / drawnWidth
    : 1);
const innerWidth = computed(() =>
  width.value >= scaleFrom
    ? drawnWidth
    : width.value);
const outerHeight = computed(() => Math.round(props.height * scale.value));

const ready = ref(false);

let preview: PreviewApp | null = null;
let stopFake: (() => void) | null = null;

const isPhone = (): boolean => (box.value?.getBoundingClientRect().width ?? 0) < scaleFrom;

// A phone shows a picked step as a sheet over the whole screen, which would cover the home page itself, so on
// a phone the page opens without its picked step.
function pathToOpen(): string {
  return isPhone()
    ? props.path.replace(/[?&]step=[^&]*/, '')
    : props.path;
}

onMounted(() => {
  stopFake = startFakeApi();
  // A phone has no room for the sidebar next to the page, and the page has its own phone navigation.
  preview = createPreviewApp(pathToOpen(), props.shell && !isPhone());
  preview.app.mount(host.value as HTMLElement);

  // Ready once the first page has loaded; a failed first load still lifts the placeholder, never holds it.
  void preview.router.isReady().finally(() => {
    ready.value = true;
  });
});

watch(
  () => props.path,
  () => {
    void preview?.router.push(pathToOpen());
  },
);

onBeforeUnmount(() => {
  preview?.app.unmount();
  stopFake?.();
});
</script>

<template>
  <div
    ref="box"
    role="img"
    :aria-label="label"
    data-testid="preview-frame"
    :data-path="path"
    class="relative w-full overflow-hidden rounded-md border border-border bg-background"
    :style="{ height: `${outerHeight}px` }"
  >
    <div
      ref="host"
      inert
      class="pointer-events-none absolute left-0 top-0 origin-top-left select-none"
      :style="{
        width: `${innerWidth}px`,
        height: `${scale === 1 ? outerHeight : height}px`,
        transform: `scale(${scale})`,
      }"
    />
    <!-- The page is a chunk fetched on demand (slow on a dev server): an empty window would read as broken. -->
    <Skeleton v-if="!ready" data-preview-loading class="absolute inset-0 size-full rounded-none" />
  </div>
</template>
