# `fix/retail-compat` — retail-compatibility branch scope

This doc scopes the work on branch `fix/retail-compat`. Three concrete bugs, one shared constraint, and one bookkeeping deliverable (refreshing `hybrasyl-compat-matrix.md`).

## Context

The Chaos.Client must run cleanly against **two** servers:

1. **Hybrasyl** (`qa.hybrasyl.com:2610`) — our own server, full source under `e:/Dark Ages Dev/Repos/server`.
2. **Retail Dark Ages** (`da0.kru.com`) — third-party, **closed source**. We have packet bytes only — no logs, no builds, no source.

Hybrasyl has been reverse-engineered against retail for years, so its wire format is assumed to match retail in shape. When a feature breaks against **both** servers, the bug almost certainly lives in the **client**, not the server. All three issues below have that shape.

## Three retail-compat bugs in scope

### 1. Doors — N/S axis broken on retail (and possibly Hybrasyl)

Background investigation lives in `~/.claude/plans/on-our-recent-doors-snazzy-stonebraker.md` — carry it forward.

- **Symptom (confirmed by user 2026-04-28):** N/S-axis doors **never open** via tile click or right-click context-menu pathway, on `main` or `fix/retail-compat`. Affects all N/S panels regardless of whether they're center or side. E/W doors function normally on every server.
- **Status:** Investigation re-routed from server (the original `feature/doors` PR for Hybrasyl) to client. Three Chaos.Client commits are suspect:
  - `d74127b` feat(doors): regenerate DoorTable from audit, add Alt+right-click menu
  - `c0237ef` crash fix, door fix — sotp-strict pathfinder change ← prime suspect
  - `9bb61a6` fix(doors): dedup multi-tile doors; dismiss context menu on outside click / movement
- **Confirmed failing instances** (1-indexed in-game / 0-indexed map binary):
  - Rucesion (lod505) (40, 42), (37, 28) — center-only N/S 3-tile.
  - Piet (lod501) (29, 12) — all-change N/S 3-tile, **inverted panel order**.
  - Abel (lod502) (12, 16).
- **Critical files (client):**
  - `Chaos.Client/Definitions/DoorTable.cs`
  - `Chaos.Client/Controls/World/Popups/DoorContextMenu.cs`
  - The pathfinder code touched by `c0237ef` (sotp consultation)
  - `docs/doors.md` — source of truth, 81 hand-audited rows.

### 2. Group system — broken on Hybrasyl AND retail

Premise: per the Hybrasyl-matches-retail rule, this points client-side.

- **Server emissions (Hybrasyl, ground truth):**
  - `GroupServerPacketType` (0x63): `Ask=1, Member=2, RecruitInfo=4, RecruitAsk=5` — `server/hybrasyl/Subsystems/Players/Grouping/UserGroup.cs:380-386`
  - `GroupClientPacketType` (0x2E): `Request=2, Answer=3, RecruitInit=4, RecruitInfo=5, RecruitEnd=6, RecruitAsk=7` — same file, lines 370-378.
- **Client handler:** `Chaos.Client/Screens/WorldScreen.ServerHandlers.cs:638-695` `HandleGroupInviteReceived` switches on `ServerGroupSwitch` and only covers `Invite`, `RequestToJoin`, `ShowGroupBox`. **`Member` (value 2) is unhandled** — that's a real gap. After accept, the server's roster-update packet arrives and is silently dropped.
- **Wire-format suspicion (client→server):** Hybrasyl's `PacketHandler_0x2E_GroupRequest` in `server/hybrasyl/Servers/World.cs` reads the partner name then carries a `// TODO: currently leaving five bytes on the table here` comment — i.e. it expects 5 trailing bytes the Chaos.Client doesn't send. That mismatch could explain why Hybrasyl never advances the flow when the client initiates.
- **Existing reference:** `docs/hybrasyl-compat-matrix.md` row 0x63 — marked `DIVERGENT | UNINSPECTED`. §4.13's enum values are **stale** (don't match the server source above) and must be refreshed as part of this work.
- **Capture available:** the user can produce a retail packet capture of a working `0x2E GroupRequest` to disambiguate the 5-byte trailing payload. We should ask for that capture before guessing at the layout.

