<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->

<script setup lang="ts">
// A new pick list, or an edit of a saved one: the name (it can change), a short description and the entries, one
// per line. An edit sends the whole list back; the server replaces the old entries.
import { ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import { useSavePickList, type PickList } from '@/api/pickLists';
import FormDialog from '@/components/layout/FormDialog.vue';
import { Field, FieldInfo, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { Textarea } from '@/components/ui/textarea';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { errorText, fieldErrorLines } from '@/lib/errors';

import {
  draftFromForm,
  formFromPickList,
  formProblem,
  maxDescriptionLength,
  maxItemLength,
  maxItems,
  maxNameLength,
  type PickListForm,
} from './pickListForm';

const open = defineModel<boolean>('open', { required: true });
const props = defineProps<{ list: PickList | null }>();

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const save = useSavePickList(channelId);

const form = ref<PickListForm>(formFromPickList(props.list));
const error = ref<string | undefined>(undefined);

watch(open, (isOpen: boolean): void => {
  if (!isOpen) return;

  form.value = formFromPickList(props.list);
  error.value = undefined;
});

async function submit(): Promise<void> {
  const problem = formProblem(form.value);

  error.value = problem === undefined
    ? undefined
    : t(`dashboard.picklists.dialog.problem.${problem}`, {
        name: maxNameLength,
        description: maxDescriptionLength,
        items: maxItems,
        item: maxItemLength,
      });

  if (error.value !== undefined) return;

  try {
    await save.mutateAsync({ existing: props.list, draft: draftFromForm(form.value) });
  } catch (failure) {
    error.value = [errorText(failure, t), ...fieldErrorLines(failure)].join(' ');

    return;
  }

  open.value = false;
}
</script>

<template>
  <FormDialog
    v-model:open="open"
    :title="
      list
        ? $t('dashboard.picklists.dialog.editTitle', { name: list.name })
        : $t('dashboard.picklists.dialog.newTitle')
    "
    :description="$t('dashboard.picklists.dialog.description')"
    :action="list ? $t('dashboard.picklists.dialog.save') : $t('dashboard.picklists.dialog.create')"
    :pending="save.isPending.value"
    :error="error"
    @submit="submit"
  >
    <Field>
      <div class="flex items-center gap-1.5">
        <FieldLabel for="picklist-name">{{ $t("dashboard.picklists.dialog.name") }}</FieldLabel>
        <FieldInfo>{{ $t("dashboard.picklists.dialog.nameHelp") }}</FieldInfo>
      </div>
      <Input id="picklist-name"
             v-model="form.name"
             data-picklist-name
             autocomplete="off"
             :maxlength="maxNameLength"
      />
    </Field>
    <Field>
      <FieldLabel for="picklist-description">{{ $t("dashboard.picklists.dialog.descriptionLabel") }}</FieldLabel>
      <Input id="picklist-description"
             v-model="form.description"
             data-picklist-description
             autocomplete="off"
             :maxlength="maxDescriptionLength"
      />
    </Field>
    <Field>
      <div class="flex items-center gap-1.5">
        <FieldLabel for="picklist-items">{{ $t("dashboard.picklists.dialog.items") }}</FieldLabel>
        <FieldInfo>{{ $t("dashboard.picklists.dialog.itemsHelp", { items: maxItems }) }}</FieldInfo>
      </div>
      <Textarea id="picklist-items" v-model="form.items" data-picklist-items rows="6" />
    </Field>
  </FormDialog>
</template>
