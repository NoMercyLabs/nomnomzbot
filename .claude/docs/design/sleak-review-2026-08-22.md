# Sleak review — dashboard (rendered, dev.nomnomz.bot, 2026-08-22)

Judged on rendered pixels (owner's Chrome, 1920×963, dark theme, Dutch locale), authenticated as the
owner. Screens walked: landing, Dashboard/Home, Commands (+ "Nieuw commando" dialog), Chat,
Moderation, Settings. Six of ~42 routes — the shell, the card/list/dialog primitives and the
form pattern repeat everywhere, so the findings below are systemic, not per-screen; a second pass
over Overlays / Economy / TTS / Integrations would add instances, not categories.

## Context
- Product: multi-platform bot management dashboard; audience = streamers, moderators and viewers —
  streamers and moderators live in it for hours; dense data, frequent small edits.
- Constraints: shadcn/ui (new-york) ported 1:1 to Compose; neutral base; accent derived at runtime
  from the user's Twitch chat colour (here: pure red `#FF0000`-ish → a deep maroon tint).
  `DesignSystemStyleGuardTest` forbids raw hex/dp in feature screens.

## Findings

### Color — intentional accent use (core rule 3)
- **High** Accent hierarchy is inverted. On Commands every row's enable toggle is full-accent (10+
  red pills per screen) while the primary action of the screen ("+ Nieuw commando") and the dialog's
  primary ("Aanmaken") are neutral grey. Same on Chat ("Versturen" grey), Moderation ("Toevoegen"
  grey), Settings ("Opslaan" grey). The eye is pulled to 10 toggles and away from the one task.
  → Toggles use the neutral "on" state (as Settings' auto-join toggle already does — the two
  screens disagree today); the single primary CTA per screen gets the accent.
- **High** Chat usernames render the raw Twitch chat colour at full saturation on the near-black
  ground (`Stoney_Eagle` in pure red, `NoMercyBot_` in pure red). Readable but glaring, and the same
  red as the accent, so usernames compete with selection/active states. (Also in the widget audit
  §2: no contrast adjustment anywhere.) → Clamp chat colours to a minimum contrast + cap chroma in
  dark/light themes (the same helper the chat widget needs).
- **Medium** The runtime accent derived from a pure-red chat colour lands on a maroon that reads as
  "destructive" in a dark UI: the active sidebar item and the active tab look like error states.
  → Derive the accent with a lightness/chroma floor so a saturated chat colour yields a usable
  accent, and keep destructive red a distinct, reserved token.

### Layout & spacing / hierarchy
- **High** Home: eight identical stat tiles in a 4×2 grid with equal weight — viewers, followers,
  subs, chatters, donations, commands, messages, uptime. No primary number. "Uptime" shows "—" and
  "Streamt naar: Offline" duplicates the status row above. → One hero tile (live state + viewers),
  secondary stats as a compact row; hide/zero-state tiles that have no value while offline.
