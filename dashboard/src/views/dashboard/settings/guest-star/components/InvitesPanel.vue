<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The invites of the running session. Twitch gives an invite only the id of the person, so the row shows the id.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  useGuestStarInvites,
  useRemoveGuestStarInvite,
  type GuestStarInvite,
} from '@/api/guestStar';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';
import { useTime } from '@/composables/useTime';

import { useGuestStarFailure } from '../guestStarFailure';
import InviteDialog from './InviteDialog.vue';

const props = defineProps<{ sessionId: string }>();

const { t } = useI18n();
const { can } = useCan();
const { notify } = useGuestStarFailure();
const { channelId } = useCurrentChannel();
const time = useTime();

const invites = useGuestStarInvites(channelId, () => props.sessionId);
const remove = useRemoveGuestStarInvite(channelId);
const state = usePanelState(invites, (list: GuestStarInvite[]) =>
  list.length === 0);
const canWrite = computed(() => can('guest-star:write'));
const inviting = ref(false);

function removeInvite(invite: GuestStarInvite): void {
  remove.mutate(
    { sessionId: props.sessionId, guestTwitchUserId: invite.userId },
    { onError: notify },
  );
}

function detailOf(invite: GuestStarInvite): string {
  return `${t('dashboard.settings.guestStar.invites.status', { status: invite.status })} · ${
    t('dashboard.settings.guestStar.invites.invitedAt', { time: time.relative(invite.invitedAt) })}`;
}
</script>

<template>
  <PanelCard
    :title="$t('dashboard.settings.guestStar.invites.title')"
    :state="state"
    :empty-text="$t('dashboard.settings.guestStar.invites.empty')"
    :error-text="$t('dashboard.settings.guestStar.invites.loadError')"
    data-panel="invites"
    @retry="invites.refetch()"
  >
    <template v-if="canWrite" #actions>
      <Button size="sm" data-invite-open @click="inviting = true">
        {{ $t("dashboard.settings.guestStar.invites.invite") }}
      </Button>
    </template>
    <RowGroup>
      <SummaryRow
        v-for="invite in invites.data.value"
        :key="invite.userId"
        :data-invite="invite.userId"
        :title="invite.userId"
        :detail="detailOf(invite)"
      >
        <template v-if="canWrite" #trailing>
          <Button
            variant="ghost"
            size="sm"
            :data-invite-remove="invite.userId"
            :disabled="remove.isPending.value"
            @click="removeInvite(invite)"
          >
            {{ $t("dashboard.settings.guestStar.invites.remove") }}
          </Button>
        </template>
      </SummaryRow>
    </RowGroup>
  </PanelCard>
  <InviteDialog v-model:open="inviting" :session-id="sessionId" />
</template>
