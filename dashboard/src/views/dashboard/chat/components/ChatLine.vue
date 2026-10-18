<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// One chat line. The action tray floats over the line on hover or focus and never moves it.
// Reply hands the line to the composer, Timeout picks its length in a menu, Delete runs at once,
// Ban asks the page for its confirm dialog, and Profile opens the one profile dialog of the frame.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { toast } from 'vue-sonner';
import Clock from '~icons/nnz/clock';
import Cross from '~icons/nnz/cross';
import EmailReply from '~icons/nnz/email-reply';
import StopSign from '~icons/nnz/stop-sign';
import Trash from '~icons/nnz/trash';
import UserCircle from '~icons/nnz/user-circle';

import { useChannelAccess } from '@/api/access';
import { useDeleteChatMessage, useTimeoutUser, type ChatLine } from '@/api/chat';
import PlatformLogo from '@/components/layout/PlatformLogo.vue';
import UserName from '@/components/layout/UserName.vue';
import { Button } from '@/components/ui/button';
import { Dialog, DialogClose, DialogContent, DialogDescription, DialogTitle } from '@/components/ui/dialog';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import { canSanction, type RoleKey } from '@/components/layout/profile/profileRoles';
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip';
import { readableNameColor } from '@/lib/accent';
import { purifyChatHtml } from '@/lib/html';
import { formatLength, TIMEOUT_LENGTHS } from '@/lib/time';
import { useProfileDialogStore } from '@/stores/profileDialog';

import AssetCardPopover from '@/components/layout/AssetCard.vue';
import {
  badgeCard as cardOfBadge,
  emoteCard,
  emoteFrames,
  smallestUrl,
  type AssetCard,
  type EmoteImages,
} from '@/lib/assetCards';
import { emoteHeights, gigantifiedEmoteHeight, spacingPadding, splitMentions, type ChatDisplay } from '../chatDisplay';

const props = defineProps<{
  line: ChatLine;
  channelId: string | null;
  display: ChatDisplay;
  index: number;
  me: string | null;
  // The channel's own login: its owner is the broadcaster.
  channelLogin?: string | null;
  focus?: 'on' | 'dim';
  parent?: ChatLine | null;
  // The popped-out dock: tighter row padding, the tray close to the edge.
  compact?: boolean;
  // The viewer chat: a participant's line carries no moderation and no profile (the profile dialog reads operator data), only reply.
  viewer?: boolean;
}>();

const emit = defineEmits<{
  ban: [line: ChatLine];
  focus: [line: ChatLine];
  reply: [line: ChatLine];
}>();

const { t, te, locale } = useI18n();
const timeouts = useTimeoutUser(() => props.channelId);
const deletes = useDeleteChatMessage(() => props.channelId);
const profile = useProfileDialogStore();
const access = useChannelAccess(() => props.channelId);

const time = computed<string>(() =>
  new Date(props.line.timestamp).toLocaleTimeString(locale.value, {
    hour: '2-digit',
    minute: '2-digit',
    hour12: props.display.timestamps === '12',
  }),
);

// A chatter color as the Display option asks: off, as sent, or lifted to a readable contrast.
function shownColor(color: string | null | undefined): string | undefined {
  if (!color || props.display.nameColors === 'off')
    return undefined;

  if (props.display.nameColors === 'sent')
    return color;

  const theme = document.documentElement.classList.contains('light')
    ? 'light'
    : 'dark';

  return readableNameColor(color, theme, 4.5) ?? undefined;
}

const nameColor = computed<string | undefined>(() => shownColor(props.line.color));

// A deleted message set to "show" stays readable; a timeout or ban still strikes the line.
const shownDeleted = computed<boolean>(() =>
  props.line.removed === 'deleted' && props.display.deleted === 'show');
const struck = computed<boolean>(() => Boolean(props.line.removed) && !shownDeleted.value);

const mentionsMe = computed<boolean>(() => {
  if (!props.display.mentions)
    return false;

  return props.line.fragments.some((fragment) => {
    const found = splitMentions(fragment.text, props.me);

    return !fragment.emote && found.some((part) => part.mention);
  });
});

