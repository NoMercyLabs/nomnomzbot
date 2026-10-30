<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The steps of one run in order. Each step shows what it was, whether it worked, how long it took against the
// whole run (the bar) and why it failed. Inside a step sit its host calls, so a slow call such as a 12 s
// `tts.speak` shows at once. The bot logs steps by their place in the run, so the number is that place.
// A run written before host calls were timed has none and shows its steps only.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';
import ChevronDown from '~icons/nnz/chevron-down';

import type { ChannelRunStep } from '@/api/adminDiagnostics';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from '@/components/ui/collapsible';
import { Item, ItemContent, ItemTitle } from '@/components/ui/item';
import { durationText } from '@/lib/time';

const props = defineProps<{ steps: readonly ChannelRunStep[]; runMs: number }>();

const { locale } = useI18n();

const ordered = computed<ChannelRunStep[]>(() => {
  const copy = Array.from(props.steps);

  return copy.sort((a, b) => Number(a.stepIndex) - Number(b.stepIndex));
});

// The longest of the run and its steps is the whole bar, so a step never draws past the end.
const scale = computed<number>(() =>
  Math.max(props.runMs, ...props.steps.map((step) => Number(step.durationMs)), 1));

function share(ms: number | string): string {
  return `${Math.min(100, (Number(ms) / scale.value) * 100)}%`;
}

function length(ms: number | string): string {
  return durationText(Number(ms), locale.value);
}
</script>

<template>
  <ol class="grid gap-2">
    <li v-for="step in ordered" :key="String(step.stepIndex)">
      <Item data-run-step variant="outline" size="sm" class="block">
        <ItemContent class="gap-2">
          <div class="flex items-center justify-between gap-3">
            <ItemTitle class="min-w-0 truncate">
              <span class="text-muted-foreground tabular-nums">{{ Number(step.stepIndex) + 1 }}</span>
              <span class="font-mono">{{ step.actionType }}</span>
            </ItemTitle>
            <span class="flex flex-none items-center gap-3">
              <span class="text-muted-foreground tabular-nums">{{ length(step.durationMs) }}</span>
              <StatusBadge
                :tone="step.succeeded
                  ? 'ok'
                  : 'bad'"
                :label="$t(step.succeeded
                  ? 'dashboard.admin.console.runs.detail.worked'
                  : 'dashboard.admin.console.runs.detail.failed')"
              />
            </span>
          </div>
          <div class="h-1.5 w-full rounded-full bg-muted" aria-hidden="true">
            <div
              data-step-bar
              class="h-full rounded-full"
              :class="step.succeeded
                ? 'bg-muted-foreground'
                : 'bg-destructive'"
              :style="{ width: share(step.durationMs) }"
            />
          </div>
          <p v-if="step.errorMessage" class="font-mono text-sm wrap-anywhere">{{ step.errorMessage }}</p>
          <Collapsible v-if="(step.hostCalls ?? []).length > 0" data-host-calls default-open>
            <CollapsibleTrigger
              class="group flex items-center gap-1 text-sm text-muted-foreground underline-offset-4 hover:underline"
            >
              {{ $t('dashboard.admin.console.runs.detail.hostCalls', { count: step.hostCalls.length }) }}
              <ChevronDown
                aria-hidden="true"
                class="size-4 flex-none transition-transform group-data-[state=open]:rotate-180"
              />
            </CollapsibleTrigger>
            <CollapsibleContent>
              <ul class="grid divide-y divide-border">
                <li
                  v-for="(call, index) in step.hostCalls"
                  :key="index"
                  data-host-call
                  class="grid gap-0.5 py-2 text-sm"
                >
                  <div class="flex items-center justify-between gap-3">
                    <span class="min-w-0 truncate font-mono">{{ call.name }}</span>
                    <span class="flex flex-none items-center gap-3">
                      <span class="text-muted-foreground tabular-nums">{{ length(call.durationMs) }}</span>
                      <StatusBadge
                        :tone="call.ok
                          ? 'ok'
                          : 'bad'"
                        :label="$t(call.ok
                          ? 'dashboard.admin.console.runs.detail.worked'
                          : 'dashboard.admin.console.runs.detail.failed')"
                      />
                    </span>
                  </div>
                  <p v-if="call.error" class="font-mono text-muted-foreground wrap-anywhere">{{ call.error }}</p>
                </li>
              </ul>
            </CollapsibleContent>
          </Collapsible>
        </ItemContent>
      </Item>
    </li>
  </ol>
</template>
