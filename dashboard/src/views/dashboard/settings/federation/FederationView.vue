<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Federation: which data this channel shares with, or accepts from, other bot instances. Every kind is off until
// the streamer picks accept, share or both. A choice made for all trusted peers is one row per kind; a choice that
// names one peer is listed below it. The page follows the bot: a change made elsewhere shows without a reload.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  federationOptInTypes,
  useDisableFederationOptIn,
  useFederationOptIns,
  useSaveFederationOptIn,
  type FederationDirection,
  type FederationOptIn,
  type FederationOptInType,
} from '@/api/federation';
import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';
import { errorText } from '@/lib/errors';

import FederationChoiceRow, { type FederationChoice } from './components/FederationChoiceRow.vue';
import FederationPeerRow from './components/FederationPeerRow.vue';

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const optIns = useFederationOptIns(channelId);
const save = useSaveFederationOptIn(channelId);
const disable = useDisableFederationOptIn(channelId);

const state = usePanelState(optIns);

const pageError = ref<string | undefined>(undefined);
const busy = ref(false);

const rows = computed(() => optIns.data.value ?? []);

const anyPeerRows = computed(() => rows.value.filter((optIn) => !optIn.peerId));

const peerRows = computed(() => rows.value.filter((optIn) => optIn.peerId && optIn.isEnabled));

function rowOf(type: FederationOptInType): FederationOptIn | undefined {
  return anyPeerRows.value.find((optIn) => optIn.optInType === type);
}

function choiceOf(type: FederationOptInType): FederationChoice {
  const row = rowOf(type);

  return row?.isEnabled
    ? (row.direction as FederationDirection)
    : 'off';
}

async function run(action: () => Promise<void>): Promise<void> {
  if (busy.value) return;

  pageError.value = undefined;
  busy.value = true;

  try {
    await action();
  } catch (failure) {
    pageError.value = errorText(failure, t);
  } finally {
    busy.value = false;
  }
}

// Off keeps the saved way and turns the choice off; a way turns the choice on for any trusted peer.
async function choose(type: FederationOptInType, choice: FederationChoice): Promise<void> {
  const row = rowOf(type);

  if (choice === 'off') {
    if (row === undefined) return;

    await run(() =>
      save.mutateAsync({
        peerId: null,
        optInType: type,
        direction: row.direction,
        isEnabled: false,
      }));

    return;
  }

  await run(() =>
    save.mutateAsync({
      peerId: null,
      optInType: type,
      direction: choice,
      isEnabled: true,
    }));
}

async function turnOff(optIn: FederationOptIn): Promise<void> {
  await run(() => disable.mutateAsync(optIn.id));
}
</script>

<template>
  <PageHeader :title="$t('frame.nav.settingsPages.federation')" :crumb="$t('dashboard.settings.crumb')" />

  <PageBody>
    <PanelCard
      data-view="federation"
      tier="lead"
      :title="$t('dashboard.settings.federation.title')"
      :description="$t('dashboard.settings.federation.description')"
      :state="state"
      :error-text="$t('dashboard.settings.federation.loadFailed')"
      @retry="optIns.refetch()"
    >
      <RowGroup>
        <FederationChoiceRow
          v-for="type in federationOptInTypes"
          :key="type"
          :type="type"
          :choice="choiceOf(type)"
          :busy="busy"
          @choose="choose(type, $event)"
        />
      </RowGroup>
    </PanelCard>

    <PanelCard
      v-if="peerRows.length > 0"
      data-view="federation-peers"
      :title="$t('dashboard.settings.federation.peers.title')"
      :description="$t('dashboard.settings.federation.peers.description')"
    >
      <RowGroup>
        <FederationPeerRow
          v-for="optIn in peerRows"
          :key="optIn.id"
          :opt-in="optIn"
          :busy="busy"
          @disable="turnOff(optIn)"
        />
      </RowGroup>
    </PanelCard>

    <p v-if="pageError" role="alert" class="pt-3 text-sm text-destructive">{{ pageError }}</p>
  </PageBody>
</template>
