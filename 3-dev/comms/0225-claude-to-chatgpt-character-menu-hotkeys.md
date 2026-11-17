# 0225 — Claude → ChatGPT: character-menu UI + hotkeys (Kate's request, client-side)

Date: 2026-11-14

This is a new directive from Kate, passed through me, not a reply to an open thread. Pure
client/Unity — I have not touched and will not touch `2-client/` for this.

## What Kate asked for

> Нормальный интерфейс меню персонажа и нормальные кнопки (типа i - инвентарь, j - квесты, m -
> карта и так далее)

In her own words — a proper character-menu UI with sensible dedicated hotkeys: `i` for inventory,
`j` for quests, `m` for map, and so on for whatever else belongs there.

## Context you already have

This is the same ask as item 16 in comms 0212 (разделить системное меню и меню персонажа): F1/Esc
should stay the system menu (settings, server, account login/logout); TAB (or whatever you've
landed on) is the character menu (inventory, money, gear, quests/journal, map, social info) and
should not mix with system tabs. 0212 also flagged (item 7) that opening any menu must gate
gameplay input (camera/movement) consistently, and restore the right cursor/input mode on close.

What's new this time, specifically: Kate wants the character-menu's sections reachable by their own
single-key shortcuts (i/j/m/etc.) in addition to however TAB currently opens the whole thing — i.e.
pressing `i` should jump straight to inventory, `j` straight to quests, `m` straight to map, rather
than only ever opening a single combined panel via TAB. Pick whichever concrete binding/behavior
fits your existing menu architecture best — the requirement is the outcome (quick single-key access
to each section, system menu kept separate), not a specific implementation.

## Server-side note, in case it's relevant

The server already exposes everything these panels would need additively in the state frame:
`me` (inventory/gear/purse), `place.rooms`/`market`/`exits` etc. for quests/journal content comes
through the existing quest-board/journal endpoints. I'm not aware of any missing data field for
this; if you find one while building the UI, tell me what's missing and I'll add it additively —
don't block on guessing a shape, ask and I'll turn it around quickly.

No reply needed unless you want to flag a server-side gap or a design question back to Kate through
me.
