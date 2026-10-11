# Page drawings — the new dashboard layout (2026-10-11)

Wireframes of the most important pages in the new layout. They draw what
`ui-restructure-research-2026-10-11.md` describes (sections 3 to 7). The paper wins when the two differ.
Status: part of S-IA-SPEC. The owner gave his yes on 2026-10-11 (see the psychology check, section 10).

## Legend

| Mark | Meaning |
|---|---|
| `[[ Save ]]` | Primary button: solid accent. One per group, and the accent is scarce on the page (Sleak rule 3). |
| `[ Edit ]` | Secondary button: outline. |
| `( More )` | Ghost button. |
| `{ Ban }` | Destructive button: its own red treatment, never the primary. |
| `*` | Asks for a second tap before it runs. |
| `E` | Needs the Editor role (the risky items, owner 2026-10-11). |
| `(o)` / `( )` | Toggle on / off. |
| `v` / `>` | Group open / closed. |

The drawings show layout and hierarchy only. Colours, radius and spacing come from the design system
(`spec/frontend-design-system.md`). Nested boxes follow the concentric radius rule (Sleak rule 1).

## 1. The shell and Home, live (broadcaster, wide screen)

The most used screen while on air (rank 3 for the broadcaster, section 5.2). The attention inbox holds
the one primary action, because it is the thing that needs a human now.

```
+--------------------+---------------------------------------------------------------+
| NomNomz  [chan  v] | Home   LIVE 1:42:10   Twitch ok  Kick ok  YouTube !  Ctrl+K   |
|--------------------|---------------------------------------------------------------|
| PINNED             | +-- ! YouTube dropping ---- ( Details ) + +-- Chat -------+   |
|  Home              | +-- Needs you (3) ----------------------+ | viewer1: hi   |   |
|  Chat              | | 2 held messages                       | | viewer2: !sr  |   |
|  Deck              | | 1 unban request                       | |   never gonna |   |
|  Commands          | |                                       | | viewer3: gg   |   |
|  Event Responses   | |                  [[ Open the queue ]] | | viewer4: lol  |   |
|  Overlays          | +---------------------------------------+ | viewer5: hype |   |
|  People            | +-- Deck: first 8 ------------- ( All ) + | viewer6: o7   |   |
|  Music             | | [Scene: Game] [Shield (o)] [Hydrate ] | | viewer7: :)   |   |
|                    | | [Clip      ] [Raid *E  ] [Ad 60s *E]  | |               |   |
| LIVE            v  | | [Poll      ] [Skip song ]             | |               |   |
|  Chat  Deck        | +---------------------------------------+ |               |   |
|  Stream  OBS       | +-- Activity ---------------------------+ |               |   |
|  VTube Studio      | | follow  name       sub   name x3      | | [ Message   ] |   |
|  Analytics         | | raid    name 40                       | | [ Send ]      |   |
| BUILD           >  | +---------------------------------------+ +---------------+   |
| PEOPLE          >  |                                                               |
| LOYALTY         >  |                                                               |
| MUSIC & VOICE   >  |                                                               |
| CONNECT         >  |                                                               |
|--------------------|                                                               |
| SETUP           >  |                                                               |
+--------------------+---------------------------------------------------------------+
```

- Stream health sits in the header, one word per platform. A problem opens a red card at the top of the
  left column. A normal state takes no space: the eye goes to what is wrong.
- The attention inbox is the one primary. When nothing waits, it shrinks to one line, "Nothing waits
  for you", with no button. The accent is never spent on an empty card.
- Chat is a full-height column on the right. Chat is read as a stream of lines; a short box shows too few.
- Deck tiles are neutral. A tile shows its live state (Shield on, the current scene).
- A Moderator sees only the tiles at or below their role. Raid and Ad are hidden from a Moderator.

## 2. Home, prep (broadcaster, offline)

Rank 1 for the broadcaster when offline. The same Stream info component as Live, Stream.

```
+--------------------+---------------------------------------------------------------+
| (sidebar as in 1)  | Home                        OFFLINE          Ctrl+K   (avatar) |
|                    |---------------------------------------------------------------|
|                    | +-- Next stream ------------------------------------------+   |
|                    | | Title     [ Chill dev night, building the bot        ]  |   |
|                    | | Category  [ Software and Game Development       v ]     |   |
|                    | | Tags      [ dev ] [ csharp ] [ + ]                      |   |
|                    | | Platforms  (o) All   ( ) per platform                   |   |
|                    | | Preset    [ Dev night  v ]   ( Save as preset )         |   |
|                    | |                                  [[ Save to all ]]      |   |
|                    | +---------------------------------------------------------+   |
|                    | +-- Schedule ---------------+ +-- Before you go live -------+ |
|                    | | Tue 20:00  Dev night      | | ok  Bot is moderator        | |
|                    | | Thu 20:00  Games          | | ok  Overlays connected      | |
|                    | | [ Open schedule ]         | | !   OBS not connected       | |
|                    | +---------------------------+ |     [ Connect OBS ]         | |
|                    |                               +-----------------------------+ |
+--------------------+---------------------------------------------------------------+
```

