<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Everyone banned in the channel, read live from Twitch, with who banned them and why. Unban is outline and has no
// confirm, because banning the person again undoes it.
import { useI18n } from 'vue-i18n';
import { toast } from 'vue-sonner';

import { useBans, useLiftBanned, type BannedUser } from '@/api/bans';
import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';
import { useTime } from '@/composables/useTime';

const { t } = useI18n();
const { can } = useCan();
const { channelId } = useCurrentChannel();
const time = useTime();

const bans = useBans(channelId);
const lift = useLiftBanned(channelId);
const state = usePanelState(bans, (list: BannedUser[]) =>
  list.length === 0);

function detailOf(ban: BannedUser): string {
  const when = t('dashboard.settings.banned.by', {
    name: ban.bannedBy,
    when: time.relative(ban.bannedAt),
  });

  return ban.reason
    ? `${ban.reason} · ${when}`
    : when;
}

function unban(ban: BannedUser): void {
  lift.mutate(ban, {
    onError: () => toast.error(t('dashboard.settings.banned.error', { name: ban.username })),
  });
}
</script>

<template>
  <PageHeader
    :title="$t('frame.nav.settingsPages.banned')"
    :crumb="$t('dashboard.settings.banned.crumb')"
  />
  <PageBody>
    <p class="text-sm text-muted-foreground">{{ $t("dashboard.settings.banned.explain") }}</p>
    <PanelCard
      tier="lead"
      :title="$t('dashboard.settings.banned.listTitle')"
      :state="state"
      :empty-text="$t('dashboard.settings.banned.empty')"
      :error-text="$t('dashboard.settings.banned.loadError')"
      @retry="bans.refetch()"
    >
      <RowGroup>
        <SummaryRow
          v-for="ban in bans.data.value"
          :key="ban.userId"
          :data-ban="ban.userId"
          :title="ban.username"
          :detail="detailOf(ban)"
        >
          <template v-if="can('moderation:unban')" #trailing>
            <Button
              variant="outline"
              size="sm"
              :data-ban-lift="ban.userId"
              :disabled="lift.isPending.value"
              @click="unban(ban)"
            >
              {{ $t("dashboard.settings.banned.unban") }}
            </Button>
          </template>
        </SummaryRow>
      </RowGroup>
    </PanelCard>
  </PageBody>
</template>
