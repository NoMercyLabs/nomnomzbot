<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->

<script setup lang="ts">
// A new code pool: a name, an optional note, and the codes to load now (one per line). The server keeps
// the codes secret; the page only ever shows counts.
import { ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import { useCreateCodePool } from '@/api/giveaways';
import FormDialog from '@/components/layout/FormDialog.vue';
import { Field, FieldInfo, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { Textarea } from '@/components/ui/textarea';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { errorText } from '@/lib/errors';

import { codesFromText } from './giveawayForm';

const open = defineModel<boolean>('open', { required: true });

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const create = useCreateCodePool(channelId);

const name = ref('');
const description = ref('');
const codes = ref('');
const error = ref<string | undefined>(undefined);

watch(open, (isOpen: boolean): void => {
  if (!isOpen) return;

  name.value = '';
  description.value = '';
  codes.value = '';
  error.value = undefined;
});

async function submit(): Promise<void> {
  if (name.value.trim() === '') {
    error.value = t('dashboard.giveaways.pools.dialog.problem.nameRequired');

    return;
  }

  try {
    await create.mutateAsync({
      name: name.value.trim(),
      description: description.value.trim(),
      codes: codesFromText(codes.value),
    });
  } catch (failure) {
    error.value = errorText(failure, t);

    return;
  }

  open.value = false;
}
</script>

<template>
  <FormDialog
    v-model:open="open"
    :title="$t('dashboard.giveaways.pools.dialog.title')"
    :description="$t('dashboard.giveaways.pools.dialog.description')"
    :action="$t('dashboard.giveaways.pools.dialog.create')"
    :pending="create.isPending.value"
    :error="error"
    @submit="submit"
  >
    <Field>
      <FieldLabel for="pool-name">{{ $t("dashboard.giveaways.pools.dialog.name") }}</FieldLabel>
      <Input id="pool-name" v-model="name" data-pool-name autocomplete="off" />
    </Field>
    <Field>
      <FieldLabel for="pool-description">{{
        $t("dashboard.giveaways.pools.dialog.note")
      }}</FieldLabel>
      <Input id="pool-description" v-model="description" data-pool-description autocomplete="off" />
    </Field>
    <Field>
      <div class="flex items-center gap-1.5">
        <FieldLabel for="pool-codes">{{ $t("dashboard.giveaways.pools.dialog.codes") }}</FieldLabel>
        <FieldInfo>{{ $t("dashboard.giveaways.pools.dialog.codesHelp") }}</FieldInfo>
      </div>
      <Textarea id="pool-codes" v-model="codes" data-pool-codes />
    </Field>
  </FormDialog>
</template>
