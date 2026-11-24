<!--
  -----------------------------------------------------------------------------
   Copyright (c) NoMercy Labs.

   This file is part of NomNomzBot. You may use, change and share it for free,
   for any noncommercial purpose, under the PolyForm Noncommercial License
   1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
   See LICENSE for details, and TRADEMARKS.md for the name and logo.

   SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
  -----------------------------------------------------------------------------
-->
<script setup lang="ts">
// A pick list definition's draft with the fields of the channel's pick list dialog: the name, the description and
// the entries, one per line. Save keeps the whole payload as a new version and is the one primary action; Edit JSON
// is the raw fallback.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import InlineNotice from '@/components/layout/InlineNotice.vue';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogContent,
  DialogFooter,
} from '@/components/ui/dialog';
import { Field, FieldInfo, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { Textarea } from '@/components/ui/textarea';
import { ensureWords } from '@/i18n';
import {
  maxDescriptionLength,
  maxItemLength,
  maxItems,
  maxNameLength,
  type PickListForm,
} from '@/views/dashboard/picklists/pickListForm';

import AdminPayloadDialogHeader from './AdminPayloadDialogHeader.vue';
import { pickListPayloadProblem, pickListWords, writePickListPayload } from './pickListPayload';

const props = defineProps<{
  title: string;
  payload: PickListForm;
  // Keeps the payload; resolves to the message shown after Save.
  keep: (payloadJson: string) => Promise<string>;
}>();

const emit = defineEmits<{
  close: [];
  // Hands the draft over as JSON for the raw editor.
  editJson: [payloadJson: string];
}>();

const { t } = useI18n();

const draft = ref<PickListForm>({ ...props.payload });

const wordsReady = ref(false);
const message = ref('');
const saving = ref(false);

void ensureWords(pickListWords).then(() => {
  wordsReady.value = true;
});

const problem = computed(() => pickListPayloadProblem(draft.value));
const problemText = computed(() => {
  const found = problem.value;

  if (found === null)
    return '';

  if (found === 'nameCharacters' || found === 'itemsRequired')
    return t(`dashboard.admin.content.pickListEditor.problem.${found}`);

  return t(`dashboard.picklists.dialog.problem.${found}`, {
    name: maxNameLength,
    description: maxDescriptionLength,
    items: maxItems,
    item: maxItemLength,
  });
});

function currentJson(): string {
  return writePickListPayload(draft.value);
}

async function save(): Promise<void> {
  saving.value = true;
  message.value = await props.keep(currentJson());
  saving.value = false;
}
</script>

<template>
  <Dialog :open="true" @update:open="emit('close')">
    <DialogContent
      data-picklist-payload-editor
      :show-close-button="false"
      class="flex max-h-[90vh] min-h-0 flex-col gap-0 overflow-hidden p-0 sm:max-w-2xl"
    >
      <AdminPayloadDialogHeader
        :title="title"
        :description="$t('dashboard.admin.content.pickListEditor.description')"
      />
      <div v-if="wordsReady" class="flex min-h-0 flex-1 flex-col gap-5 overflow-y-auto px-6 pb-4">
        <Field>
          <div class="flex items-center gap-1.5">
            <FieldLabel for="picklist-name">{{ $t("dashboard.picklists.dialog.name") }}</FieldLabel>
            <FieldInfo>{{ $t("dashboard.picklists.dialog.nameHelp") }}</FieldInfo>
          </div>
          <Input
            id="picklist-name"
            v-model="draft.name"
            autocomplete="off"
            :maxlength="maxNameLength"
          />
        </Field>

        <Field>
          <FieldLabel for="picklist-description">{{
            $t("dashboard.picklists.dialog.descriptionLabel")
          }}</FieldLabel>
          <Input
            id="picklist-description"
            v-model="draft.description"
            autocomplete="off"
            :maxlength="maxDescriptionLength"
          />
        </Field>

        <Field>
          <div class="flex items-center gap-1.5">
            <FieldLabel for="picklist-items">{{ $t("dashboard.picklists.dialog.items") }}</FieldLabel>
            <FieldInfo>{{ $t("dashboard.picklists.dialog.itemsHelp", { items: maxItems }) }}</FieldInfo>
          </div>
          <Textarea id="picklist-items" v-model="draft.items" rows="8" />
        </Field>

        <InlineNotice v-if="problemText" data-picklist-problem tone="error" compact>
          {{ problemText }}
        </InlineNotice>
      </div>
      <p
        v-if="message"
        data-picklist-message
        class="flex-none px-6 pb-4 text-sm text-muted-foreground"
        role="status"
      >
        {{ message }}
      </p>
      <DialogFooter class="flex-none border-t border-border px-6 py-4">
        <Button
          data-picklist-edit-json
          type="button"
          variant="outline"
          class="sm:mr-auto"
          @click="emit('editJson', currentJson())"
        >
          {{ $t("dashboard.admin.content.pickListEditor.editJson") }}
        </Button>
        <Button
          data-picklist-save
          type="button"
          :disabled="saving || problem !== null"
          @click="save"
        >
          {{ $t("dashboard.admin.content.draft.save") }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
