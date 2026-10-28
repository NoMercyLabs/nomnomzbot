<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Edits one moderator note: its text and its pin. The change shows at once; a refused save rolls back
// and reaches the error inbox, like every save.
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import { useEditNote, type UserNote } from '@/api/people';
import FormDialog from '@/components/layout/FormDialog.vue';
import { Checkbox } from '@/components/ui/checkbox';
import { Field, FieldError, FieldLabel } from '@/components/ui/field';
import { Label } from '@/components/ui/label';
import { Textarea } from '@/components/ui/textarea';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

const props = defineProps<{ userId: string; note: UserNote | null }>();
const open = defineModel<boolean>('open', { required: true });

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const edit = useEditNote(channelId);

const text = ref('');
const pinned = ref(false);
const tried = ref(false);

const trimmed = computed(() => text.value.trim());
const textError = computed(() => {
  if (!tried.value || trimmed.value.length > 0)
    return null;

  return t('profile.notes.editNeeded');
});

watch(open, (isOpen: boolean) => {
  if (!isOpen || !props.note) return;

  text.value = props.note.content;
  pinned.value = props.note.pinned;
  tried.value = false;
});

function save(): void {
  tried.value = true;

  if (!props.note || trimmed.value.length === 0) return;

  edit.mutate({
    userId: props.userId,
    noteId: Number(props.note.id),
    content: trimmed.value,
    pinned: pinned.value,
  });

  open.value = false;
}
</script>

<template>
  <FormDialog
    v-model:open="open"
    data-profile-note-edit-dialog
    :title="$t('profile.notes.editTitle')"
    :action="$t('profile.notes.save')"
    @submit="save"
  >
    <Field :data-invalid="textError ? true : undefined">
      <FieldLabel for="profile-note-edit-text">{{ $t('profile.notes.editLabel') }}</FieldLabel>
      <Textarea
        id="profile-note-edit-text"
        v-model="text"
        data-profile-note-edit-text
        rows="3"
        :aria-invalid="textError ? true : undefined"
        @keydown.enter.ctrl.prevent="save"
      />
      <FieldError v-if="textError">{{ textError }}</FieldError>
    </Field>
    <Label class="gap-2 text-sm font-normal">
      <Checkbox v-model="pinned" data-profile-note-edit-pin />
      {{ $t('profile.notes.pin') }}
    </Label>
  </FormDialog>
</template>
