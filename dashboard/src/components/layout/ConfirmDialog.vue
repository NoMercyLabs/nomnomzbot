<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The one confirm for every destructive action (spec row ConfirmDialog + BlastList).
// Header and footer stay; only the body scrolls; the dialog is at most 90% of the window.
// Cancel is outline on the left; the red button names the action. Pressing it emits confirm and
// leaves the dialog open: the owner closes it (v-model:open) when the work is done, so pending can show.
import BlastList from '@/components/layout/BlastList.vue'
import {
  AlertDialog,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog'
import { Button } from '@/components/ui/button'
import { Spinner } from '@/components/ui/spinner'

withDefaults(
  defineProps<{
    open: boolean
    title: string
    description?: string
    consequences?: string[]
    action: string
    pending?: boolean
  }>(),
  { description: undefined, consequences: () => [], pending: false },
)

defineEmits<{ 'update:open': [open: boolean]; confirm: [] }>()
</script>

<template>
  <AlertDialog :open="open" @update:open="$emit('update:open', $event)">
    <AlertDialogContent
      class="flex max-h-[90vh] min-h-0 flex-col gap-0 overflow-hidden p-0 data-[size=default]:sm:max-w-115"
    >
      <div class="grid flex-none gap-1.5 px-6 pt-6 pb-4">
        <AlertDialogTitle class="text-lg font-semibold">{{ title }}</AlertDialogTitle>
        <AlertDialogDescription v-if="description" class="text-sm text-muted-foreground">
          {{ description }}
        </AlertDialogDescription>
      </div>
      <div v-if="consequences.length > 0" class="min-h-0 flex-1 overflow-y-auto px-6 pb-4">
        <BlastList :items="consequences" />
      </div>
      <AlertDialogFooter class="flex-none border-t border-border px-6 py-4">
        <AlertDialogCancel>{{ $t('common.cancel') }}</AlertDialogCancel>
        <Button
          data-confirm-action
          variant="destructive"
          :disabled="pending"
          @click="$emit('confirm')"
        >
          <Spinner v-if="pending" />
          {{ action }}
        </Button>
      </AlertDialogFooter>
    </AlertDialogContent>
  </AlertDialog>
</template>
