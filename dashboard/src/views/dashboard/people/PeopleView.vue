<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The People page: a list of everyone the bot has seen, tabs by role, and one person beside it.
// Tabs without a list on the bot (Subscribers, Regulars) say so instead of drawing an empty list.
// The person in view is the login in ?u=, so a link opens them.
// Typing asks the bot for every known user in any channel and shows name and avatar only. Enter or a
// click opens the person in the same profile view the dialog shows.
// A button looks a name up live on Twitch for someone the bot has never seen.
import { refDebounced, useEventListener, useLocalStorage } from '@vueuse/core';
import { computed, ref, watchEffect } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRoute, useRouter } from 'vue-router';

import {
  TwitchUserNotFoundError,
  useTwitchLookup,
  useViewerSearch,
  type ViewerOption,
} from '@/api/community';
import { useCommunityList, type CommunityPage, type CommunityRow } from '@/api/people';
import BackLink from '@/components/layout/BackLink.vue';
import ListDetail from '@/components/layout/ListDetail.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PageTabs from '@/components/layout/PageTabs.vue';
import type { PageTab } from '@/components/layout/pageTab';
import type { ProfilePreview } from '@/components/layout/profile/ProfileHeader.vue';
import ProfileView from '@/components/layout/profile/ProfileView.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import type { PanelState } from '@/composables/usePanelState';
import SearchField from '@/components/layout/SearchField.vue';
import SegmentedControl from '@/components/layout/SegmentedControl.vue';
import VirtualList from '@/components/layout/VirtualList.vue';
import { Button } from '@/components/ui/button';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePageView } from '@/composables/usePageView';
import { oneOf } from '@/lib/array';
import { usePanelState } from '@/composables/usePanelState';

import Cross from '~icons/nnz/cross';

import PersonRow from './components/PersonRow.vue';
import SearchResultRow from './components/SearchResultRow.vue';

// The light row a search hit or a Twitch lookup gives: name, avatar and login, nothing heavier.
type Seed = {
  id: string;
  login: string;
  displayName: string;
  profileImageUrl: string | null;
};

const SEARCH_PAUSE_MS = 250;
const NEW_WINDOW_DAYS = 90;
const ROW_HEIGHT = 56;
const TAB_VALUES = ['all', 'moderator', 'vip', 'subscriber', 'regular', 'new'] as const;

type PeopleTab = (typeof TAB_VALUES)[number];
type PeopleSort = 'recent' | 'alphabetical';
const UNLISTED = ['subscriber', 'regular'] as const satisfies readonly PeopleTab[];

const { t } = useI18n();
const route = useRoute();
const router = useRouter();
const { channelId } = useCurrentChannel();

const everyone = useCommunityList(channelId, 'all', '');
const moderators = useCommunityList(channelId, 'moderator', '');
const vips = useCommunityList(channelId, 'vip', '');

const view = usePageView(TAB_VALUES);
const search = ref('');
const pausedSearch = refDebounced(search, SEARCH_PAUSE_MS);
const typed = computed<string>(() => search.value.trim());
const searching = computed<boolean>(() => typed.value !== '');
const found = useViewerSearch(channelId, () =>
  pausedSearch.value.trim());
const lookup = useTwitchLookup(channelId);
const seed = ref<Seed | null>(null);
const lookupMissing = ref('');

function rowsOf(query: typeof everyone): CommunityRow[] | undefined {
  return query.data.value?.pages.flatMap((page: CommunityPage) => page.rows);
}

function totalOf(query: typeof everyone): number | undefined {
  return query.data.value?.pages[0]?.total ?? undefined;
}

const tabs = computed<PageTab<PeopleTab>[]>(() => [
  {
    value: 'all',
    label: t('dashboard.people.tabs.all'),
    count: totalOf(everyone),
  },
  {
    value: 'moderator',
    label: t('dashboard.people.tabs.moderator'),
    count: totalOf(moderators),
  },
  {
    value: 'vip',
    label: t('dashboard.people.tabs.vip'),
    count: totalOf(vips),
  },
  {
    value: 'subscriber',
    label: t('dashboard.people.tabs.subscriber'),
    disabled: true,
    reason: t('dashboard.people.list.noSubscribers'),
  },
  {
    value: 'regular',
    label: t('dashboard.people.tabs.regular'),
    disabled: true,
    reason: t('dashboard.people.list.noRegulars'),
  },
  { value: 'new', label: t('dashboard.people.tabs.new') },
]);

const activeQuery = computed<typeof everyone | null>(() => {
  if (oneOf(UNLISTED, view.value))
    return null;

  if (view.value === 'moderator')
    return moderators;

  if (view.value === 'vip')
    return vips;

  return everyone;
});

// The list reached its end: fetch the next page, so everyone known to the channel is a scroll away.
function loadMore(): void {
  const query = activeQuery.value;

  if (query?.hasNextPage.value && !query.isFetchingNextPage.value) {
    void query.fetchNextPage();
  }
}