const rowClass = computed<string[]>(() => {
  const classes: string[] = [
    spacingPadding[props.display.spacing],
    props.compact
      ? 'px-2'
      : 'px-3',
  ];

  if (props.display.narrow) {
    classes.push('max-w-[65ch]');
  }

  // A focus lights many lines at once, so it takes an 8% tint and keeps the full accent scarce.
  if (props.focus === 'dim') {
    classes.push('opacity-70');
  }

  // A dimmed line drops its mention tint, so only the picked chatter's lines draw the eye (owner
  // 2026-10-14: "i selected bamo and my name tag still draws my eyes").
  if (props.focus === 'on') {
    classes.push('bg-primary/8');
  } else if (mentionsMe.value && props.focus !== 'dim') {
    classes.push('bg-primary/10');
  } else if (props.display.alternating && props.index % 2 === 1) {
    classes.push('bg-muted/40');
  }

  return classes;
});

function parts(text: string): ReturnType<typeof splitMentions> {
  const me = props.display.mentions
    ? props.me
    : null;

  return splitMentions(text, me);
}

const moderatorBadges = ['moderator', 'lead_moderator'];

// Timeout and Ban show only where Twitch accepts them for this chatter and the signed-in person.
const canSanctionLine = computed<boolean>(() => {
  const roles: RoleKey[] = [];

  if (props.line.isBroadcaster) {
    roles.push('broadcaster');
  }

  const moderatorBadge = props.line.badges.some((badge) =>
    moderatorBadges.includes(badge.setId));

  if (props.line.isModerator || moderatorBadge) {
    roles.push('moderator');
  }

  return canSanction(props.me, props.channelLogin, { username: props.line.username, roles });
});

// Each action needs the key its endpoint checks: Reply sends (a participant sends through the viewer
// endpoint), Timeout and Ban act through moderation:ban, Delete through moderation:delete_message, and
// Profile reads the community member.
const canReply = computed<boolean>(() =>
  access.can(props.viewer
    ? 'chat:viewer:send'
    : 'chat:send'));
const canTimeout = computed<boolean>(() =>
  !props.viewer && canSanctionLine.value && access.can('moderation:ban'));
const canDelete = computed<boolean>(() =>
  !props.viewer && access.can('moderation:delete_message'));
const canOpenProfile = computed<boolean>(() =>
  !props.viewer && access.can('community:read'));
const hasActions = computed<boolean>(() =>
  canReply.value || canTimeout.value || canDelete.value || canOpenProfile.value);

// A badge the server could not resolve has no image, so it is left out rather than shown broken.
const badges = computed(() =>
  props.line.badges.flatMap((badge) => {
    const src = smallestUrl(badge.urls);
    const card = cardOfBadge(badge, t);

    return src && card
      ? [{
          key: badge.setId + badge.id,
          src,
          card,
        }]
      : [];
  }));

// One card per emote fragment, by its place in the line; a fragment with no emote image has none.
const emoteCards = computed<(AssetCard | null)[]>(() =>
  props.line.fragments.map((fragment) =>
    emoteCard(fragment, t)));

// Every part of a line centers on one line height: the emote height on a line with emotes, else 1.5rem.
const lineStyle = computed(() => {
  // A Gigantified Emote Power-up shows its emote large, like Twitch does.
  const emoteHeight = props.line.messageType === 'power_ups_gigantified_emote'
    ? gigantifiedEmoteHeight
    : emoteHeights[props.display.emoteSize];
  const hasEmote = props.line.fragments.some((fragment) => fragment.emote || fragment.type === 'html');

  return {
    '--emote-h': emoteHeight,
    '--line-h': hasEmote
      ? `max(1.5rem, ${emoteHeight})`
      : '1.5rem',
  };
});

// What a reply answers: the parent Twitch named on the message, else the parent line still loaded here.
const replyParent = computed<string | null>(() => {
  const { replyToMessageId, replyParentUserName, replyParentMessageBody } = props.line;

  if (!replyToMessageId)
    return null;

  const name = replyParentUserName ?? props.parent?.displayName;
  const message = replyParentMessageBody ?? props.parent?.message;

  return name && message
    ? t('dashboard.chat.reply.parent', { name, message })
    : t('dashboard.chat.reply.earlier');
});

// A message sent with a Power-up says so above its text.
const powerUp = computed<string | null>(() => {
  const key = `dashboard.chat.redemption.powerUp.${props.line.messageType}`;

  return te(key)
    ? t(key)
    : null;
});

