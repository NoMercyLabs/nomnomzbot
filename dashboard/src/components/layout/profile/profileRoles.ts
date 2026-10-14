// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { Component } from 'vue';
import Crown from '~icons/nnz/crown';
import Gem from '~icons/nnz/gem';
import Shield from '~icons/nnz/shield';
import Star from '~icons/nnz/star';

import type { CommunityMember } from '@/api/community';
import type { PersonProfile } from '@/api/people';

export type RoleKey = 'broadcaster' | 'moderator' | 'vip' | 'subscriber';

// The icon beside each role badge, as in the mockup (profile.js roles()).
export const roleIcons = {
  broadcaster: Crown,
  moderator: Shield,
  vip: Gem,
  subscriber: Star,
} as const satisfies Record<RoleKey, Component>;

// Twitch sends a subscription tier as its code ("1000"); a person reads it as its number.
const tierNumbers = {
  1000: 1,
  2000: 2,
  3000: 3,
} as const satisfies Record<number, number>;

// The tier number for a Twitch tier code, or null for a code Twitch has not defined.
export function tierNumber(code: string | null | undefined): number | null {
  return tierNumbers[Number(code) as keyof typeof tierNumbers] ?? null;
}

// The broadcaster is the channel's own account (a Twitch login is unique); a stored role row never makes
// someone else the broadcaster.
export function isChannelOwner(
  person: CommunityMember,
  channelLogin: string | null | undefined,
): boolean {
  return Boolean(channelLogin) && person.username.toLowerCase() === channelLogin?.toLowerCase();
}

// The roles the bot can prove for a person: the channel's own account, Twitch's own answers (isVip,
// isModerator), the channel role the bot stores, and the subscription the bot has seen. A role it cannot
// prove is left out.
export function roleKeys(
  person: CommunityMember,
  profile: PersonProfile | null,
  channelLogin: string | null | undefined,
): RoleKey[] {
  const stored = profile?.identity.managementRole?.toLowerCase();
  const roles: RoleKey[] = [];

  if (isChannelOwner(person, channelLogin)) {
    roles.push('broadcaster');
  }

  if (stored === 'moderator' || person.isModerator === true) {
    roles.push('moderator');
  }

  if (person.isVip === true) {
    roles.push('vip');
  }

  if (profile?.activity?.isSubscriber === true || Boolean(profile?.identity.subTier)) {
    roles.push('subscriber');
  }

  return roles;
}
