<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The foot of every editor, in one order: Delete (or Reset, for an item the bot ships) at the far left and quiet,
// then the extra actions (Duplicate), then the unsaved-changes text, then Discard and Save at the right.
// Save is the one primary action. Delete only asks: the page may load its consequences first, then opens the
// confirm through v-model:confirm-open; the red button of the ConfirmDialog is the only loud one.
// Without a deleteLabel there is no Delete; without a saveLabel there is no Discard or Save (an editor that
// saves each change at once).
import RotateLeft from '~icons/nnz/rotate-left';
import Trash2 from '~icons/nnz/trash';

import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import { Button } from '@/components/ui/button';

withDefaults(
  defineProps<{
    deleteLabel?: string;
    // A fixed-set item (an event response) cannot be deleted; it resets to its default here instead.
    resetLabel?: string;
    discardLabel?: string;
    saveLabel?: string;
    unsaved?: string;
    canDiscard?: boolean;
    canSave?: boolean;
    saving?: boolean;
    resetting?: boolean;
    confirmTitle?: string;
    confirmDescription?: string;
    confirmAction?: string;
    consequences?: readonly string[];
    deleting?: boolean;
  }>(),
  {
    deleteLabel: undefined,
    resetLabel: undefined,
    discardLabel: undefined,
    saveLabel: undefined,
    unsaved: '',
    canDiscard: true,
    canSave: true,
    saving: false,
    resetting: false,
    confirmTitle: '',
    confirmDescription: undefined,
    confirmAction: '',
    consequences: () => [],
    deleting: false,
  },
);

const confirmOpen = defineModel<boolean>('confirmOpen', { default: false });

defineEmits<{
  askDelete: [];
  confirmDelete: [];
  reset: [];
  discard: [];
  save: [];
}>();
</script>

<template>
  <div data-slot="editor-footer" class="flex w-full flex-wrap items-center gap-2">
    <!-- Quiet destructive: ghost with the destructive text color; its confirm carries the danger. -->
    <Button
      v-if="deleteLabel"
      data-editor-delete
      type="button"
      variant="ghost"
      size="sm"
      class="text-destructive hover:text-destructive"
      @click="$emit('askDelete')"
    >
      <Trash2 aria-hidden="true" />
      {{ deleteLabel }}
    </Button>
    <Button
      v-else-if="resetLabel"
      data-editor-reset
      type="button"
      variant="ghost"
      size="sm"
      :disabled="resetting"
      @click="$emit('reset')"
    >
      <RotateLeft aria-hidden="true" />
      {{ resetLabel }}
    </Button>
    <slot name="extra" />
    <span data-unsaved class="mr-auto text-sm text-muted-foreground">{{ unsaved }}</span>
    <template v-if="saveLabel">
      <Button
        v-if="discardLabel"
        data-editor-discard
        type="button"
        variant="outline"
        size="sm"
        :disabled="!canDiscard"
        @click="$emit('discard')"
      >
        {{ discardLabel }}
      </Button>
      <Button
        data-editor-save
        type="button"
        variant="default"
        size="sm"
        :disabled="!canSave || saving"
        @click="$emit('save')"
      >
        {{ saveLabel }}
      </Button>
    </template>
  </div>
  <ConfirmDialog
    v-if="deleteLabel"
    v-model:open="confirmOpen"
    :title="confirmTitle"
    :description="confirmDescription"
    :consequences="consequences"
    :action="confirmAction"
    :pending="deleting"
    @confirm="$emit('confirmDelete')"
  />
</template>