// Someone the bot never observed has no first-seen moment, so they are not new.
function isNew(row: CommunityRow): boolean {
  if (!row.firstSeen)
    return false;

  return Date.now() - new Date(row.firstSeen).getTime() <= NEW_WINDOW_DAYS * 24 * 60 * 60 * 1000;
}

const tabRows = computed<CommunityRow[] | undefined>(() => {
  if (activeQuery.value === null)
    return [];

  const rows = rowsOf(activeQuery.value);

  return view.value === 'new'
    ? rows?.filter(isNew)
    : rows;
});

// The bot hands rows over last seen first; A to Z sorts the loaded rows by name, so it loads every page first.
const sort = useLocalStorage<PeopleSort>('nnz.people.sort', 'recent');
const sortOptions = computed(
  () =>
    [
      { value: 'recent', label: t('dashboard.people.sort.recent') },
      { value: 'alphabetical', label: t('dashboard.people.sort.alphabetical') },
    ] as const satisfies readonly { value: PeopleSort; label: string }[],
);

const visible = computed<CommunityRow[] | undefined>(() => {
  if (sort.value === 'recent' || !tabRows.value)
    return tabRows.value;

  return [...tabRows.value].sort((a, b) =>
    a.displayName.localeCompare(b.displayName, undefined, { sensitivity: 'base' }),
  );
});

watchEffect(() => {
  if (sort.value === 'alphabetical') {
    loadMore();
  }
});

// Hits of an older keystroke never show under newer text.
const hits = computed(() =>
  pausedSearch.value.trim() === typed.value
    ? found.data.value
    : undefined,
);

const tabState = usePanelState(
  {
    data: visible,
    isPending: computed<boolean>(() => activeQuery.value?.isPending.value ?? false),
    isError: computed<boolean>(() => activeQuery.value?.isError.value ?? false),
  },
  (rows: CommunityRow[]) => rows.length === 0,
);
const searchState = usePanelState(
  {
    data: hits,
    isPending: found.isPending,
    isError: found.isError,
  },
  (options: ViewerOption[]) => options.length === 0,
);
const listState = computed<PanelState>(() =>
  searching.value
    ? searchState.value
    : tabState.value,
);

const emptyText = computed<string>(() => {
  if (view.value === 'subscriber')
    return t('dashboard.people.list.noSubscribers');

  if (view.value === 'regular')
    return t('dashboard.people.list.noRegulars');

  if (searching.value)
    return t('dashboard.people.noMatch', { query: typed.value });

  return t('dashboard.people.list.empty');
});

const hint = computed<string>(() => t(`dashboard.people.hints.${view.value}`));
// The bot knows how many people a tab holds before every page is loaded; New is counted from what is loaded.
const listTotal = computed<number>(() => {
  const known = activeQuery.value && view.value !== 'new'
    ? totalOf(activeQuery.value)
    : undefined;

  return known ?? visible.value?.length ?? 0;
});
const count = computed<string | undefined>(() => {
  if (listState.value !== 'ready')
    return undefined;

  const total = searching.value
    ? (hits.value?.length ?? 0)
    : listTotal.value;

  return t('dashboard.people.count', { count: total }, total);
});

// The person in ?u= can sit in any loaded list (a moderator is also in All), so look in all three.
const asked = computed<string>(() =>
  typeof route.query.u === 'string'
    ? route.query.u
    : '');
const loadedRow = computed<CommunityRow | null>(() => {
  const login = asked.value.toLowerCase();

  if (login === '')
    return null;

  const loaded: CommunityRow[] = [everyone, moderators, vips].flatMap(
    (query) => rowsOf(query) ?? [],
  );

  return loaded.find((row) => row.username.toLowerCase() === login) ?? null;
});

// A seeded person that is not in a loaded list: the full record is asked for only now, never while typing.
const seedInView = computed<Seed | null>(() =>
  loadedRow.value === null && seed.value?.login.toLowerCase() === asked.value.toLowerCase()
    ? seed.value
    : null,
);
// The person in view: the profile beside the list is the same ProfileView the dialog shows, and it loads them
// by id.
const selectedId = computed<string | null>(
  () => loadedRow.value?.id ?? seedInView.value?.id ?? null,
);
const preview = computed<ProfilePreview | null>(() => {
  const from = seedInView.value;

  if (loadedRow.value || !from)
    return loadedRow.value;

  return {
    displayName: from.displayName,
    username: from.login,
    profileImageUrl: from.profileImageUrl,
  };
});

// Another person opens on their Overview: the tab of the last one belongs to them.
function show(login: string): void {
  void router.push({
    query: {
      ...route.query,
      u: login,
      tab: undefined,
    },
  });
}

function pick(row: CommunityRow): void {
  show(row.username);
}

function open(from: Seed): void {
  seed.value = from;
  show(from.login);
}

function openOption(option: ViewerOption): void {
  open({
    id: option.id,
    login: option.subLabel.replace(/^@/, ''),
    displayName: option.label,
    profileImageUrl: option.imageUrl ?? null,
  });
}

function openFirstHit(): void {
  const first = hits.value?.[0];

  if (first) {
    openOption(first);
  }
}