### 3. Boards — clicking a board-object does nothing on either server

- **Click pathway (client):** `Chaos.Client/Screens/WorldScreen.InputHandlers.cs:1196` — double-click on any non-Aisling, non-GroundItem entity calls `Game.Connection.ClickEntity(entity.Id)` and sends a generic click packet, **not** a board-specific request. The board-list flow that *does* work is the F7 hotkey at line 840-846, which calls `SendBoardInteraction(BoardRequestType.BoardList)`.
- **Hybrasyl side:** A clickable board world-object is a `Signpost` with `IsMessageboard=true`. On click, the server **directly emits** a `BoardResponseType` (mailbox or board index) response with `isClick=true` — it does *not* expect a `BoardRequest` packet first.
- **Hypothesis:** the click → server flow works fine. The client likely *receives* the unsolicited `0x31` board response but doesn't route it into the boards UI, because `Board.IsBoardListPending` is false (no outstanding request). The inbound 0x31 handler probably gates UI presentation on a pending-request flag and drops server-initiated opens.
- **Existing reference:** `docs/hybrasyl-compat-matrix.md` row 0x31 — `DIVERGENT | VERIFIED-COMPATIBLE` for **mail** (F7-driven). The world-object click path is the untested gap.
- **Test target:** `lod505` (Rucesion) board signpost at **(19, 3)**. Same map binary at `e:/games/dark ages/maps/lod505.map` (retail) and `f:/documents/hybrasyl/world/mapfiles/lod505.map` (Hybrasyl) — identical bytes. Lets us repro on both servers without map-data variance.
- **Critical files:**
  - `Chaos.Client/ViewModel/Board.cs`
  - `Chaos.Client.Networking/ConnectionManager.cs` — 0x31 inbound handler (locate via the `Handlers[(byte)ServerOpCode.DisplayBoard] = ...` registration)
  - `Chaos.Client/Screens/WorldScreen.Wiring.cs` — board UI wiring (around line 455+)
- **Server `BoardResponseType`** ground truth, `server/hybrasyl/Subsystems/Messaging/Enums.cs:28-38`:
  `DisplayList=1, GetMailboxIndex=2, GetBoardIndex=3, GetMailMessage=4, GetBoardMessage=5, EndResult=6, DeleteMessage=7, HighlightMessage=8`. The matrix §4.9 needs refreshing against these.

## Cross-cutting constraint: retail is a black box

No source, no logs, no builds for `da0.kru.com`. Investigation against retail is **packet-byte capture only**. Implications:

- For doors and boards: prove the fix works against Hybrasyl first (where we can read source and logs), then verify against retail by observing visible behavior (the click does the visible thing).
- For group: a retail packet capture is the cleanest way to nail the 5-byte trailing payload. The user can produce one — request it before guessing.

## Plan of attack

### Phase 1 — Doors

Continue from the existing doors plan. Read `c0237ef`, `9bb61a6`, `d74127b` in order. The strongest hypothesis is that `c0237ef` (sotp-strict pathfinder) blocks N/S door clicks because the open-state side panels of N/S doors carry sotp wall-bits that, post-fix, the pathfinder treats as non-walkable — preventing the click from registering as targeting a door. Identify the regression, write a regression test if a client harness exists (or manual repro via Rucesion (40,42) and Piet (29,12)), fix.

**Phase-1 review gate:** bug/regression review + architecture review of the door fix before moving on (per CLAUDE.md review policy).

### Phase 2 — Group

1. Read full client `0x2E` send paths in `Chaos.Client.Networking/ConnectionManager.cs`. Map every `ClientGroupSwitch` value → expected Hybrasyl `GroupClientPacketType` value.
2. Request the retail packet capture from the user. Decode the 5 trailing bytes Hybrasyl flags as unknown — they'll match retail's wire format.
3. Add the missing `Member` (value 2) case to `HandleGroupInviteReceived`. Find the Hybrasyl emission site for `GroupServerPacketType.Member` and decode its body to drive the roster-update path.
4. Test full lifecycle on each server: invite, accept, member-list update, recruit-box flow, leave/kick.

