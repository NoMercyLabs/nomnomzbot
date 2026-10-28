<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
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
