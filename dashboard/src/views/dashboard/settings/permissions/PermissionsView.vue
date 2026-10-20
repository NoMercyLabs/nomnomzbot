<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Permissions: the team and its roles, the lowest role for each action, and the dangerous capabilities given to one person.
// Each is its own tab, and the bot's long list of actions is split by area, so the page never runs on for miles.
// The tab (?view=) and the area (?area=) sit in the address, so a link or the Back button lands on the same place.
// What the caller may change follows the bot's own gate (roles:manage); a reader sees the page without the controls.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRoute, useRouter } from 'vue-router';

import { useChannelAccess } from '@/api/access';
import { useActionMatrix, useMembers, usePersonGrants } from '@/api/permissions';
import BackLink from '@/components/layout/BackLink.vue';
import ListDetail from '@/components/layout/ListDetail.vue';
import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PageTabs from '@/components/layout/PageTabs.vue';
import type { PageTab } from '@/components/layout/pageTab';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePageView } from '@/composables/usePageView';

import { groupByArea, matchesSearch, useActionName, useAreaLabel } from './actionArea';
import ActionAreaList from './components/ActionAreaList.vue';
import ActionMatrixPanel from './components/ActionMatrixPanel.vue';
import PersonGrantsPanel from './components/PersonGrantsPanel.vue';
import TeamRolesPanel from './components/TeamRolesPanel.vue';
import { isAllowedGrant } from './personGrant';

const { t } = useI18n();
const route = useRoute();
const router = useRouter();
const { channelId } = useCurrentChannel();
const access = useChannelAccess(channelId);
const members = useMembers(channelId);
const matrix = useActionMatrix(channelId);
const grants = usePersonGrants(channelId);
const labelOf = useAreaLabel();
const nameOf = useActionName();

const VIEWS = ['team', 'actions', 'capabilities'] as const satisfies readonly string[];
const view = usePageView(VIEWS);

const counts = computed(() =>
  ({
    team: members.data.value?.length,
    actions: matrix.data.value?.length,
    capabilities: grants.data.value?.filter(isAllowedGrant).length,
  }) as const satisfies Record<(typeof VIEWS)[number], number | undefined>);

const tabs = computed<PageTab[]>(() =>
  VIEWS.map((value) => ({
    value,
    label: t(`dashboard.settings.permissions.tabs.${value}`),
    count: counts.value[value],
  })));

const search = ref('');
const areas = computed(() =>
  groupByArea(
    (matrix.data.value ?? []).filter((row) =>
      matchesSearch(row, search.value, nameOf)),
    labelOf,
  ));
const area = usePageView(
  () =>
    areas.value.map((group) => group.area),
  'area',
);

// On a phone the list and the area take turns: an area in the address opens it over the list.
const areaOpen = computed<boolean>(() => typeof route.query.area === 'string');

function closeArea(): void {
  const { area: _closed, ...kept } = route.query;

  void router.push({ query: kept });
}
</script>

<template>
  <PageHeader :title="$t('frame.nav.settingsPages.permissions')" :crumb="$t('dashboard.settings.crumb')">
    <template #bar><PageTabs :tabs="tabs" /></template>
  </PageHeader>

  <ListDetail v-if="view === 'actions'" data-view="permissions" :show-detail="areaOpen">
    <template #list>
      <ActionAreaList
        v-model:search="search"
        :areas="areas"
        :selected="area"
        @pick="area = $event"
      />
    </template>
    <template v-if="area" #detail>
      <div class="flex flex-1 flex-col gap-2 md:min-h-0">
        <BackLink
          data-detail-back
          data-page-inset
          class="self-start md:hidden"
          :label="$t('dashboard.settings.permissions.areas.back')"
          @click="closeArea"
        />
        <ActionMatrixPanel :can-manage="access.can('roles:manage')" :area="area" :search="search" />
      </div>
    </template>
  </ListDetail>

  <PageBody v-else>
    <div data-view="permissions" class="flex flex-col gap-4">
      <PersonGrantsPanel v-if="view === 'capabilities'" :can-manage="access.can('roles:manage')" />
      <TeamRolesPanel v-else :can-manage="access.can('roles:manage')" />
    </div>
  </PageBody>
</template>
