# Spec Readiness Report

All subsystem specs are **decision-complete and implementable** — every previously tracked band
(engine blocking gaps, cross-cutting decisions, missing specs, the product-edge & ops band, and
the twelve cross-spec owner decisions) is resolved and verified. Resolved detail is removed, not
archived (history lives in git). **The live open-work list is `../SHORTCOMINGS-EXECUTION-PLAN.md`**;
`_GAP-AUDIT.md` is the authoritative readiness gate and supersedes this table. One row per spec doc
in `_INDEX.md` (regenerated 2026-08-22 against `../PRODUCT-ALIGNMENT.md` D1–D9).

| Spec doc | Ready? |
|---|---|
| `analytics.md` | Ready |
| `automation-api.md` | Ready (D6 expose-all with PII projection, attribute-driven catalog) |
| `backend-structure.md` (rulebook) | Ready |
| `broadcaster-liveops.md` | Ready |
| `chat-client.md` | Ready (multi-platform feed/composer per D1; client render items open in §10) |
| `chat-decoration.md` | Ready |
| `code-execution-sandbox.md` | Ready |
| `commands-pipelines.md` | Ready |
| `community-dashboard.md` | Ready (provider-fanned reads) |
| `custom-code.md` | Ready |
| `custom-events.md` | Ready |
| `deployment-distribution.md` (rulebook) | Ready |
| `dev-platform.md` | Ready (decided 2026-08-22: expose-all with PII projection) |
| `discord.md` | Ready |
| `economy.md` | Ready (one balance per human per channel) |
| `engagement.md` | Ready |
| `event-store.md` | Ready |
| `federation-oidc.md` | Ready |
| `figma-design-system-rules.md` (reference note) | Ready — non-canonical reference |
| `frontend.md` (client) | Ready |
| `frontend-data-layer.md` (client) | Ready |
| `frontend-design-system.md` (client) | Ready |
| `frontend-design-system.catalogue.md` (client) | Ready (13 catalogued components marked "to build") |
| `frontend-ia.md` (client) | Ready (42 shipped routes reconciled in §3b; participant rung per D4) |
| `frontend-structure.md` (client) | Ready |
| `gdpr-crypto.md` | Ready |
| `giveaways.md` | Ready |
| `identity-auth.md` | Ready (D2: any platform can be the first login) |
| `integrations-oauth.md` | Ready |
| `live-games.md` | Ready |
| `marketplace.md` | Ready |
| `media-share.md` | Ready |
| `moderation.md` | Ready |
| `monetization-billing.md` | Ready |
| `music-automation-controls.md` | Ready |
| `music-sr.md` | Ready |
| `obs-control.md` | Ready |
| `onboarding-setup.md` | Ready |
| `per-viewer-data.md` | Ready |
| `pipeline-control-flow.md` | Ready |
| `platform-conventions.md` | Ready |
| `platform-identity.md` | Ready (D1: one channel, many `PlatformConnection`s; D3: X is a sibling platform) |
| `pronouns.md` | Ready |
| `quotes.md` | Ready |
| `rewards.md` | Ready |
| `roles-permissions.md` | Ready |
| `rollout-updates.md` (rulebook) | Ready |
| `scaling-qos.md` | Ready |
| `sound-system.md` | Ready |
| `stream-admin.md` | Ready |
| `stream-deck.md` | Ready |
| `streamdeck-plugin.md` | Ready |
| `supporter-events.md` | Ready |
| `tts.md` | Ready |
| `twitch-eventsub.md` | Ready |
| `twitch-helix.md` | Ready |
| `vtube-studio.md` | Ready |
| `webhooks.md` | Ready |
| `widget-sdk.md` | Ready |
| `widgets-overlays.md` | Ready |

**Verdict: 60 specs — 60 Ready, 0 blocked, 0 needs-owner.**
