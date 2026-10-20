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
// The buttons under the Now playing card. A press runs the button's action. Someone who may change them
// drags a button to move it and right-clicks it to edit it; the panel header's Add button adds one.
import { useSortable } from '@vueuse/integrations/useSortable';
import type { SortableEvent } from 'sortablejs';
import { computed, ref, useTemplateRef } from 'vue';
import { toast } from 'vue-sonner';
import { useI18n } from 'vue-i18n';

import { useChannelAccess } from '@/api/access';
import type { NowPlaying } from '@/api/music';
import {
  usePlayerButtons,
  useRunAction,
  useSavePlayerButtons,
  type PlayerButton,
} from '@/api/playerButtons';
import { Button } from '@/components/ui/button';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { moveItem } from '@/lib/array';

import PlayerButtonPicker from './PlayerButtonPicker.vue';
import { playerIcon } from './playerIcons';

// The track is null while nothing plays: the buttons still show, so Play works and they can be arranged.
const props = defineProps<{
  track: NowPlaying | null;
}>();

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const access = useChannelAccess(channelId);
const canSee = computed(() => access.can('music:queue:read'));

const canEdit = computed(() =>
  canSee.value && access.can('music:config:write'));
const editing = ref<number | null>(null);

const query = usePlayerButtons(() =>
  canSee.value
    ? channelId.value
    : null);
const run = useRunAction(channelId);
const save = useSavePlayerButtons(channelId);

const buttons = computed(() => query.data.value ?? []);

const pickerOpen = computed({
  get: () =>
    editing.value !== null,
  set: (isOpen: boolean) => {
    if (isOpen) return;

    editing.value = null;
  },
});

// The variables a button's action can read: the song on the player and the viewer who asked for it.
// Every key is a name the bot registers (MusicBuiltins song.*, TemplateHelperRegistry target.*). The
// track carries no login, so target.name is left out rather than guessed from the display name.
type ActionVariables = {
  'song.name': string;
  'song.artist': string;
  'song.requester': string;
  'target.id': string;
  'target.displayname': string;
};

const variables = computed(() => ({
  'song.name': props.track?.trackName ?? '',
  'song.artist': props.track?.artist ?? '',
  'song.requester': props.track?.requestedBy ?? '',
  'target.id': props.track?.requesterId ?? '',
  'target.displayname': props.track?.requestedBy ?? '',
}) as const satisfies ActionVariables);

function press(button: PlayerButton): void {
  run.mutate(
    {
      actionType: button.actionType,
      parameters: button.parameters,
      variables: variables.value,
    },
    { onError: () => toast.error(t('dashboard.music.now.buttons.failed', { label: button.label })) },
  );
}

// A right click opens the button's edit options instead of the browser menu.
function edit(event: MouseEvent, index: number): void {
  if (!canEdit.value) return;

  event.preventDefault();
  editing.value = index;
}

// Stoney: "be allowed to drag all the buttons around". Sortable moved the node itself; it goes back so Vue
// alone orders the buttons from the saved list, and the new order is saved in one PUT.
const strip = useTemplateRef<HTMLElement>('strip');

function dropped(event: SortableEvent): void {
  event.item.remove();
  event.from.insertBefore(event.item, event.from.children[event.oldIndex ?? 0] ?? null);

  const from = event.oldIndex ?? -1;
  const to = event.newIndex ?? -1;

  if (from < 0 || to < 0 || from === to) return;

  save.mutate(moveItem(buttons.value, from, to));
}

useSortable(strip, buttons, {
  // Only a button someone may move carries data-draggable, so a viewer's strip does not drag.
  draggable: '[data-draggable]',
  watchElement: true,
  animation: 150,
  onUpdate: dropped,
});
</script>

<template>
  <div
    v-if="canSee && buttons.length > 0"
    ref="strip"
    data-player-buttons
    class="flex flex-wrap items-center justify-center gap-2"
  >
    <Button
      v-for="(button, index) in buttons"
      :key="button.id"
      data-player-button
      :data-draggable="canEdit || undefined"
      variant="outline"
      size="sm"
      :class="canEdit && 'cursor-grab active:cursor-grabbing'"
      :title="canEdit ? $t('dashboard.music.now.buttons.editHint') : undefined"
      :disabled="run.isPending.value"
      @click="press(button)"
      @contextmenu="edit($event, index)"
    >
      <component :is="playerIcon(button.icon)" class="size-5" />
      {{ button.label }}
    </Button>
  </div>
  <PlayerButtonPicker
    v-if="canEdit"
    v-model:open="pickerOpen"
    :buttons="buttons"
    :index="editing"
  />
</template>
