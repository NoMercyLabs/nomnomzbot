<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Every timer of the channel with its state. Pause stops a timer from firing and keeps it saved;
// Resume starts it again. Pause is the outline action, Resume the quiet one: no row shouts.
import { useI18n } from 'vue-i18n'

import { useToggleTimer, type Timer } from '@/api/liveOps'
import RowGroup from '@/components/layout/RowGroup.vue'
import SummaryRow from '@/components/layout/SummaryRow.vue'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogTitle,
} from '@/components/ui/dialog'
import { useCurrentChannel } from '@/composables/useCurrentChannel'

defineProps<{ timers: Timer[] }>()

const open = defineModel<boolean>('open', { required: true })

const { t } = useI18n()
const { channelId } = useCurrentChannel()
const toggle = useToggleTimer(channelId)

function stateText(timer: Timer): string {
  return timer.isEnabled
    ? t('dashboard.home.quick.timerOn', { minutes: Number(timer.intervalMinutes) })
    : t('dashboard.home.quick.timerPaused')
}
</script>

<template>
  <Dialog v-model:open="open">
    <DialogContent
      :show-close-button="false"
      class="flex max-h-[90vh] min-h-0 flex-col gap-0 overflow-hidden p-0 data-[size=default]:sm:max-w-120"
    >
      <div class="grid flex-none gap-1.5 px-6 pt-6 pb-4">
        <DialogTitle class="text-lg font-semibold">{{
          t('dashboard.home.quick.timersTitle')
        }}</DialogTitle>
        <DialogDescription class="text-sm text-muted-foreground">
          {{ t('dashboard.home.quick.timersDialogIntro') }}
        </DialogDescription>
      </div>
      <div class="min-h-0 flex-1 overflow-y-auto px-3 pb-4">
        <RowGroup>
          <div v-for="timer in timers" :key="timer.id" :data-timer-row="timer.name">
            <SummaryRow :title="timer.name" :detail="stateText(timer)">
              <template #trailing>
                <Button
                  :variant="timer.isEnabled ? 'outline' : 'ghost'"
                  size="sm"
                  :disabled="toggle.isPending.value"
                  @click="toggle.mutate(timer.id)"
                >
                  {{
                    timer.isEnabled
                      ? t('dashboard.home.quick.timerPause')
                      : t('dashboard.home.quick.timerResume')
                  }}
                </Button>
              </template>
            </SummaryRow>
          </div>
        </RowGroup>
      </div>
      <DialogFooter class="flex-none border-t border-border px-6 py-4">
        <Button @click="open = false">{{ t('dashboard.home.quick.timersDone') }}</Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
