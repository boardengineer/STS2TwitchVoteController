# STS2 Twitch Vote Controller - Software Architecture

## Overview

A Slay the Spire 2 mod that lets Twitch chat vote on every game decision. Built on .NET 9.0/C# 13.0, it connects to Twitch IRC, presents available game actions as numbered options, collects votes for 10 seconds, and executes the winner via the RunReplays command system.

**Dependencies**: sts2.dll, GodotSharp.dll, 0Harmony.dll, RunReplays.dll

---

## Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────────────────────┐
│                        STS2 TWITCH VOTE CONTROLLER                              │
└─────────────────────────────────────────────────────────────────────────────────┘

  EXTERNAL                     CORE ENGINE                      GAME INTEGRATION
 ─────────                    ────────────                     ─────────────────

┌──────────────┐         ┌─────────────────────┐         ┌──────────────────────┐
│  Twitch IRC  │         │     Plugin.cs        │         │   STS2 Game Engine   │
│  (TCP:6667)  │◄───────►│   [ModInitializer]   │────────►│   (sts2.dll)         │
└──────┬───────┘         │                      │         │                      │
       │                 │  - Harmony patches    │         │  - RunManager        │
       ▼                 │  - State machine      │         │  - CombatManager     │
┌──────────────┐         │  - Event routing      │         │  - ModelDb           │
│ TwitchIrc    │         └──────────┬────────────┘         │  - Map/Event/Shop    │
│ Client.cs    │                    │                      └──────────┬───────────┘
│              │    OnMessage       │                                 │
│ - Connect    │───────────►┌──────┴──────────┐              ┌───────┴──────────┐
│ - Auth       │            │                 │              │  RunReplays.dll   │
│ - Reconnect  │            ▼                 ▼              │  (dependency)     │
│ - Parse IRC  │   ┌──────────────┐  ┌───────────────┐      │                   │
└──────────────┘   │ ChatCommands │  │ VoteExecu-    │      │ - ReplayCommand   │
                   │     .cs      │  │ tioner.cs     │◄────►│ - ReplayDispatcher│
┌──────────────┐   │              │  │               │      │ - InputRequired   │
│ TwitchConfig │   │ !card <name> │  │ - Start vote  │      │   signal          │
│    .cs       │   │ !relic <name>│  │ - Collect     │      │ - GetAvailable    │
│              │   │ !potion <nm> │  │   votes       │      │   Commands()      │
│ config.json  │   └──────────────┘  │ - Tally &     │      │ - Execute()       │
│ - channel    │                     │   execute     │      └───────────────────┘
│ - username   │                     │ - State flags │
│ - oauthToken │                     └───────┬───────┘
└──────────────┘                             │
                                             │ Refresh overlays
                              ┌──────────────┼──────────────────┐
                              │              │                  │
                              ▼              ▼                  ▼
                   ┌─────────────────────────────────────────────────────┐
                   │                  UI OVERLAY LAYER                    │
                   │              (Godot Label nodes)                     │
                   │                                                     │
                   │  ┌──────────────┐  ┌────────────────┐              │
                   │  │ CardVote     │  │ Selection      │              │
                   │  │ Overlay.cs   │  │ Overlay.cs     │              │
                   │  │              │  │                │              │
                   │  │ - Hand cards │  │ - Card rewards │              │
                   │  │ - Potions    │  │ - Rest site    │              │
                   │  │ - End turn   │  │ - Grid select  │              │
                   │  │ - Hand select│  │ - Confirm/back │              │
                   │  └──────────────┘  └────────────────┘              │
                   │                                                     │
                   │  ┌──────────────┐  ┌──────────────┐  ┌──────────┐ │
                   │  │ Event        │  │ Map          │  │ Shop     │ │
                   │  │ Overlay.cs   │  │ Overlay.cs   │  │ Overlay  │ │
                   │  │              │  │              │  │ .cs      │ │
                   │  │ - Event      │  │ - Map nodes  │  │ - Cards  │ │
                   │  │   choices    │  │ - Travel pts │  │ - Relics │ │
                   │  └──────────────┘  └──────────────┘  │ - Potions│ │
                   │                                       │ - Remove │ │
                   │  ┌──────────────┐  ┌──────────────┐  └──────────┘ │
                   │  │ Combat       │  │ Command      │               │
                   │  │ Overlay.cs   │  │ Describer.cs │               │
                   │  │              │  │              │               │
                   │  │ - Enemy      │  │ Command →    │               │
                   │  │   numbers    │  │ human text   │               │
                   │  └──────────────┘  └──────────────┘               │
                   └─────────────────────────────────────────────────────┘

                   ┌─────────────────────────────────────────────────────┐
                   │                  SUPPORT LAYER                       │
                   │                                                     │
                   │  ┌──────────────┐  ┌──────────────┐  ┌──────────┐ │
                   │  │ RunStart     │  │ Keyboard     │  │ DevConsole│ │
                   │  │ Helper.cs    │  │ Shortcuts.cs │  │ Logger.cs│ │
                   │  │              │  │              │  │          │ │
                   │  │ Activate     │  │ F5 → restart │  │ Thread-  │ │
                   │  │ replay mode  │  │ vote         │  │ safe log │ │
                   │  └──────────────┘  └──────────────┘  └──────────┘ │
                   └─────────────────────────────────────────────────────┘
