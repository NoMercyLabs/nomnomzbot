<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The New pipeline dialog: one name field. The bot makes an empty pipeline; the page then opens its workbench.
// The dialog stays open on a refusal and shows the bot's reason.
import { ref, watch } from 'vue';

import { useCreatePipeline } from '@/api/pipelines';
import FormDialog from '@/components/layout/FormDialog.vue';
import { Field, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

const open = defineModel<boolean>('open', { required: true });
const emit = defineEmits<{ created: [id: string] }>();

const { channelId } = useCurrentChannel();
const create = useCreatePipeline(channelId);
const name = ref('');
const error = ref<string | undefined>(undefined);

watch(open, (isOpen: boolean) => {
  if (!isOpen) return;

  name.value = '';
  error.value = undefined;
});

async function submit(): Promise<void> {
  const trimmed = name.value.trim();

  if (trimmed === '') return;

  error.value = undefined;

  try {
    const created = await create.mutateAsync({ name: trimmed });

    open.value = false;
    emit('created', created.id);
  } catch (failure) {
    error.value = (failure as Error).message;
  }
}
</script>

<template>
  <FormDialog
    v-model:open="open"
    data-pipelines-new-dialog
    :title="$t('dashboard.pipelines.newDialog.title')"
    :description="$t('dashboard.pipelines.newDialog.description')"
    :action="$t('dashboard.pipelines.newDialog.action')"
    :pending="create.isPending.value"
    :error="error"
    @submit="submit"
  >
    <Field>
      <FieldLabel for="pipeline-new-name">{{ $t('dashboard.pipelines.newDialog.name') }}</FieldLabel>
      <Input
        id="pipeline-new-name"
        v-model="name"
        data-pipelines-new-name
        autofocus
        :maxlength="100"
        :placeholder="$t('dashboard.pipelines.newDialog.namePlaceholder')"
      />
    </Field>
  </FormDialog>
</template>