async function lookUp(): Promise<void> {
  const login = typed.value.replace(/^@/, '');

  lookupMissing.value = '';

  try {
    const person = await lookup.mutateAsync(login);

    open({
      id: person.id,
      login: person.login,
      displayName: person.displayName,
      profileImageUrl: person.profileImageUrl ?? null,
    });
  } catch (error: unknown) {
    if (error instanceof TwitchUserNotFoundError) {
      lookupMissing.value = login;
    }
  }
}

function back(): void {
  const { u, tab, ...rest } = route.query;

  void u;
  void tab;
  void router.push({ query: rest });
}

function isTyping(target: EventTarget | null): boolean {
  return (
    target instanceof HTMLElement && target.matches('input, textarea, select, [contenteditable]')
  );
}

// Escape closes the open profile, as the Back link and browser Back do. A key meant for the search box stays there.
function closeOnEscape(event: KeyboardEvent): void {
  if (
    event.key !== 'Escape' ||
    event.defaultPrevented ||
    isTyping(event.target) ||
    asked.value === ''
  ) return;

  back();
}

useEventListener(document, 'keydown', closeOnEscape);
</script>

<template>
  <PageHeader :title="$t('dashboard.people.title')" :crumb="$t('dashboard.people.crumb')">
    <template #bar>
      <PageTabs :tabs="tabs" />
    </template>
  </PageHeader>
  <ListDetail :show-detail="asked !== ''">
    <template #list>
      <PanelCard
        class="min-h-80 flex-1"
        :title="$t('dashboard.people.list.title')"
        :description="count === undefined ? hint : `${count} · ${hint}`"
        :state="listState"
        :empty-text="emptyText"
        :error-text="$t('dashboard.people.list.error')"
        @retry="searching ? found.refetch() : activeQuery?.refetch()"
      >
        <template #actions>
          <!-- Concentric with the panel corner: 8px from the top and the right, so a 20 - 8 = 12px radius. -->
          <div class="-mr-2 flex min-w-0 items-center gap-2">
            <SegmentedControl
              v-if="!searching"
              v-model="sort"
              :label="$t('dashboard.people.sort.label')"
              :options="sortOptions"
            />
            <SearchField
              v-model="search"
              class="w-52 min-w-0 shrink rounded-[12px]"
              :placeholder="$t('dashboard.people.search')"
              @keydown.enter="openFirstHit"
            />
          </div>
        </template>
        <div v-if="searching" class="grid gap-0.5">
          <SearchResultRow
            v-for="option in hits ?? []"
            :key="option.id"
            :option="option"
            :selected="option.subLabel.replace(/^@/, '').toLowerCase() === asked.toLowerCase()"
            @pick="openOption(option)"
          />
        </div>
        <VirtualList
          v-else
          class="h-full"
          :items="visible ?? []"
          :item-key="(row: CommunityRow) => row.id"
          :estimate-size="ROW_HEIGHT"
          :label="$t('dashboard.people.list.label')"
          @end="loadMore"
        >
          <template #default="{ item }">
            <PersonRow
              :person="item"
              :selected="item.username.toLowerCase() === asked.toLowerCase()"
              @pick="pick(item)"
            />
          </template>
        </VirtualList>
        <template v-if="searching" #footer>
          <Button variant="outline" size="sm" :disabled="lookup.isPending.value" @click="lookUp">
            {{ $t("dashboard.people.lookup.button") }}
          </Button>
          <p v-if="lookupMissing !== ''" role="alert" class="text-sm text-muted-foreground">
            {{ $t("dashboard.people.lookup.notFound", { login: lookupMissing }) }}
          </p>
          <p v-else-if="lookup.isError.value" role="alert" class="text-sm text-destructive">
            {{ $t("dashboard.people.lookup.error") }}
          </p>
        </template>
      </PanelCard>
    </template>
    <template #detail>
      <!-- On a phone the profile grows with its content and the page scrolls once; beside the list it scrolls inside
           its card. -->
      <div v-if="selectedId" data-person-detail class="flex flex-1 flex-col gap-2 md:min-h-0">
        <BackLink
          data-page-inset
          class="self-start md:hidden"
          :label="$t('dashboard.people.back')"
          @click="back"
        />
        <div
          class="relative flex flex-1 flex-col border-y bg-card md:min-h-0 md:overflow-hidden md:rounded-xl md:border"
        >
          <!-- A phone has the Back link above; beside the list the profile closes with its own button. -->
          <Button
            data-person-close
            variant="ghost"
            size="icon"
            class="absolute top-2 right-2 z-10 max-md:hidden"
            :aria-label="$t('common.close')"
            @click="back"
          >
            <Cross class="size-5" aria-hidden="true" />
          </Button>
          <ProfileView :user-id="selectedId" :preview="preview" />
        </div>
      </div>
      <PanelCard
        v-else
        class="flex-1"
        :title="$t('dashboard.people.detail.title')"
        state="empty"
        :empty-text="
          asked === ''
            ? $t('dashboard.people.detail.pick')
            : $t('dashboard.people.detail.notFound', { login: asked })
        "
      />
    </template>
  </ListDetail>
</template>
