# EventSub test fixtures vs the Twitch event reference tables

62 match, 14 fixed, 0 open. Every one of the 76 fixtures carries every required field of its event table and no field the table lacks.

Authority: the field tables of the Twitch EventSub reference page (event objects). The notification examples on the subscription types page are NOT the authority: they are wrong for some events (automod settings update wraps the event in data[], automod message update has a top-level fragments object).

Source of the fixtures: `server/src/NomNomzBot.Infrastructure/DevPlatform/EventSamplePayloads.cs` (dictionary `ByWireName`).

Pinned by: `server/tests/NomNomzBot.Infrastructure.Tests/DevPlatform/EventSampleFieldCoverageTests.cs`. Its table is built from the reference field tables. Three checks: every fixture has a table; every required path is present; no fixture path is absent from the table.

Method: each fixture is matched by subscription type and version to its event table. Shared objects (such as `message`, `badges`, `bits_voting`) are expanded inline. Paths are dotted and arrays are transparent. A child path counts as required only when its parent object exists in the fixture: variant objects Twitch does not use are sent as null, so their children are not demanded. A field is optional when the table says optional, null, or only-when.

Line format: `wire name` | Twitch subscription type | version | status | event table anchor.

- `chat.cleared` | channel.chat.clear | v1 | matches | #channel-chat-clear-event
- `chat.message` | channel.chat.message | v1 | matches ; optional fields not in the fixture (variant or end-only): is_source_only, message.fragments.gif | #channel-chat-message-event
- `chat.message.deleted` | channel.chat.message_delete | v1 | matches | #channel-chat-message-delete-event
- `chat.notification` | channel.chat.notification | v1 | fixed: added resub.sub_tier, resub.is_prime; removed chatter_user_login, resub.sub_plan ; optional fields not in the fixture (variant or end-only): message.fragments.cheermote, message.fragments.emote, message.fragments.mention | #channel-chat-notification-event
- `chat.settings.updated` | channel.chat_settings.update | v1 | matches | #channel-chat-settings-update-event
- `chat.shared.chat.began` | channel.shared_chat.begin | v1 | matches | #channel-shared-chat-session-begin-event
- `chat.shared.chat.ended` | channel.shared_chat.end | v1 | matches | #channel-shared-chat-session-end-event
- `chat.shared.chat.updated` | channel.shared_chat.update | v1 | matches | #channel-shared-chat-session-update-event
- `chat.user.message.held` | channel.chat.user_message_hold | v1 | fixed: removed message.fragments[].type, .mention | #channel-chat-user-message-hold-event
- `chat.user.message.updated` | channel.chat.user_message_update | v1 | fixed: removed message.fragments[].type, .mention | #channel-chat-user-message-update-event
- `chat.user.messages.cleared` | channel.chat.clear_user_messages | v1 | matches | #channel-chat-clear-user-messages-event
- `chat.whisper.received` | user.whisper.message | v1 | matches | #whisper-received-event
- `community.charity.campaign.progress` | channel.charity_campaign.progress | v1 | matches | #charity-campaign-progress-event
- `community.charity.campaign.started` | channel.charity_campaign.start | v1 | fixed: removed broadcaster_user_id (the table uses broadcaster_id) | #charity-campaign-start-event
- `community.charity.campaign.stopped` | channel.charity_campaign.stop | v1 | matches | #charity-campaign-stop-event
- `community.charity.donation` | channel.charity_campaign.donate | v1 | matches | #charity-donation-event
- `community.follow` | channel.follow | v1 | matches | #channel-follow-event
- `community.goal.began` | channel.goal.begin | v1 | matches ; optional fields not in the fixture (variant or end-only): ended_at, is_achieved | #goals-event
- `community.goal.ended` | channel.goal.end | v1 | matches | #goals-event
- `community.goal.progress` | channel.goal.progress | v1 | matches ; optional fields not in the fixture (variant or end-only): ended_at, is_achieved | #goals-event
- `community.hype.train.began` | channel.hype_train.begin | v2 | matches | #hype-train-begin-event
- `community.hype.train.ended` | channel.hype_train.end | v2 | matches | #hype-train-end-event
- `community.hype.train.progress` | channel.hype_train.progress | v2 | matches | #hype-train-progress-event
- `community.poll.began` | channel.poll.begin | v1 | matches | #channel-poll-begin-event
- `community.poll.ended` | channel.poll.end | v1 | matches | #channel-poll-end-event
- `community.poll.progress` | channel.poll.progress | v1 | matches | #channel-poll-progress-event
- `community.prediction.began` | channel.prediction.begin | v1 | fixed: added outcomes[].top_predictors (empty before any prediction) | #channel-prediction-begin-event
- `community.prediction.ended` | channel.prediction.end | v1 | matches | #channel-prediction-end-event
- `community.prediction.locked` | channel.prediction.lock | v1 | matches | #channel-prediction-lock-event
- `community.prediction.progress` | channel.prediction.progress | v1 | matches | #channel-prediction-progress-event
- `identity.user.updated` | user.update | v1 | matches | #user-update-event
- `moderation.action.taken` | channel.moderate | v2 | fixed: added source_broadcaster_user_id, source_broadcaster_user_login, source_broadcaster_user_name (null outside shared chat) ; optional fields not in the fixture (variant or end-only): warn | #channel-moderate-event-v2
- `moderation.auto.mod.message.held` | automod.message.hold | v1 | fixed: added category, level; removed automod{category, level, boundaries}, blocked_term, reason, message.fragments[].type | #automod-message-hold-event
- `moderation.auto.mod.message.updated` | automod.message.update | v1 | fixed: removed top-level fragments{emotes, cheermotes}; message.fragments is now an array of {text, emote, cheermote} | #automod-message-update-event
- `moderation.auto.mod.settings.updated` | automod.settings.update | v1 | fixed: removed the data[] wrapper (the event is flat) | #automod-settings-update-event
- `moderation.auto.mod.terms.updated` | automod.terms.update | v1 | matches | #automod-terms-update-event
- `moderation.moderator.added` | channel.moderator.add | v1 | matches | #channel-moderator-add-event
- `moderation.moderator.removed` | channel.moderator.remove | v1 | matches | #channel-moderator-remove-event
- `moderation.shield.mode.began` | channel.shield_mode.begin | v1 | matches ; optional fields not in the fixture (variant or end-only): ended_at | #shield-mode
- `moderation.shield.mode.ended` | channel.shield_mode.end | v1 | matches ; optional fields not in the fixture (variant or end-only): started_at | #shield-mode
- `moderation.suspicious.user.message` | channel.suspicious_user.message | v1 | matches | #channel-suspicious-user-message-event
- `moderation.suspicious.user.updated` | channel.suspicious_user.update | v1 | matches | #channel-suspicious-user-update-event
- `moderation.unban.request.created` | channel.unban_request.create | v1 | matches | #channel-unban-request-create-event
- `moderation.unban.request.resolved` | channel.unban_request.resolve | v1 | fixed: added moderator_id, moderator_login, moderator_name; removed moderator_user_id, moderator_user_login, moderator_user_name | #channel-unban-request-resolve-event
- `moderation.user.banned` | channel.ban | v1 | matches | #channel-ban-event
- `moderation.user.timed.out` | channel.ban | v1 | matches | #channel-ban-event
- `moderation.user.unbanned` | channel.unban | v1 | matches | #channel-unban-event
- `moderation.vip.added` | channel.vip.add | v1 | matches | #channel-vip-add-event
- `moderation.vip.removed` | channel.vip.remove | v1 | matches | #channel-vip-remove-event
- `moderation.warning.acknowledged` | channel.warning.acknowledge | v1 | matches | #channel-warning-acknowledge-event
- `moderation.warning.sent` | channel.warning.send | v1 | matches | #channel-warning-send-event
- `rewards.automatic.reward.redeemed` | channel.channel_points_automatic_reward_redemption.add | v2 | matches | #channel-points-automatic-reward-redemption-add-v2-event
- `rewards.bits.used` | channel.bits.use | v1 | matches | #channel-bits-use-event
- `rewards.cheer` | channel.cheer | v1 | matches | #channel-cheer-event
- `rewards.custom.power.up.redeemed` | channel.custom_power_up_redemption.add | v1 | matches | #channel-custom-power-up-redemption-add-event
- `rewards.gift.subscription` | channel.subscription.gift | v1 | matches | #channel-subscription-gift-event
- `rewards.new.subscription` | channel.subscribe | v1 | matches | #channel-subscribe-event
- `rewards.resubscription` | channel.subscription.message | v1 | matches | #channel-subscription-message-event
- `rewards.reward.created` | channel.channel_points_custom_reward.add | v1 | matches | #channel-points-custom-reward-add-event
- `rewards.reward.redeemed` | channel.channel_points_custom_reward_redemption.add | v1 | matches | #channel-points-custom-reward-redemption-add-event
- `rewards.reward.redemption.updated` | channel.channel_points_custom_reward_redemption.update | v1 | matches | #channel-points-custom-reward-redemption-update-event
- `rewards.reward.removed` | channel.channel_points_custom_reward.remove | v1 | matches | #channel-points-custom-reward-remove-event
- `rewards.reward.updated` | channel.channel_points_custom_reward.update | v1 | matches | #channel-points-custom-reward-update-event
- `rewards.subscription.ended` | channel.subscription.end | v1 | matches | #channel-subscription-end-event
- `rewards.watch.streak.received` | channel.chat.notification | v1 | fixed: removed chatter_user_login (shares the chat.notification table) | #channel-chat-notification-event
- `stream.ad.break.began` | channel.ad_break.begin | v1 | matches | #channel-ad-break-begin-event
- `stream.channel.updated` | channel.update | v1 | matches | #channel-update-event
- `stream.guest.star.guest.updated` | channel.guest_star_guest.update | vbeta | fixed: added host_user_id, host_user_login, host_user_name | #channel-guest-star-guest-update-event
- `stream.guest.star.session.began` | channel.guest_star_session.begin | vbeta | fixed: removed moderator_user_id, moderator_user_login, moderator_user_name | #channel-guest-star-session-begin-event
- `stream.guest.star.session.ended` | channel.guest_star_session.end | vbeta | fixed: added host_user_id, host_user_login, host_user_name; removed moderator_user_id, moderator_user_login, moderator_user_name | #channel-guest-star-session-end-event
- `stream.guest.star.settings.updated` | channel.guest_star_settings.update | vbeta | matches | #channel-guest-star-settings-update-event
- `stream.offline` | stream.offline | v1 | matches | #stream-offline-event
- `stream.online` | stream.online | v1 | matches | #stream-online-event
- `stream.raid` | channel.raid | v1 | matches | #channel-raid-event
- `stream.shoutout.received` | channel.shoutout.receive | v1 | matches | #shoutout-received
- `stream.shoutout.sent` | channel.shoutout.create | v1 | matches | #shoutout-create