```

---

## Vote Lifecycle

```
  RunReplays emits           Plugin filters         VoteExecutioner         Chat sends
  "InputRequired"       ──►  commands by state  ──► starts 10s vote    ──► "Vote now!
       signal                machine (shop,          timer & overlays       1: Play Strike
                             chest, map, etc.)                              2: Play Defend"
                                                          │
                                                          ▼
                                                    Viewers type
                                                    "1" or "2"
                                                          │
                                                          ▼
                                                    Timer expires →
                                                    tally votes →
                                                    execute winner
                                                    via RunReplays
                                                          │
                                                          ▼
                                                    Update state ──► next InputRequired
```

---

## State Machine (Plugin.cs)

```
         ┌──────────┐  MapMove   ┌───────────┐  OpenShop   ┌───────────┐
         │  Map     │──────────►│  Room      │───────────►│  Shop     │
         │  View    │◄──────────│  (combat,  │            │  Open     │
         └──────────┘  Proceed   │  event,   │◄───────────│           │
              ▲                  │  reward)  │  CloseShop  └───────────┘
              │                  └─────┬─────┘
              │                        │ OpenChest
              │                  ┌─────▼─────┐  TakeRelic  ┌───────────┐
              │                  │  Chest    │───────────►│  Relic    │
              └──────────────────│  Opened   │            │  Taken    │
                    Proceed      └───────────┘            └─────┬─────┘
                                                                │
                                                   Proceed ◄────┘
```

---

## Component Details

### Core Engine

| File | Role | Key Responsibilities |
|------|------|---------------------|
| **Plugin.cs** | Entry point & orchestrator | Mod initialization, Harmony patches, state machine, event routing |
| **TwitchIrcClient.cs** | Twitch connection | TCP/IRC protocol, auth, reconnect with backoff, message parsing |
| **TwitchConfig.cs** | Configuration | Loads channel/username/oauth from JSON config file |
| **VoteExecutioner.cs** | Vote engine | Collects votes, tallies, executes winner, manages 10s timer |
| **ChatCommands.cs** | Utility commands | `!card`, `!relic`, `!potion` lookups with fuzzy matching |
| **CommandDescriber.cs** | Text formatting | Converts ReplayCommand objects to human-readable vote options |

### UI Overlay Layer

| File | Role | Covers |
|------|------|--------|
| **CardVoteOverlay.cs** | Combat labels | Hand cards, potions, end turn button, hand selection |
| **SelectionOverlay.cs** | Selection labels | Card rewards, rest site, grid select, confirm/cancel |
| **EventOverlay.cs** | Event labels | Event choice buttons |
| **MapOverlay.cs** | Map labels | Travelable map nodes (+ Harmony patches for refresh) |
| **ShopOverlay.cs** | Shop labels | Merchant cards, relics, potions, card removal, open/close |
| **CombatOverlay.cs** | Enemy labels | Numbers enemies (1, 2, 3...) for targeting votes |

### Support Layer

| File | Role |
|------|------|
| **RunStartHelper.cs** | Activates replay mode on run start, hides RunReplays overlay |
| **KeyboardShortcuts.cs** | F5 hotkey to manually restart a vote (debug/testing) |
| **DevConsoleLogger.cs** | Thread-safe message queue for in-game dev console |

---

## Key Design Patterns

- **Harmony Patching** - Hooks into game methods at runtime without modifying source code
- **Reflection** - Accesses private game fields for UI node positions and internal state
- **Event-Driven** - IRC messages and RunReplays `InputRequired` signal drive the vote loop
- **State Machine** - Tracks shop/chest/map context to filter valid commands per vote round
- **Command Pattern** - RunReplays provides executable `ReplayCommand` objects, decoupling voting from game internals

---

## Data Flow

```
Twitch Chat
    │
    ▼
TwitchIrcClient (TCP/IRC)
    │
    ├──► ChatCommands.TryHandle()  ──► response to chat
    │
    └──► VoteExecutioner.OnChatMessage()  ──► store vote
                                                  │
RunReplays "InputRequired" signal                 │
    │                                             │
    ▼                                             │
Plugin.StartOrRestartVote()                       │
    │                                             │
    ├── GetAvailableCommands()                    │
    ├── Filter by state machine                   │
    ├── Sort via CommandDescriber                  │
    └── VoteExecutioner.StartVote(commands)        │
              │                                   │
              ├── Send options to chat             │
              ├── Start 10s timer                  │
              ├── Refresh overlays (0.1s loop) ◄───┘
              │
              ▼ (timer expires)
         OnVoteEnd()
              │
              ├── Tally votes (ties → lowest number wins)
              ├── winner.Execute() via RunReplays
              ├── Update state machine flags
              └── Next InputRequired triggers new vote
```