## 3. Live, Deck (the shenanigans board)

Everything on stream, one tap away (section 4). Each tile binds to one action from the action catalogue.

```
+--------------------+---------------------------------------------------------------+
| (sidebar)          | Deck                      [ Edit ]  ( Pop out )   Ctrl+K       |
|                    |---------------------------------------------------------------|
|                    | Scenes                                                        |
|                    | [ Starting ] [ Game      ] [ Just chat ] [ BRB       ]        |
|                    | Chat                                                          |
|                    | [ Shield (o)] [ Emote only] [ Slow 10s  ] [ Followers ]       |
|                    | Fun                                                           |
|                    | [ Airhorn  ] [ Hydrate TTS] [ Confetti  ] [ Drop game ]       |
|                    | Stream                                                        |
|                    | [ Poll: Next] [ Clip      ] [ Marker    ] [ Raid *E  ]        |
|                    | Music                                                         |
|                    | [ Skip song] [ Pause     ] [ Clear TTS ] [ +         ]         |
+--------------------+---------------------------------------------------------------+

Edit mode: the same grid, each tile opens a side panel:
  Action  [ Run a pipeline  v ]   Target [ Confetti burst  v ]
  Label   [ Confetti ]   Icon [ pick ]   Who sees it [ Moderator  v ]
  Second tap  ( ) off                    [[ Save tile ]]   ( Cancel )
```

- No page has a primary button on the grid itself: every tile is equal by design. Edit holds the primary.

## 4. Live, Chat with the viewer card (moderator)

Rank 1 for a moderator. The viewer card opens in a side panel, so chat stays visible.

```
+--------------------+------------------------------------+--------------------------+
| (sidebar)          | Chat  [ One channel | All mine ]   | viewer2           ( x )  |
|                    | [All] [Twitch] [Kick] [YouTube]  Shield (o) | Twitch viewer2  |
|                    |------------------------------------| Kick   viewer_2          |
|                    | viewer1: hi                        | Follows 2 years          |
|                    | > viewer2: buy followers at ...    | Points  1 240            |
|                    | viewer3: gg                        |--------------------------|
|                    | viewer4: !sr song                  | History                  |
|                    |                                    |  timeout 10m  by mod1    |
|                    |                                    | Notes                    |
|                    |                                    |  [ Add a note ...    ]   |
|                    |                                    |--------------------------|
|                    | [ Type a message ...    ] [ Send ] | ( Warn ) [ Timeout v ]   |
|                    |                                    | { Ban }                  |
+--------------------+------------------------------------+--------------------------+
```

- "All mine" shows every channel the moderator moderates, one stream with a channel label.
- A platform action the platform lacks is disabled with the reason ("Not on Kick"), never hidden.

## 5. People, Queue (one queue for everything that waits for a moderator)

Rank 2 for a moderator. List on the left, the selected item on the right.

```
+--------------------+---------------------------------------------------------------+
| (sidebar)          | People   Overview  [Queue 4]  Viewers  History  Rules  Roles  |
|                    |---------------------------------------------------------------|
|                    | [All 4] [Held 2] [Reports 1] [Appeals 0] [Unban 1]            |
|                    | +-- List ----------------------+ +-- Selected ---------------+ |
|                    | | > Held   viewer7   Twitch    | | Held message, Twitch       | |
|                    | |   Held   viewer9   Kick      | | viewer7: free nitro at ... | |
|                    | |   Report viewer2   YouTube   | | Why held: link, new account| |
|                    | |   Unban  viewer5   Twitch    | | Earlier: none              | |
|                    | |                              | |                            | |
|                    | |                              | | [[ Allow ]]  ( Deny )      | |
|                    | |                              | | { Deny and ban }           | |
|                    | +------------------------------+ +----------------------------+ |
+--------------------+---------------------------------------------------------------+
```

- Rows carry no buttons. The accent appears once: on the selected item.

## 6. Build, Commands (list and editor)

Rank 2 for the broadcaster when offline, rank 1 for an Editor.

```
+--------------------+---------------------------------------------------------------+
| (sidebar)          | Commands   [Custom]  Built-in        [ Search ]  [[ New v ]]  |
|                    |---------------------------------------------------------------|
|                    | +-- Name ----+-- Reply / action ---------+-- Who --+-- On --+ |
|                    | | !discord   | Join us at ...            | Everyone | (o)    | |
|                    | | !lurk      | {name} is lurking         | Everyone | (o)    | |
|                    | | !so        | pipeline: Shoutout        | Moderator| (o)    | |
|                    | +------------+---------------------------+----------+--------+ |
|                    |                                                               |
| New v opens:       |   Start from a template | Start empty                         |
+--------------------+---------------------------------------------------------------+

Editor (side panel or full page on a phone):
  Name     [ !lurk ]            Aliases [ !afk ]
  Reply    [ {name} is lurking, see you soon ]
  Who can use it  [ Everyone v ]      Cooldown [ 10 s ]
  Before you save: this command replies in 3 connected chats (Twitch, Kick, YouTube).
                                    ( Cancel )  [[ Save command ]]
```