// A timeout or ban says what happened, with the timeout's length; other removals only strike the line.
const removal = computed<string | null>(() => {
  const { removed, timeoutSeconds } = props.line;

  if (removed === 'banned') {
    return t('dashboard.chat.line.banned');
  }

  if (removed === 'timeout' && timeoutSeconds !== undefined) {
    return t('dashboard.chat.line.timedOut', {
      length: formatLength(timeoutSeconds, locale.value),
    });
  }

  return null;
});

async function timeout(seconds: number): Promise<void> {
  try {
    await timeouts.mutateAsync({ targetUserId: props.line.userId, durationSeconds: seconds });
    toast.success(t('dashboard.chat.timeout.result', { name: props.line.displayName }));
  } catch {
    toast.error(t('dashboard.chat.timeout.failure'));
  }
}

async function remove(): Promise<void> {
  try {
    await deletes.mutateAsync(props.line.id);
    toast.success(t('dashboard.chat.delete.result'));
  } catch {
    toast.error(t('dashboard.chat.delete.failure'));
  }
}

function openProfile(event: MouseEvent): void {
  profile.open(
    props.line.userId,
    event.currentTarget instanceof HTMLElement
      ? event.currentTarget
      : null,
  );
}

function emoteUrl(emote: EmoteImages): string | undefined {
  return smallestUrl(emoteFrames(emote));
}

// A posted image (not an emote) in a chat html fragment opens large. The address and caption come from the
// image the purifier already cleaned, and the dialog draws them through its own img, never as markup.
const lightbox = ref<{ src: string; alt: string } | null>(null);

const lightboxOpen = computed<boolean>({
  get: () => lightbox.value !== null,
  set: (open) => {
    if (!open) {
      lightbox.value = null;
    }
  },
});

function openImage(event: MouseEvent): void {
  const target = event.target;

  if (!(target instanceof HTMLImageElement) || target.classList.contains('emote') || !target.src) return;

  event.stopPropagation();
  lightbox.value = { src: target.src, alt: target.alt };
}

// A click on the line itself picks its chatter; a click on a name or an action does its own job.
function pick(event: MouseEvent): void {
  const target = event.target;

  if (target instanceof Element && target.closest('button, a')) return;

  emit('focus', props.line);
}
</script>

