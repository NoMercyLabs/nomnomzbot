<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The notes the channel's moderators keep on a person, and a box to add one. Reading and writing are two
// separate rights; without the write right the box shows but is locked, so a note that cannot save is never typed.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import Edit from '~icons/nnz/edit';
import Trash2 from '~icons/nnz/trash';

import { useChannelAccess } from '@/api/access';
import { useAddNote, useDeleteNote, useUserNotes, type UserNote } from '@/api/people';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import ReadOnlyScope from '@/components/layout/ReadOnlyScope.vue';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { Label } from '@/components/ui/label';
import { Textarea } from '@/components/ui/textarea';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';
import { shortDate } from '@/lib/time';

import ProfileNoteEdit from './ProfileNoteEdit.vue';

const props = defineProps<{ userId: string }>();

const { t, locale } = useI18n();
const { channelId } = useCurrentChannel();
const access = useChannelAccess(channelId);

const canRead = computed(() => access.can('moderation:usercontext:read'));
const canWrite = computed(() => access.can('moderation:note:write'));

const notes = useUserNotes(channelId, () =>
  canRead.value
    ? props.userId
    : null);
const listState = usePanelState(notes, (list: UserNote[]) =>
  list.length === 0);
const state = computed(() =>
  canRead.value
    ? listState.value
    : 'empty');
const emptyText = computed(() =>
  canRead.value
    ? t('profile.notes.empty')
    : t('profile.notes.noAccess'));

const addNote = useAddNote(channelId);
const draft = ref('');
const pinned = ref(false);
const content = computed(() => draft.value.trim());

function byline(note: UserNote): string {
  const date = shortDate(note.createdAt, locale.value);
  const line = note.authorName
    ? t('profile.notes.by', { name: note.authorName, date })
    : date;

  return note.pinned
    ? t('profile.notes.pinned', { line })
    : line;
}

function add(): void {
  if (!content.value) return;

  addNote.mutate({
    userId: props.userId,
    content: content.value,
    pinned: pinned.value,
  });

  draft.value = '';
  pinned.value = false;
}

// The note a button acted on; Edit and Delete each open their own dialog for it.
const chosen = ref<UserNote | null>(null);
const editOpen = ref(false);
const deleteOpen = ref(false);
const deleteNote = useDeleteNote(channelId);

function editNote(note: UserNote): void {
  chosen.value = note;
  editOpen.value = true;
}

function askDelete(note: UserNote): void {
  chosen.value = note;
  deleteOpen.value = true;
}

function confirmDelete(): void {
  if (chosen.value) {
    deleteNote.mutate({ userId: props.userId, noteId: Number(chosen.value.id) });
  }

  deleteOpen.value = false;
}
</script>

<template>
  <PanelCard
    data-slot="profile-notes"
    bare
    :title="$t('profile.notes.title')"
    :state="state"
    :empty-text="emptyText"
    :error-text="$t('profile.notes.error')"
    @retry="notes.refetch()"
  >
    <ul class="grid gap-3 text-sm">
      <li
        v-for="note in notes.data.value"
        :key="note.id"
        data-profile-note
        class="flex items-start gap-2"
      >
        <div class="grid min-w-0 flex-1 gap-0.5">
          <span class="wrap-anywhere">{{ note.content }}</span>
          <span class="text-xs text-muted-foreground">
            {{ byline(note) }}
          </span>
        </div>
        <span
          class="flex shrink-0 items-center"
          :title="canWrite ? undefined : $t('profile.notes.noWrite')"
        >
          <Button
            data-profile-note-edit
            type="button"
            variant="ghost"
            size="icon"
            :disabled="!canWrite || Number(note.id) < 0"
            :aria-label="$t('profile.notes.edit')"
            :title="$t('profile.notes.edit')"
            @click="editNote(note)"
          >
            <Edit class="size-5" aria-hidden="true" />
          </Button>
          <Button
            data-profile-note-delete
            type="button"
            variant="ghost"
            size="icon"
            :disabled="!canWrite || Number(note.id) < 0"
            :aria-label="$t('profile.notes.delete')"
            :title="$t('profile.notes.delete')"
            @click="askDelete(note)"
          >
            <Trash2 class="size-5" aria-hidden="true" />
          </Button>
        </span>
      </li>
    </ul>
    <ProfileNoteEdit v-model:open="editOpen" :user-id="userId" :note="chosen" />
    <ConfirmDialog
      v-model:open="deleteOpen"
      :title="$t('profile.notes.deleteTitle')"
      :description="chosen?.content"
      :action="$t('profile.notes.delete')"
      @confirm="confirmDelete"
    />

    <template #footer>
      <ReadOnlyScope
        :read-only="!canWrite"
        :reason="$t('profile.notes.noWrite')"
        class="grid w-full gap-2"
      >
        <Textarea
          v-model="draft"
          data-profile-note-input
          rows="2"
          class="min-h-16"
          :aria-label="$t('profile.notes.input')"
          :placeholder="$t('profile.notes.input')"
          @keydown.enter.ctrl.prevent="add"
        />
        <div class="flex items-center justify-between gap-2">
          <Label class="gap-2 text-sm font-normal">
            <Checkbox v-model="pinned" data-profile-note-pin />
            {{ $t('profile.notes.pin') }}
          </Label>
          <Button
            data-profile-note-add
            type="button"
            size="sm"
            :disabled="!content || !canWrite"
            @click="add"
          >
            {{ $t('profile.notes.add') }}
          </Button>
        </div>
      </ReadOnlyScope>
    </template>
  </PanelCard>
</template>
