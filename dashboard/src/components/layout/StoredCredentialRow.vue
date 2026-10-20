<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// A stored credential (an own app, an API key) as one row, never an open input: the row says what is in use
// (title and detail), and Edit opens the form in a FormDialog. A secret field is always empty; its placeholder
// can say that one is stored. Save emits every field by name and leaves the dialog open: the owner closes it
// (v-model:open) on success or sets error, so the typed values survive a failure. Save stays off until every
// field holds text. The remove button (when removable) sits in the dialog and emits remove.
// The owner passes every word, so Admin > Providers and the integrations page share this one row.
import { computed, ref, watch } from 'vue';

import FormDialog from '@/components/layout/FormDialog.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';

export type CredentialField = {
  name: string;
  label: string;
  // A secret is typed as a password, starts empty and is never filled from a stored value.
  secret?: boolean;
  // The current value of a field that is not a secret.
  value?: string;
  placeholder?: string;
};

const props = withDefaults(
  defineProps<{
    title: string;
    detail?: string;
    editLabel: string;
    dialogTitle: string;
    dialogDescription?: string;
    saveLabel: string;
    removeLabel: string;
    fields: CredentialField[];
    removable?: boolean;
    pending?: boolean;
    removing?: boolean;
    error?: string;
    // The data-action of the remove button, so a page keeps its own hooks.
    removeAction?: string;
  }>(),
  {
    detail: undefined,
    dialogDescription: undefined,
    removable: false,
    pending: false,
    removing: false,
    error: undefined,
    removeAction: 'remove-credentials',
  },
);

const emit = defineEmits<{ submit: [values: Record<string, string>]; remove: [] }>();

const open = defineModel<boolean>('open', { default: false });
const draft = ref<Record<string, string>>({});

const complete = computed<boolean>(() =>
  props.fields.every((field) => (draft.value[field.name] ?? '').trim() !== ''));

function resetDraft(): void {
  draft.value = Object.fromEntries(
    props.fields.map((field) => [field.name, field.secret
      ? ''
      : (field.value ?? '')]),
  );
}

// Every time the dialog opens, the form starts from what is stored; the secret starts empty.
watch(open, (isOpen) => {
  if (isOpen) {
    resetDraft();
  }
}, { immediate: true });

function submit(): void {
  emit('submit', { ...draft.value });
}
</script>

<template>
  <RowGroup>
    <SummaryRow :title="title" :detail="detail" data-slot="stored-credential">
      <template #trailing>
        <Button variant="outline"
                size="sm"
                data-action="edit-credentials"
                type="button"
                @click="open = true"
        >
          {{ editLabel }}
        </Button>
      </template>
    </SummaryRow>
  </RowGroup>

  <FormDialog
    v-model:open="open"
    :title="dialogTitle"
    :description="dialogDescription"
    :action="saveLabel"
    :pending="pending"
    :disabled="!complete || removing"
    :error="error"
    @submit="submit"
  >
    <label v-for="field in fields" :key="field.name" class="grid gap-1.5 text-sm font-medium">
      {{ field.label }}
      <Input
        v-model="draft[field.name]"
        :name="field.name"
        :type="field.secret ? 'password' : 'text'"
        :autocomplete="field.secret ? 'new-password' : 'off'"
        :placeholder="field.placeholder"
      />
    </label>
    <div v-if="removable" class="flex justify-start">
      <Button
        type="button"
        variant="outline"
        size="sm"
        :data-action="removeAction"
        :disabled="removing || pending"
        @click="emit('remove')"
      >
        {{ removeLabel }}
      </Button>
    </div>
  </FormDialog>
</template>
