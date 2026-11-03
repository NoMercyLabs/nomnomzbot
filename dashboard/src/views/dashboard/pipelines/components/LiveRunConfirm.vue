<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The confirm before a live run: what the pipeline will really do, by kind of action. One primary action
// (Run now); Cancel is outline. The groups that touch real viewers or currency wear the destructive tokens.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import type { PipelineActionDescriptor, PipelineStep } from '@/api/pipelines';
import {
  AlertDialog,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog';
import { Button } from '@/components/ui/button';

import { livePlan } from './liveRunPlan';

const props = defineProps<{
  open: boolean;
  steps: readonly PipelineStep[];
  descriptors: ReadonlyMap<string, PipelineActionDescriptor>;
}>();

defineEmits<{ 'update:open': [open: boolean]; confirm: [] }>();

const { t, te } = useI18n();
const page = 'dashboard.pipelines.workbench.live';

const groups = computed(() =>
  livePlan(props.steps, props.descriptors, { t, te }));
</script>

<template>
  <AlertDialog :open="open" @update:open="$emit('update:open', $event)">
    <AlertDialogContent
      data-live-dialog
      class="flex max-h-[90vh] min-h-0 flex-col gap-0 overflow-hidden p-0 data-[size=default]:sm:max-w-115"
    >
      <div class="grid flex-none gap-1.5 px-6 pt-6 pb-4">
        <AlertDialogTitle class="text-lg font-semibold">{{ $t(`${page}.title`) }}</AlertDialogTitle>
        <AlertDialogDescription class="text-sm text-muted-foreground">
          {{ $t(`${page}.description`) }}
        </AlertDialogDescription>
      </div>
      <div class="flex min-h-0 flex-1 flex-col gap-3 overflow-y-auto px-6 pb-4">
        <section
          v-for="group in groups"
          :key="group.key"
          :data-live-group="group.key"
          class="grid gap-2 rounded-md border p-3"
          :class="group.real
            ? 'border-destructive/50 bg-destructive/5'
            : 'bg-muted/40'"
        >
          <h3 class="m-0 text-sm font-medium">{{ $t(`${page}.groups.${group.key}`) }}</h3>
          <p v-if="group.real" data-live-real class="m-0 text-sm text-destructive">
            {{ $t(`${page}.real.${group.key}`) }}
          </p>
          <ul class="m-0 grid list-none gap-1 p-0">
            <li v-for="(item, index) in group.steps" :key="item.id ?? index" data-live-step class="grid gap-0.5 text-sm">
              <span data-live-step-title>{{ item.title }}</span>
              <span v-if="item.hitsWhoever" data-live-flag class="text-xs text-muted-foreground">
                {{ $t(`${page}.hits`, { name: item.hitsWhoever }) }}
              </span>
            </li>
          </ul>
        </section>
        <p data-live-as class="m-0 text-sm text-muted-foreground">
          {{ $t(`${page}.as`) }}
        </p>
      </div>
      <AlertDialogFooter class="flex-none border-t border-border px-6 py-4">
        <AlertDialogCancel data-live-cancel>{{ $t('common.cancel') }}</AlertDialogCancel>
        <Button data-live-confirm @click="$emit('confirm')">
          {{ $t(`${page}.confirm`) }}
        </Button>
      </AlertDialogFooter>
    </AlertDialogContent>
  </AlertDialog>
</template>