**Phase-2 review gate:** bug/regression + architecture review.

### Phase 3 — Boards

1. Locate the inbound 0x31 handler in `ConnectionManager.cs`. Trace dispatch on the sub-type byte.
2. Determine why an unsolicited (`isClick=true`) board response doesn't reach the UI. Most likely cause: a `IsBoardListPending` or session gate that drops responses without an outstanding request.
3. Fix the gate so server-initiated opens (signpost click) populate `Board.AvailableBoards` and open the board UI.
4. Test on Hybrasyl by clicking the lod505 (19, 3) signpost. Verify on retail at same coords. Regression-check F7 mail flow.

**Phase-3 review gate:** bug/regression + architecture review.

### Phase 4 — Bookkeeping: refresh `hybrasyl-compat-matrix.md`

Update §4.9 (0x31 board) and §4.13 (0x63 group) with current enum values and post-fix findings. Update the status table at the top: 0x31 → `VERIFIED-COMPATIBLE` across both mail and board-click paths if the fix works; 0x63 → `VERIFIED-COMPATIBLE` (or downgrade to `STILL-DIVERGENT` with notes if some subtypes still don't agree).

### Phase 5 — Final review

Comprehensive bug/regression + architecture review covering the whole branch. Project lead orchestrates per CLAUDE.md.

## Critical files at a glance

| Concern | Files |
|---|---|
| Doors (client) | `Chaos.Client/Definitions/DoorTable.cs`, `Chaos.Client/Controls/World/Popups/DoorContextMenu.cs`, pathfinder code touched by `c0237ef` |
| Doors (docs) | `docs/doors.md`, `docs/doors-modernization-direction.md` |
| Group (client) | `Chaos.Client/Screens/WorldScreen.ServerHandlers.cs:638`, `Chaos.Client.Networking/ConnectionManager.cs:878+` (group sends), `Chaos.Client/ViewModel/GroupState.cs`, `Chaos.Client/ViewModel/GroupInvite.cs` |
| Group (server) | `server/hybrasyl/Subsystems/Players/Grouping/UserGroup.cs`, `server/hybrasyl/Servers/World.cs` (`PacketHandler_0x2E_GroupRequest`) |
| Boards (client) | `Chaos.Client/Screens/WorldScreen.InputHandlers.cs:840` (F7) and `:1196` (entity click), `Chaos.Client/ViewModel/Board.cs`, `Chaos.Client.Networking/ConnectionManager.cs` (0x31 handler), `Chaos.Client/Screens/WorldScreen.Wiring.cs:455+` |
| Boards (server) | `server/hybrasyl/Subsystems/Messaging/Enums.cs`, `server/hybrasyl/Servers/World.cs` (`PacketHandler_0x3B_AccessMessages`), `server/hybrasyl/Networking/ServerPackets/MessagingResponse.cs`, `server/hybrasyl/Objects/Signpost.cs` |
| Bookkeeping | `docs/hybrasyl-compat-matrix.md` §4.9 and §4.13 |

## Verification

End-to-end for each phase, against **both** Hybrasyl and retail:

- **Doors:** click each previously-broken N/S door on retail (Rucesion 40,42 + 37,28; Piet 29,12; Abel 12,16). Verify open/close animation and walkability transition. Repeat on Hybrasyl. Verify no E/W regression on either server.
- **Group:** form a 2-player group on each server. Verify invite shows on the receiver, accept updates both rosters, recruit-box flow opens and accepts a join request, leave and kick both work.
- **Boards:** click the lod505 (19, 3) signpost on each server. Verify board list opens, articles read, can post (where permitted), close cleanly. F7 mail flow still works (regression check).
- **Compat matrix refresh:** new statuses for 0x31 / 0x63 reflect actual test outcomes; §4.x rewritten against current enum values.