- **High** Home "Recente activiteit" is the same line twelve times ("Stoney_Eagle loste
  Text-to-Speech Message in"), each with a date but no time, no redemption text, no grouping. Full
  column height spent on zero information. → Collapse consecutive identical events ("×12, last
  21:14"), show the relevant payload (redeemed text / amount), time-of-day not date.
- **High** Forms stretch to the full 1600 px content width: Settings inputs (prefix "!", language
  "en", timezone) are one-character values in 1600 px boxes; Moderation "Term toevoegen" likewise.
  Line-length and scan cost both suffer. → Cap form width (~640–720 px) or use a two-column
  label/control grid; short-value fields (prefix, locale) get intrinsic width.
- **Medium** Settings is one long scroll of equal cards ("Weergave", "Botbasis",
  "Botpersoonlijkheid", …) with a section eyebrow pattern repeated per card (title + one-line
  subtitle every time). Spec (`frontend-ia.md`) calls for tabs. → Tabs, and drop the repeated
  subtitle where the title is self-explanatory.
- **Medium** Commands list: no column structure — name/description left, three equally weighted
  controls (toggle, edit, delete) far right with ~1100 px of dead space between. Delete is as
  prominent as edit. → Put the toggle next to the name, edit/delete behind a row menu or on hover;
  show usage count / cooldown / permission as quiet metadata.
- **Medium** Moderation: the page opens with a grey banner "Schildmodus is hier niet beschikbaar…"
  (shield unavailable — for the owner). A permission-error banner as the first thing on a page is
  a UX finding as much as a backend one (see usability plan B3). → Either fix the cause or render it
  as an inline disabled control with the reason, not a page-wide banner.

### Components / UX states
- **High** Destructive actions without confirmation or weight: Moderation shows nine plain-text
  "Verwijderen" buttons; Commands shows a red trash icon per row. Sleak + house rule: destructive =
  confirm + reason where it changes external state. → Confirm dialog for delete; demote to a row
  overflow menu.
- **Medium** "Willekeurige antwoorden" toggle in the command dialog is a tiny neutral switch that
  reveals a second text field; "+ Willekeurig antwoord" is a text link with no affordance — the
  feature hiding behind it (random responses) is one of the most used. → Segmented control
  "Eén antwoord / Willekeurig" + a visible list.
- **Medium** Raw-text where a control belongs (pixel-confirmed): Settings "Standaardtaal" = free
  text "en" with helper "BCP-47-code"; "Tijdzone" = free text IANA name. → Dropdown of supported
  locales (en/nl only exist), timezone picker with search. (Catalogued already in the usability plan
  B6; surfaced here because it is visible on the very first settings card.)
- **Low** Chat "Chatmodi" row: four outline pills (slow / subs-only / emotes-only / followers-only)
  with no on/off visual distinction at rest — a mode that is ON looks identical to one that is OFF
  until hovered. → Filled state for active modes + the duration value inline ("Slow · 30s").
- **Low** Sidebar is 40+ items in eight collapsible groups; active item + group header both use the
  accent tint. Fine per Sleak (one focal point) but the profile block at the bottom duplicates the
  channel switcher at the top (same avatar, same name). → One identity block.

### Typography & contrast
- **Medium** Secondary text (descriptions under command names, helper lines under inputs, "Je kanaal
  in één oogopslag") is a mid-grey on `#0b0b0b`; at 13 px it sits around the WCAG AA floor — check
  APCA on the rendered pixels before sign-off (Sleak contrast rule). → Raise `muted-foreground` one
  step for 13 px text, or bump helper text to 14 px.
- **Low** Title sizes are consistent (28 px page title, 16 px card title); good. Dialog title and
  labels are well weighted.

### Concentric radius (core rule 1)
- **Low** Visually: cards ≈ 8 px, inputs inside cards ≈ 6 px with 16 px padding → should be 0 or the
  inner radius should drive (8 − 16 < 0 ⇒ inner elements should be square-ish or the card radius
  larger). Dialog ≈ 10 px with 24 px padding and 6 px inputs is in the same situation. Not wrong
  enough to read as broken, but not concentric. → Decide once in tokens: card 12 / padding 16 →
  inner 4 (or inner 8 → card 24 when padding 16), and apply to dialog/sheet/list rows.

### Motion
- Not assessed on pixels (no interaction recording); Compose defaults only. No finding.

### Copy (action-first)
- **Low** Mixed Dutch/English in the same list: command descriptions are English ("How old the
  caller's Twitch account is") under Dutch chrome — these are user data, acceptable; but the bot's
  own reply copy in chat is English while the UI is Dutch (personality tone has no locale). → Tone
  catalogue per locale (ties to the `Channel.Language` finding — it is written, never read).

## Applied changes
None — review only, per the "stabilize first, don't add" directive. Every item above is queued:
colour/hierarchy/destructive-confirm/form-width items → `SHORTCOMINGS-EXECUTION-PLAN.md` Tier 5
"Sleak pass"; the picker/raw-text items are already in Tier 3; the Home activity feed and Settings
tabs are in Tier 4.9 / B6.

## Remaining notes
- Six screens rendered; the rest of the 42 routes share the same primitives. A focused second pass
  should render: Overlays (widget rows + settings form), Economy (catalog form), TTS, Integrations
  cards, Pipelines step dialog — the screens where the catalogue/form work of Tier 3 lands.
- APCA/contrast numbers were not measured (canvas-rendered Compose; no DOM to sample). Do it with a
  pixel sampler on the screenshots in `scratchpad/sleak-*.png` before the colour token change ships.
- Evidence: the Chat screen shows the A4 voice bug live ("No voice matched 'set AnaNeural'" /
  "en-US-AnaNeural") — the audit finding reproduced on the owner's channel.
