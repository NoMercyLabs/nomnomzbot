<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The one confirm for every destructive action (spec row ConfirmDialog + BlastList).
// Header and footer stay; only the body scrolls; the dialog is at most 90% of the window.
// Cancel is outline on the left; the red button names the action. Pressing it emits confirm and
// leaves the dialog open: the owner closes it (v-model:open) when the work is done, so pending can show.
// The default slot shows the thing the action hits (a song, a user), between the header and the consequences.
import { computed, ref, watch } from 'vue';

import BlastList from '@/components/layout/BlastList.vue';
import { useReadOnly } from '@/components/layout/readOnly';
import {
  AlertDialog,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Spinner } from '@/components/ui/spinner';

const props = withDefaults(
  defineProps<{
    open: boolean;
    title: string;
    description?: string;
    consequences?: readonly string[];
    action: string;
    pending?: boolean;
    // A confirm that must be armed by typing: the exact words to type, and the label of the field that takes them.
    typeToConfirm?: string;
    typeLabel?: string;
  }>(),
  {
    description: undefined,
    consequences: () => [],
    pending: false,
    typeToConfirm: undefined,
    typeLabel: undefined,
  },
);

defineEmits<{ 'update:open': [open: boolean]; confirm: [] }>();

// The dialog renders in a portal, so it reads the lock of the scope that opened it.
const locked = useReadOnly();

// The typed words are cleared whenever the dialog opens or closes, so an old entry never arms a new confirm.
const typed = ref('');
const armed = computed(
  () => props.typeToConfirm === undefined || typed.value === props.typeToConfirm,
);

watch(() => props.open, () => {
  typed.value = '';
});
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
      <div v-if="$slots.default" data-confirm-subject class="flex-none px-6 pb-4">
        <slot />
      </div>
      <div v-if="consequences.length > 0" class="min-h-0 flex-1 overflow-y-auto px-6 pb-4">
        <BlastList :items="consequences" />
      </div>
      <div v-if="typeToConfirm !== undefined" class="flex-none px-6 pb-4">
        <Input
          v-model="typed"
          data-confirm-typed
          :aria-label="typeLabel"
          :placeholder="typeToConfirm"
          autocomplete="off"
        />
      </div>
      <AlertDialogFooter class="flex-none border-t border-border px-6 py-4">
        <AlertDialogCancel>{{ $t('common.cancel') }}</AlertDialogCancel>
        <Button
          data-confirm-action
          variant="destructive"
          :disabled="pending || locked || !armed"
          @click="$emit('confirm')"
        >
          <Spinner v-if="pending" />
          {{ action }}
        </Button>
      </AlertDialogFooter>
    </AlertDialogContent>
  </AlertDialog>
</template>