<template>
  <div
    :data-line-id="line.id"
    :data-removed="line.removed"
    :data-rows="display.rows"
    :data-alt="display.alternating && index % 2 === 1 ? 'on' : 'off'"
    :data-mention="mentionsMe ? 'true' : undefined"
    :data-focus="focus"
    class="group/line relative flex cursor-pointer gap-2 rounded-lg text-sm leading-(--line-h) hover:bg-muted focus-within:bg-muted"
    :class="rowClass"
    :style="lineStyle"
    @click="pick"
  >
    <time
      v-if="display.timestamps !== 'off' && display.rows === 'one'"
      class="flex-none text-muted-foreground tabular-nums"
      :datetime="line.timestamp"
    >{{ time }}</time>
    <!-- Two lines read like Discord's cozy mode: the avatar spans both lines at the left, the message sits under
         the name (owner 2026-10-14: "this combination looks super bad all combined"). -->
    <span
      v-if="display.avatars && line.avatarUrl"
      class="flex flex-none"
      :class="display.rows === 'two' ? 'pt-0.5' : 'h-(--line-h) items-center'"
    >
      <img
        data-avatar
        :src="line.avatarUrl"
        alt=""
        class="rounded-full"
        :class="display.rows === 'two' ? 'size-9' : 'size-6'"
      />
    </span>
    <div class="min-w-0 flex-1">
      <p
        v-if="replyParent"
        data-reply-parent
        class="flex items-center gap-1 truncate text-xs leading-5 text-muted-foreground"
      >
        <EmailReply class="size-3.5 flex-none" aria-hidden="true" />
        <span class="truncate">{{ replyParent }}</span>
      </p>
      <p
        v-if="powerUp"
        data-power-up
        class="text-xs leading-5 text-muted-foreground"
      >{{ powerUp }}</p>
      <p
        class="wrap-anywhere"
        :class="{ 'text-muted-foreground': line.removed, 'line-through': struck }"
      >
        <!-- The name's focus ring reaches 6px past its letters, so it keeps 8px from the marks and the message. -->
        <span
          data-line-head
          class="h-(--line-h) items-center gap-2 align-top"
          :class="display.rows === 'two' ? 'flex' : 'me-2 inline-flex'"
        >
          <span
            v-if="display.platform || (display.badges && badges.length > 0)"
            class="flex items-center gap-1"
          >
            <PlatformLogo
              v-if="display.platform"
              data-platform
              :platform="line.provider"
              class="size-4 flex-none"
            />
            <template v-if="display.badges">
              <AssetCardPopover
                v-for="badge in badges"
                :key="badge.key"
                :label="$t('asset.badgeLabel', { name: badge.card.title })"
                :title="badge.card.title"
                :image="badge.card.image"
                :facts="badge.card.facts"
                :link="badge.card.link"
              >
                <img data-badge :src="badge.src" alt="" class="size-4 flex-none" />
              </AssetCardPopover>
            </template>
          </span>
          <UserName
            :user-id="line.userId"
            :name="line.displayName"
            :style="{ color: nameColor }"
          />
          <time
            v-if="display.timestamps !== 'off' && display.rows === 'two'"
            class="flex-none text-xs text-muted-foreground tabular-nums"
            :datetime="line.timestamp"
          >{{ time }}</time>
        </span>
        <template v-for="(fragment, at) in line.fragments" :key="at">
          <AssetCardPopover
            v-if="fragment.emote && emoteUrl(fragment.emote) && emoteCards[at]"
            :label="$t('asset.emoteLabel', { name: fragment.text })"
            :title="emoteCards[at]!.title"
            :image="emoteCards[at]!.image"
            :facts="emoteCards[at]!.facts"
            :people="emoteCards[at]!.people"
            :link="emoteCards[at]!.link"
            class="mx-0.5 my-[calc((var(--line-h)-var(--emote-h))/2)]"
          >
            <img
              data-emote
              :src="emoteUrl(fragment.emote)"
              :alt="fragment.text"
              class="inline-block h-(--emote-h)"
            />
          </AssetCardPopover>
          <span
            v-else-if="fragment.type === 'html'"
            data-html
            class="[&_.emote]:mx-0.5 [&_.emote]:my-[calc((var(--line-h)-var(--emote-h))/2)] [&_.emote]:inline-block [&_.emote]:h-(--emote-h) [&_.emote]:align-top [&_img]:max-h-32 [&_img:not(.emote)]:cursor-zoom-in"
            @click="openImage"
            v-html="purifyChatHtml(fragment.text)"
          />
          <span
            v-else-if="fragment.type === 'mention' && fragment.mention"
            data-mention-tag
            class="font-bold"
            :style="{ color: shownColor(fragment.mention.color) }"
          >{{ fragment.text }}</span>
          <template v-else>
            <template v-for="(part, slot) in parts(fragment.text)" :key="slot">
              <mark v-if="part.mention" data-mention class="bg-transparent font-bold text-foreground">{{ part.text }}</mark>
              <span v-else-if="part.at" data-mention-tag class="font-bold">{{ part.text }}</span>
              <template v-else>{{ part.text }}</template>
            </template>
          </template>
        </template>
        <span v-if="shownDeleted" data-deleted-tag class="ml-1 text-muted-foreground">{{
          $t('dashboard.chat.display.deletedTag')
        }}</span>
      </p>
      <!-- At the row's top-right corner, above the row: never over this line or a name, and touching the row
           so the hover holds on the way up (owner 2026-10-13 and 2026-10-14). Its own layer and `invisible`
           when hidden: a fast sweep left half-painted trays over the row above (owner 2026-10-14). -->
      <div
        v-if="!line.removed && hasActions"
        :class="compact ? 'right-1' : 'right-3'"
        class="absolute bottom-full z-10 translate-y-0.5 flex items-center rounded-[12px] border p-0.5 invisible opacity-0 will-change-[opacity] group-hover/line:visible group-hover/line:opacity-100 group-focus-within/line:visible group-focus-within/line:opacity-100 has-aria-expanded:visible has-aria-expanded:opacity-100"
        data-tray
        role="group"
        :aria-label="$t('dashboard.chat.line.actions', { name: line.displayName })"
      >
        <Tooltip v-if="canReply">
          <TooltipTrigger as-child>
            <Button
              data-line-action="reply"
              variant="ghost"
              size="icon"
              :aria-label="$t('dashboard.chat.reply.label', { name: line.displayName })"
              @click="emit('reply', line)"
            >
              <EmailReply class="size-5" aria-hidden="true" />
            </Button>
          </TooltipTrigger>
          <TooltipContent>{{ $t('dashboard.chat.reply.tip') }}</TooltipContent>
        </Tooltip>
        <DropdownMenu v-if="canTimeout">
          <Tooltip>
            <TooltipTrigger as-child>
              <DropdownMenuTrigger as-child>
                <Button
                  data-line-action="timeout"
                  variant="ghost"
                  size="icon"
                  :aria-label="$t('dashboard.chat.timeout.label', { name: line.displayName })"
                >
                  <Clock class="size-5" aria-hidden="true" />
                </Button>
              </DropdownMenuTrigger>
            </TooltipTrigger>
            <TooltipContent>{{ $t('dashboard.chat.timeout.tip') }}</TooltipContent>
          </Tooltip>
          <DropdownMenuContent align="end">
            <DropdownMenuLabel>{{ $t('dashboard.chat.timeout.menu') }}</DropdownMenuLabel>
            <DropdownMenuItem
              v-for="seconds in TIMEOUT_LENGTHS"
              :key="seconds"
              :data-timeout-length="seconds"
              @select="timeout(seconds)"
            >
              {{ $t(`dashboard.chat.lengths.${seconds}`) }}
            </DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
        <Tooltip v-if="canDelete">
          <TooltipTrigger as-child>
            <Button
              data-line-action="delete"
              variant="ghost"
              size="icon"
              :aria-label="$t('dashboard.chat.delete.label')"
              @click="remove"
            >
              <Trash class="size-5" aria-hidden="true" />
            </Button>
          </TooltipTrigger>
          <TooltipContent>{{ $t('dashboard.chat.delete.label') }}</TooltipContent>
        </Tooltip>
        <Tooltip v-if="canTimeout">
          <TooltipTrigger as-child>
            <Button
              data-line-action="ban"
              variant="ghost"
              size="icon"
              :aria-label="$t('dashboard.chat.ban.label', { name: line.displayName })"
              @click="emit('ban', line)"
            >
              <StopSign class="size-5" aria-hidden="true" />
            </Button>
          </TooltipTrigger>
          <TooltipContent>{{ $t('dashboard.chat.ban.tip') }}</TooltipContent>
        </Tooltip>
        <Tooltip v-if="canOpenProfile">
          <TooltipTrigger as-child>
            <Button
              data-line-action="profile"
              variant="ghost"
              size="icon"
              :aria-label="$t('dashboard.chat.line.profile', { name: line.displayName })"
              @click="openProfile"
            >
              <UserCircle class="size-5" aria-hidden="true" />
            </Button>
          </TooltipTrigger>
          <TooltipContent>{{ $t('dashboard.chat.line.profileTip') }}</TooltipContent>
        </Tooltip>
      </div>
    </div>
    <span v-if="removal" data-removal class="flex-none text-muted-foreground">{{ removal }}</span>
    <Dialog v-model:open="lightboxOpen">
      <DialogContent class="w-auto max-w-[90vw] gap-3 p-3 sm:max-w-[90vw]" :show-close-button="false">
        <DialogClose as-child>
          <Button
            variant="ghost"
            size="icon-sm"
            class="absolute top-4 right-4 z-10 bg-background/80"
            :aria-label="$t('common.close')"
          >
            <Cross />
          </Button>
        </DialogClose>
        <DialogTitle class="sr-only">{{ $t('dashboard.chat.image.title', { name: line.displayName }) }}</DialogTitle>
        <DialogDescription class="sr-only">{{ lightbox?.alt || $t('dashboard.chat.image.untitled') }}</DialogDescription>
        <img
          v-if="lightbox"
          data-lightbox-image
          :src="lightbox.src"
          :alt="lightbox.alt"
          class="mx-auto max-h-[85vh] max-w-[calc(90vw-1.5rem)] rounded-lg object-contain"
        />
      </DialogContent>
    </Dialog>
  </div>
</template>
