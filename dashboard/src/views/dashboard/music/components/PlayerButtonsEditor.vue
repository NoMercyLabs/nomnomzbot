<!--
  -----------------------------------------------------------------------------
   Copyright (c) NoMercy Labs.

   This file is part of NomNomzBot, free software licensed under the GNU Affero
   General Public License v3.0 or later. You may redistribute and/or modify it
   under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

   SPDX-License-Identifier: AGPL-3.0-or-later
  -----------------------------------------------------------------------------
-->
<script setup lang="ts">
// The editor of the buttons under the Now playing card: add, change, reorder and remove them, then save the
// whole list in one go. A button that breaks the bot's rules shows why and the list stays open, unsent.
import Plus from '~icons/nnz/add';
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import { usePipelineCatalog } from '@/api/pipelines';
import {
  MAX_PLAYER_BUTTONS,
  useSavePlayerButtons,
  type PlayerButton,
} from '@/api/playerButtons';
import FormDialog from '@/components/layout/FormDialog.vue';
import { Button } from '@/components/ui/button';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { errorText } from '@/lib/errors';

import PlayerButtonRow from './PlayerButtonRow.vue';
import {
  blankButton,
  hasProblems,
  moved,
  toDraft,
  toSaved,
  type DraftButton,
} from './playerButtonDraft';

const props = defineProps<{ buttons: PlayerButton[] }>();

const open = defineModel<boolean>('open', { required: true });

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const catalog = usePipelineCatalog(channelId);
const save = useSavePlayerButtons(channelId);

const drafts = ref<DraftButton[]>([]);
const checked = ref(false);
const error = ref<string | undefined>(undefined);

const actions = computed(() =>
  catalog.data.value?.actions ?? []);
const full = computed(() =>
  drafts.value.length >= MAX_PLAYER_BUTTONS);

watch(open, (isOpen: boolean) => {
  if (!isOpen) return;

  drafts.value = toDraft(props.buttons);
  checked.value = false;
  error.value = undefined;
});

function add(): void {
  drafts.value = [...drafts.value, blankButton()];
}

function change(index: number, button: DraftButton): void {
  drafts.value = drafts.value.map((current, at) =>
    at === index
      ? button
      : current);
}

function remove(index: number): void {
  drafts.value = drafts.value.filter((_, at) =>
    at !== index);
}

async function submit(): Promise<void> {
  checked.value = true;
  error.value = undefined;

  if (hasProblems(drafts.value)) return;

  try {
    await save.mutateAsync(toSaved(drafts.value));
    open.value = false;
  } catch (failure) {
    error.value = errorText(failure, t);
  }
}
</script>

<template>
  <FormDialog
    v-model:open="open"
    data-player-buttons-editor
    :title="$t('dashboard.music.now.buttons.editor.title')"
    :description="$t('dashboard.music.now.buttons.editor.description')"
    :action="$t('dashboard.music.now.buttons.editor.save')"
    :pending="save.isPending.value"
    :error="error"
    @submit="submit"
  >
    <p v-if="drafts.length === 0" data-editor-empty class="m-0 text-sm text-muted-foreground">
      {{ $t('dashboard.music.now.buttons.editor.empty') }}
    </p>
    <ul v-else class="m-0 flex list-none flex-col gap-3 p-0">
      <PlayerButtonRow
        v-for="(button, index) in drafts"
        :key="button.key"
        :button="button"
        :index="index"
        :count="drafts.length"
        :actions="actions"
        :checked="checked"
        @change="change(index, $event)"
        @move="drafts = moved(drafts, index, $event)"
        @remove="remove(index)"
      />
    </ul>
    <div class="flex items-center gap-3">
      <Button type="button"
              variant="outline"
              data-button-add
              :disabled="full"
              @click="add"
      >
        <Plus class="size-5" aria-hidden="true" />
        {{ $t('dashboard.music.now.buttons.editor.add') }}
      </Button>
      <span v-if="full" data-editor-full class="text-sm text-muted-foreground">
        {{ $t('dashboard.music.now.buttons.editor.full', { max: MAX_PLAYER_BUTTONS }) }}
      </span>
    </div>
  </FormDialog>
</template>
