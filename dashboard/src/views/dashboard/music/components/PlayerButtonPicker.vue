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
// Adds or edits one player button. Adding offers every key of the Stream Deck music plugin (Stoney: "pick
// from any of the streamdeck plugin buttons ... a picker with edit options too, same as the streamdeck
// plugin"); the picked key then shows its edit options. Every change saves the whole list in one PUT.
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import { usePipelineCatalog } from '@/api/pipelines';
import { useSavePlayerButtons, type PlayerButton } from '@/api/playerButtons';
import FormDialog from '@/components/layout/FormDialog.vue';
import { Button } from '@/components/ui/button';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { merge } from '@/lib/array';
import { errorText } from '@/lib/errors';
import { STREAM_DECK_MUSIC_ACTIONS, type StreamDeckAction } from '@/lib/streamDeckActions';

import PlayerButtonFields from './PlayerButtonFields.vue';
import {
  blankButton,
  hasProblems,
  toDraft,
  toSaved,
  type DraftButton,
} from './playerButtonDraft';
import { playerIcon } from './playerIcons';

const props = defineProps<{
  buttons: PlayerButton[];
  // The place of the button being edited; null adds a new one.
  index: number | null;
}>();

const open = defineModel<boolean>('open', { required: true });

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const catalog = usePipelineCatalog(channelId);
const save = useSavePlayerButtons(channelId);
const page = 'dashboard.music.now.buttons';

const draft = ref<DraftButton | null>(null);
const picked = ref<string | null>(null);
const checked = ref(false);
const error = ref<string | undefined>(undefined);

const adding = computed(() =>
  props.index === null);
const descriptor = computed(() =>
  catalog.data.value?.actions.find((action) =>
    action.type === draft.value?.actionType));

watch(open, (isOpen: boolean) => {
  if (!isOpen) return;

  const editing = props.index === null
    ? undefined
    : props.buttons[props.index];

  draft.value = editing === undefined
    ? null
    : (toDraft([editing])[0] ?? null);

  picked.value = null;
  checked.value = false;
  error.value = undefined;
}, { immediate: true });

function pick(action: StreamDeckAction): void {
  picked.value = action.key;

  draft.value = {
    ...blankButton(),
    label: t(`${page}.deck.${action.key}.name`),
    icon: action.icon,
    actionType: action.type,
    parameters: { ...action.defaults },
  };
}

function nextList(button: DraftButton): PlayerButton[] {
  const [saved] = toSaved([button]);

  if (saved === undefined)
    return props.buttons;

  if (props.index === null)
    return Array.from(merge(props.buttons, [saved]));

  return props.buttons.map((current, at) =>
    at === props.index
      ? saved
      : current);
}

async function send(buttons: PlayerButton[]): Promise<void> {
  error.value = undefined;

  try {
    await save.mutateAsync(buttons);
    open.value = false;
  } catch (failure) {
    error.value = errorText(failure, t);
  }
}

async function submit(): Promise<void> {
  checked.value = true;

  if (draft.value === null || hasProblems([draft.value])) return;

  await send(nextList(draft.value));
}

async function remove(): Promise<void> {
  await send(props.buttons.filter((_, at) =>
    at !== props.index));
}
</script>

<template>
  <FormDialog
    v-model:open="open"
    data-player-button-picker
    :title="$t(adding ? `${page}.picker.addTitle` : `${page}.picker.editTitle`)"
    :description="adding ? $t(`${page}.picker.description`) : undefined"
    :action="$t(adding ? `${page}.picker.add` : `${page}.picker.save`)"
    :pending="save.isPending.value"
    :disabled="draft === null"
    :error="error"
    @submit="submit"
  >
    <section v-if="adding" class="flex flex-col gap-2" :aria-label="$t(`${page}.picker.actions`)">
      <div class="grid grid-cols-2 gap-2 sm:grid-cols-3">
        <Button
          v-for="action in STREAM_DECK_MUSIC_ACTIONS"
          :key="action.key"
          type="button"
          :variant="picked === action.key ? 'secondary' : 'outline'"
          :data-deck-action="action.key"
          :aria-pressed="picked === action.key"
          :title="$t(`${page}.deck.${action.key}.help`)"
          class="justify-start"
          @click="pick(action)"
        >
          <component :is="playerIcon(action.icon)" class="size-5" aria-hidden="true" />
          <span class="truncate">{{ $t(`${page}.deck.${action.key}.name`) }}</span>
        </Button>
      </div>
      <p class="m-0 text-sm text-muted-foreground">
        {{ picked === null ? $t(`${page}.picker.choose`) : $t(`${page}.deck.${picked}.help`) }}
      </p>
    </section>
    <PlayerButtonFields
      v-if="draft !== null"
      :button="draft"
      :descriptor="descriptor"
      :checked="checked"
      @change="draft = $event"
    />
    <div v-if="!adding" class="flex">
      <Button
        type="button"
        variant="outline"
        class="text-destructive"
        data-button-remove
        :disabled="save.isPending.value"
        @click="remove"
      >
        {{ $t(`${page}.picker.remove`) }}
      </Button>
    </div>
  </FormDialog>
</template>