## 7. Live, Stream, Info & presets (more than one platform)

```
+--------------------+---------------------------------------------------------------+
| (sidebar)          | Stream   [Info & presets]  Interactions  Schedule             |
|                    |---------------------------------------------------------------|
|                    | Edit  (o) All platforms   ( ) One platform                    |
|                    | +-- Twitch -------------------+ +-- Kick ------------------+ |
|                    | | Title    [ Chill dev ... ]  | | Title    [ Chill dev ...]| |
|                    | | Category [ Software ... v]  | | Category [ Software .. v]| |
|                    | | Tags     [ dev ] [ + ]      | | Tags     Not on Kick     | |
|                    | | Labels E [ none  v ]        | |                          | |
|                    | +-----------------------------+ +--------------------------+ |
|                    |                                       [[ Save to all ]]       |
+--------------------+---------------------------------------------------------------+
```

- Content labels and branded content are Editor only: a Moderator sees them disabled, with the reason.

## 8. Phone (under 600 dp)

```
+--------------------------+        +--------------------------+
| Home       LIVE  (avatar)|        | Deck                 ( x )|
|--------------------------|        |--------------------------|
| Needs you (3)            |        | [ Game     ] [ Shield (o)]|
| [[ Open the queue ]]     |        | [ Hydrate  ] [ Clip      ]|
|--------------------------|        | [ Airhorn  ] [ Confetti  ]|
| Twitch ok   812 view     |        | [ Poll     ] [ Skip song ]|
| Deck: first 4            |        | [ Raid *E  ] [ Ad *E     ]|
| [ Game ] [ Shield ]      |        |                          |
| [ Clip ] [ Skip  ]       |        |                          |
|--------------------------|        |                          |
| Home  Chat  Deck  People |        |                          |
|                  ( More )|        |                          |
+--------------------------+        +--------------------------+
```

- The bottom bar holds 4 role-pinned items and More. More opens the full sidebar as a sheet.

## 9. Viewer: My channels and a channel

The viewer shell is separate from the operator shell (section 5.3).

```
+---------------------------------------------------------------------------------+
| NomNomz   My channels                                         Ctrl+K   (avatar)  |
|---------------------------------------------------------------------------------|
| +-- stoney_eagle ---------+ +-- friend_streamer -------+ +-- other -----------+ |
| | LIVE  Chill dev night   | | offline                  | | offline            | |
| | 1 240 points            | | 300 points               | | 12 points          | |
| | [[ Open ]]              | | [ Open ]                 | | [ Open ]           | |
| +-------------------------+ +--------------------------+ +--------------------+ |
+---------------------------------------------------------------------------------+

stoney_eagle:   Now playing | Leaderboards | Points & store | Games | Me
  Now playing  Never Gonna Give You Up         [[ Request a song ]]
  Me           TTS voice [ Brian v ]  Pronouns [ he/him v ]  Linked: Twitch, Kick
```

- The live channel gets the primary Open. The others are outline.

## 10. Dashboard psychology check (2026-10-11)

Owner 2026-10-11, verbatim: "if you are sure that this matches human psychology for dashboards then you have my clearance to execute".
Arc's reading: check the layout against known rules of how people read a dashboard, fix each gap,
and then execute the plan.

| Rule | Where the layout applies it | Result |
|------|-----------------------------|--------|
| Fewer choices give a faster choice (Hick's law) | 8 pins; one group open at a time; Setup closed at the bottom; Ctrl+K for the rest | applied |
| Recognize, do not recall | Page names in the sidebar; the palette finds pages, tabs and actions by name; role names, never numbers | applied |
| The first look goes to the top left (F-pattern) | Home Live puts a health problem, then Needs you, at the top left | fixed in this check: the health box sat there even when all was ok |
| Show the exception, keep the normal state small | Health is one word per platform in the header; a red card only on a problem; an empty inbox shrinks to one line | fixed in this check |
| One focal point (Sleak rule 3, isolation effect) | One accent per screen: the inbox on Home Live, Save on Prep, Edit on the Deck | applied |
| A familiar layout is faster (Jakob's law) | The Deck works like Quick Actions, SAMMI and Lumia; one queue like Mod View; group names in streamer words | applied |
| Chat is read as a stream of lines | Chat is a full-height column on Home Live | fixed in this check: chat was a short box at the bottom |
| Big, near targets when in a hurry (Fitts's law) | Large Deck tiles; a pop-out for a second monitor; the phone bar in thumb reach | applied |
| A screen fits the task of the moment | Home changes with the stream state (Prep, Live) and with the role | applied |
| Prevent the costly error | A second tap on risky tiles; the consequence shown before Save; risky items at Editor | applied |

Not proven yet: no person has used this layout. These are known rules, not a test with users.
The slice S-IA-USABILITY closes that gap after S-IA-NAV and S-IA-ROLE-HOMES.
