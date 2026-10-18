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
import Edit from '~icons/nnz/edit';
import { computed, ref } from 'vue';
import { toast } from 'vue-sonner';
import { useI18n } from 'vue-i18n';

import { useChannelAccess } from '@/api/access';
import type { NowPlaying } from '@/api/music';
import { usePlayerButtons, useRunAction, type PlayerButton } from '@/api/playerButtons';
import { Button } from '@/components/ui/button';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

import PlayerButtonsEditor from './PlayerButtonsEditor.vue';
import { playerIcon } from './playerIcons';

const props = defineProps<{
  track: NowPlaying;
}>();

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const access = useChannelAccess(channelId);
const canSee = computed(() => access.can('music:queue:read'));

const canEdit = computed(() =>
  canSee.value && access.can('music:config:write'));
const editing = ref(false);

const query = usePlayerButtons(() =>
  canSee.value
    ? channelId.value
    : null);
const run = useRunAction(channelId);

const buttons = computed(() => query.data.value ?? []);

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
  'song.name': props.track.trackName ?? '',
  'song.artist': props.track.artist ?? '',
  'song.requester': props.track.requestedBy ?? '',
  'target.id': props.track.requesterId ?? '',
  'target.displayname': props.track.requestedBy ?? '',
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
</script>

<template>
  <div
    v-if="canSee && (buttons.length > 0 || canEdit)"
    data-player-buttons
    class="flex flex-wrap items-center justify-center gap-2"
  >
    <Button
      v-for="button in buttons"
      :key="button.id"
      data-player-button
      variant="outline"
      size="sm"
      :disabled="run.isPending.value"
      @click="press(button)"
    >
      <component :is="playerIcon(button.icon)" class="size-5" />
      {{ button.label }}
    </Button>
    <Button
      v-if="canEdit"
      data-player-buttons-edit
      variant="ghost"
      size="sm"
      @click="editing = true"
    >
      <Edit class="size-5" aria-hidden="true" />
      {{ $t('dashboard.music.now.buttons.editor.edit') }}
    </Button>
    <PlayerButtonsEditor v-if="canEdit" v-model:open="editing" :buttons="buttons" />
  </div>
</template>
