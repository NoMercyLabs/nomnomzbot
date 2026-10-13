<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// What went wrong in one failed test run: the error, where in the code, the steps it took and what it did.
// Opening the editor at that line comes with the editor slice; here the place is plain text.
import { computed, type ComputedRef } from 'vue'

import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogTitle,
} from '@/components/ui/dialog'
import { Button } from '@/components/ui/button'

import type { BuildError } from '@/composables/useEditorBridge'

import type { SessionRun } from './sessionRun'

const props = defineProps<{ run: SessionRun | null }>()
const emit = defineEmits<{ close: [] }>()

const place: ComputedRef<BuildError | undefined> = computed(() => props.run?.result.errors?.[0])
</script>

<template>
  <Dialog :open="run !== null" @update:open="(open: boolean) => !open && emit('close')">
    <DialogContent
      v-if="run"
      :show-close-button="false"
      class="flex max-h-[90vh] min-h-0 flex-col gap-0 overflow-hidden p-0 data-[size=default]:sm:max-w-160"
    >
      <div class="grid flex-none gap-1.5 px-6 pt-6 pb-4">
        <DialogTitle class="text-lg font-semibold">{{
          $t('dashboard.scripts.failed.title')
        }}</DialogTitle>
        <DialogDescription class="text-sm text-muted-foreground">
          {{
            $t('dashboard.scripts.failed.when', { name: run.scriptName, ms: run.result.durationMs })
          }}
        </DialogDescription>
      </div>
      <div class="grid min-h-0 flex-1 gap-4 overflow-y-auto px-6 pb-4 text-sm">
        <div data-slot="failed-run-error" class="grid gap-1 rounded-[8px] bg-muted p-3">
          <b class="wrap-break-word">{{ run.result.error }}</b>
          <span v-if="place" class="font-mono text-xs text-muted-foreground">
            {{
              $t('dashboard.scripts.failed.place', {
                file: place.file ?? 'index.ts',
                line: place.line,
                column: place.column,
              })
            }}
          </span>
        </div>
        <section v-if="run.result.timeline.length > 0" class="grid gap-2">
          <h3 class="font-medium">{{ $t('dashboard.scripts.failed.steps') }}</h3>
          <ol class="grid gap-1">
            <li v-for="step in run.result.timeline" :key="step.seq" class="flex gap-2">
              <span class="w-6 flex-none text-muted-foreground tabular-nums">{{ step.seq }}</span>
              <span class="wrap-break-word">{{ step.text }}</span>
            </li>
          </ol>
        </section>
        <section v-if="run.result.effects.length > 0" class="grid gap-2">
          <h3 class="font-medium">{{ $t('dashboard.scripts.failed.effects') }}</h3>
          <ul class="grid gap-1">
            <li
              v-for="(effect, index) in run.result.effects"
              :key="index"
              class="font-mono text-xs"
            >
              {{ effect.name }} {{ effect.argsPreview }}
            </li>
          </ul>
        </section>
        <section v-if="run.result.console && run.result.console.length > 0" class="grid gap-2">
          <h3 class="font-medium">{{ $t('dashboard.scripts.failed.log') }}</h3>
          <pre class="overflow-x-auto rounded-[8px] bg-muted p-3 font-mono text-xs">{{
            run.result.console.join('\n')
          }}</pre>
        </section>
      </div>
      <DialogFooter class="flex-none border-t border-border px-6 py-4">
        <Button data-close variant="outline" @click="emit('close')">
          {{ $t('dashboard.scripts.failed.close') }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
