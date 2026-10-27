<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The Connect button on a YouTube request that waits for the Music Player page in OBS. It opens the same setup
// dialog as the OBS page's Music Player row. Someone who cannot read the overlays sees no button.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import { useChannelAccess } from '@/api/access';
import { useRequiredOverlays, type RequiredOverlay } from '@/api/widgets';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import ObsSetupAction from '@/views/dashboard/overlays/components/ObsSetupAction.vue';

const MUSIC_PLAYER_KEY = 'music_player';

withDefaults(defineProps<{ primary?: boolean }>(), { primary: false });

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const access = useChannelAccess(channelId);
const canReadOverlays = computed<boolean>(() => access.can('widget:read'));
const required = useRequiredOverlays(channelId, canReadOverlays);
const player = computed<RequiredOverlay | undefined>(() =>
  required.data.value?.find((overlay: RequiredOverlay) => overlay.key === MUSIC_PLAYER_KEY));
</script>

<template>
  <ObsSetupAction
    v-if="player?.widget.overlayUrl"
    data-music-player-connect
    :title="t('dashboard.music.queue.musicPlayer')"
    :setup="player.widget.obsSetup"
    :connected="player.widget.isAttached"
    :expected="player.widget.isEnabled"
    :primary="primary"
    :resolve-address="async () => player?.widget.overlayUrl"
  />
</template>
