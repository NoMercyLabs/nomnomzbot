<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The one dialog for a small form (spec row Dialog): 5 fields or fewer. Six or more become an editor page.
// Same frame as ConfirmDialog: header and footer stay, only the body scrolls, at most 90% of the window.
// The owner puts the fields in the default slot and keeps their values; this dialog only submits.
// The optional header-action slot sits at the right of the title (for example an on/off switch).
// Pressing the primary button, or Enter in a field, emits submit once and leaves the dialog open:
// the owner closes it (v-model:open) on success, or sets error, so the typed values survive a failure.
// While pending the primary is disabled and Esc, Cancel and a click outside do not close it.
// The dialog renders in a portal, so it reads the lock from the scope that opened it: locked, the
// fields are disabled (own ReadOnlyScope) and the primary cannot submit.
import { nextTick, ref } from 'vue';

import ReadOnlyScope from '@/components/layout/ReadOnlyScope.vue';
import { useReadOnly } from '@/components/layout/readOnly';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogTitle,
} from '@/components/ui/dialog';
import { Spinner } from '@/components/ui/spinner';

// Dialog renders no element of its own, so hooks such as data-* from the owner land on the visible content.
defineOptions({ inheritAttrs: false });

const props = withDefaults(
  defineProps<{
    open: boolean;
    title: string;
    description?: string;
    action: string;
    pending?: boolean;
    // The owner holds the primary off while the form cannot be sent yet (a preview still loading).
    disabled?: boolean;
    error?: string;
  }>(),
  {
    description: undefined,
    pending: false,
    disabled: false,
    error: undefined,
  },
);

const emit = defineEmits<{ 'update:open': [open: boolean]; submit: [] }>();

const body = ref<HTMLElement | null>(null);
const locked = useReadOnly();

function setOpen(open: boolean): void {
  if (!open && props.pending) return;

  emit('update:open', open);
}

function submit(): void {
  if (props.pending || props.disabled || locked.value) return;

  emit('submit');
}

function submitFromField(event: KeyboardEvent): void {
  const target = event.target;

  if (!(target instanceof HTMLInputElement || target instanceof HTMLSelectElement)) return;

  event.preventDefault();
  submit();
}

function keepOpenWhilePending(event: Event): void {
  if (props.pending) {
    event.preventDefault();
  }
}

function focusFirstField(event: Event): void {
  event.preventDefault();

  void nextTick(() => {
    body.value
      ?.querySelector<HTMLElement>('input, select, textarea, [tabindex]:not([tabindex="-1"])')
      ?.focus();
  });
}
</script>

<template>
  <Dialog :open="open" @update:open="setOpen">
    <DialogContent
      v-bind="$attrs"
      :show-close-button="false"
      class="flex max-h-[90vh] min-h-0 flex-col gap-0 overflow-hidden p-0 sm:max-w-200"
      @open-auto-focus="focusFirstField"
      @escape-key-down="keepOpenWhilePending"
      @pointer-down-outside="keepOpenWhilePending"
      @interact-outside="keepOpenWhilePending"
    >
      <form
        class="flex min-h-0 flex-1 flex-col"
        novalidate
        @submit.prevent="submit"
        @keydown.enter="submitFromField"
      >
        <div data-form-header class="flex flex-none items-start justify-between gap-4 px-6 pt-6 pb-4">
          <div class="grid gap-1.5">
            <DialogTitle class="text-lg font-semibold">{{ title }}</DialogTitle>
            <DialogDescription v-if="description" class="text-sm text-muted-foreground">
              {{ description }}
            </DialogDescription>
          </div>
          <slot name="header-action" />
        </div>
        <div ref="body" class="min-h-0 flex-1 overflow-y-auto px-6 pb-4">
          <ReadOnlyScope :read-only="false" class="flex flex-col gap-4">
            <slot />
          </ReadOnlyScope>
        </div>
        <div
          v-if="error"
          data-form-error
          role="alert"
          class="flex-none px-6 pb-4 text-sm text-destructive"
        >
          {{ error }}
        </div>
        <DialogFooter class="flex-none border-t border-border px-6 py-4">
          <Button
            data-form-cancel
            type="button"
            variant="outline"
            :disabled="pending"
            @click="setOpen(false)"
          >
            {{ $t('common.cancel') }}
          </Button>
          <Button data-form-action type="submit" :disabled="pending || disabled || locked">
            <Spinner v-if="pending" />
            {{ action }}
          </Button>
        </DialogFooter>
      </form>
    </DialogContent>
  </Dialog>
</template>
