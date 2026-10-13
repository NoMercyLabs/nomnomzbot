<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The New script dialog: one name field. The bot gets the name, an empty description and the starter code.
// The dialog stays open on a refusal and shows the bot's reason; on success it closes and tells the page the new id.
import { ref, watch } from 'vue';

import { useCreateCodeScript } from '@/api/codeScripts';
import FormDialog from '@/components/layout/FormDialog.vue';
import { Input } from '@/components/ui/input';

import { STARTER_SOURCE } from './starterSource';

const props = defineProps<{ open: boolean }>();
const emit = defineEmits<{ 'update:open': [open: boolean]; created: [id: string | null] }>();

const create = useCreateCodeScript();
const name = ref('');
const error = ref<string | undefined>(undefined);

// A reopened dialog starts clean.
watch(
  () => props.open,
  (open: boolean): void => {
    if (!open) return;

    name.value = '';
    error.value = undefined;
  },
);

async function submit(): Promise<void> {
  const trimmed = name.value.trim();

  if (trimmed === '') return;

  error.value = undefined;

  try {
    const id: string | null = await create.mutateAsync({
      name: trimmed,
      description: '',
      sourceCode: STARTER_SOURCE,
    });

    emit('update:open', false);
    emit('created', id);
  } catch (failure) {
    error.value = (failure as Error).message;
  }
}
</script>

<template>
  <FormDialog
    :open="open"
    :title="$t('dashboard.scripts.new.title')"
    :description="$t('dashboard.scripts.new.description')"
    :action="$t('dashboard.scripts.new.action')"
    :pending="create.isPending.value"
    :error="error"
    @update:open="$emit('update:open', $event)"
    @submit="submit"
  >
    <label class="grid gap-1.5 text-sm font-medium">
      {{ $t('dashboard.scripts.new.name') }}
      <Input v-model="name" :placeholder="$t('dashboard.scripts.new.namePlaceholder')" />
    </label>
  </FormDialog>
</template>
