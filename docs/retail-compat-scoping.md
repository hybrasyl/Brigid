# `fix/retail-compat` — retail-compatibility branch scope

This doc scopes the work on branch `fix/retail-compat`. Three concrete bugs, one shared constraint, and one bookkeeping deliverable (refreshing `hybrasyl-compat-matrix.md`).

## Context

The Chaos.Client must run cleanly against **two** servers:

1. **Hybrasyl** (`qa.hybrasyl.com:2610`) — our own server, full source under `e:/Dark Ages Dev/Repos/server`.
2. **Retail Dark Ages** (`da0.kru.com`) — third-party, **closed source**. We have packet bytes only — no logs, no builds, no source.

Hybrasyl has been reverse-engineered against retail for years, so its wire format is assumed to match retail in shape. When a feature breaks against **both** servers, the bug almost certainly lives in the **client**, not the server. All three issues below have that shape.

## Three retail-compat bugs in scope

### 1. Doors — N/S axis broken on retail only

Background investigation lives in `~/.claude/plans/on-our-recent-doors-snazzy-stonebraker.md`. Reframed below after a 2026-04-28 investigation pass.

#### Refined symptom (2026-04-28)

- N/S-axis doors **never toggle** when this client clicks them on retail (`da0.kru.com`). Affects every panel of every N/S door tested — clicking different panels of the same door doesn't change anything. Failing instances confirmed: Rucesion (40, 42), (37, 28); Piet (29, 12); Abel (12, 16); Mileth (69, 54) and (69, 55).
- E/W doors function normally on retail.
- **Both axes work on Hybrasyl** (across `main`, `develop`, `feature/doors`).
- **Inbound `0x32 Door` handling is fine** — when another player opens an N/S door near this client, the sprite swap renders correctly and the user can walk through. So `HandleDoor` and the open-state pathfinder are both working. The failure is exclusively in this client's **outbound** click for N/S doors.
- The legacy DA client opens these same doors on retail. So retail's catalog, server-side toggle, and 0x32 emission all work — the bug is wholly in what Chaos.Client puts on the wire that the legacy client doesn't.

#### Reframe: missing-behavior, not regression

Originally framed as a regression on `c0237ef`/`d74127b`/`9bb61a6`. After investigation that framing is **wrong**:

- `c0237ef` (sotp-strict pathfinder) was reverted via the upstream merge `9d3ed3f` and is not in current `main`.
- `d74127b` regenerated `DoorTable` correctly — every N/S pair from `docs/doors.md` is present (Rucesion `(2929,2923)`, Piet `(2850,2857)/(2851,2858)/(2852,2859)`, Mileth `(2000,2003)/(2001,2004)`, etc.). The table is not the bug.
- `9bb61a6` only touches the Alt+right-click context menu (dedup + dismiss). It doesn't change the click-out path. User confirmed the menu shows correct labels for N/S doors anyway.
- Hybrasyl's `0x43 PointClick` handler ([World.cs:3494](../../server/hybrasyl/Servers/World.cs#L3494)) is **byte-identical across `main`, `develop`, and `feature/doors`**. `feature/doors` only restructured server-side door registration (`Sprites.cs`, `Door.cs`/`DoorGroup.cs`/`MapObject.cs`), not the wire protocol.
- `Chaos.Networking`'s `ClickConverter` writes `{0x43}{type=3}{x:u16BE}{y:u16BE}` (5 bytes payload). Hybrasyl reads it the same way and works.

The simplest framing consistent with all observations: **N/S door clicks have likely never worked on retail in Chaos.Client.** E/W happens to work because retail's check is satisfied by what we send. N/S has an additional retail requirement we don't satisfy. Hybrasyl is more permissive and tolerates both, so we never noticed.

#### Leading hypothesis (per user, 2026-04-28)

Retail validates **trailing payload bytes** on the door-click packet that Chaos.Client doesn't emit. Hybrasyl's handler reads exactly the 5 bytes we send and stops, silently consuming any additional legacy bytes; retail is stricter and likely requires (or at least keys door registration on) something past the X/Y. For E/W doors retail's missing-bytes default lines up; for N/S it doesn't.

#### Server-emission evidence (2026-04-28, retail packet observation)

Retail's door-open mechanism is **fundamentally different from Hybrasyl's**, and the client only sees activity for E/W:

- **E/W door click on retail:** client receives `0x0C` (CreatureWalk, len 18) followed by `0x0E` (RemoveEntity, len 13). Retail represents doors as invisible entities sitting on the door tile; opening = walk-the-entity-off-tile + remove-entity, which makes the tile traversable. The client already handles this correctly *by accident* — `HandleCreatureWalk` / `HandleRemoveEntity` route through generic `WorldState` entity tracking, and the door-tile foreground is left alone (the door visual disappears because the door entity disappears).
- **N/S door click on retail:** client receives **nothing**. Retail's `0x43 PointClick` handler rejects the click before it gets to "should I move + remove this door entity."

This confirms the missing-trailing-payload hypothesis. Hybrasyl's `0x32 Door` mechanism is a different model entirely; the existing matrix entry "0x32 Door | VERIFIED-COMPATIBLE" was tested only against Hybrasyl, not retail.

Implication for the fix once the missing payload is identified: nothing in `HandleDoor` (the 0x32 handler) needs to change for retail compat — it's dead code on the retail path. The fix is purely on the outbound `0x43` side. Inbound 0x0C / 0x0E already do the right thing.

What might be in the trailing bytes — speculative until we have a capture:

- An axis discriminator (E/W vs N/S) the client should set per door.
- The clicked tile's sprite ID (so retail can confirm which door the client thinks it's clicking).
- An "open right" / hinge-side hint matching the legacy DA's door-rendering convention.
- Padding zeros that retail length-validates.

#### Ruled out

- **Auto-walk + facing.** Confirmed not the case (user, 2026-04-28). Retail does not require the player to be physically adjacent or facing the door before the click is honored.
- **Pathfinder treats N/S open doors as walls.** Confirmed: `IsTileWall` honors sotp correctly, and other-player door opens render and become walkable for this client. Pathfinding is not the failure mode.
- **DoorTable is missing N/S entries.** Audited line-by-line against `docs/doors.md` — every catalogued N/S pair is present.
- **`feature/doors` server changes affect retail compat.** They don't — `feature/doors` is a Hybrasyl-internal restructure with no wire-format change, and retail isn't running Hybrasyl anyway.

#### Adjacent observation: ClickTile is sent for *any* foreground tile

Currently [WorldScreen.InputHandlers.cs:1387-1388](Chaos.Client/Screens/WorldScreen.InputHandlers.cs#L1387) sends `ClickTile(x, y)` whenever `TileHasForeground` returns true — regardless of whether the foreground is an interactable. The legacy DA client only sends a click when the tile is a known interactable (door, signpost). This is a divergence.

**Do not "fix" by gating on door/signpost detection.** Per user (2026-04-28), the broader plan is to expand `ClickTile` for targeting and other future features — gating it now would regress planned work. Note here for future awareness; treat as intentional client-forward behavior.

#### What needs to happen next (handoff)

1. **Capture wire bytes** the legacy DA client sends for an N/S door click on retail. Compare to what Chaos.Client sends for the same click. The byte-count delta and any post-`y` payload pinpoints the missing field. One E/W and one N/S capture bracketed together would also confirm whether legacy distinguishes axes on the wire.
2. **If byte capture is impractical**, fall back to brute-force: append plausible trailing fields to `ClickArgs` (axis byte, sprite ID, zero padding) and observe which retail accepts. Slow but tractable.
3. Once the missing field(s) are identified, add them to `Chaos.Networking`'s `ClickArgs` / `ClickConverter` (or fork client-side if that NuGet can't be modified) and re-test on retail (Mileth 69,54-55) and Hybrasyl (regression check).
4. Add a regression test once the wire shape is known.

#### Critical files for the eventual fix

- `Chaos.Client.Networking/ConnectionManager.cs:138-144` — `ClickTile` send site (currently no axis info).
- `Chaos-Server/Chaos.Networking/Converters/Client/ClickConverter.cs` — wire serialization (5 bytes after opcode currently). External NuGet — may need fork or upstream PR.
- `Chaos-Server/Chaos.DarkAges/Definitions/Enums.cs` `ClickType` enum — only `TargetId=1, TargetPoint=3` defined. If retail uses an additional type, this is where it'd land.
- `Chaos.Client/Screens/WorldScreen.ServerHandlers.cs:1113` `HandleDoor` — verified working; do not touch.
- `Chaos.Client/Definitions/DoorTable.cs` — verified complete; do not touch.

### 2. Group system — broken on Hybrasyl AND retail

Premise: per the Hybrasyl-matches-retail rule, this points client-side.

- **Server emissions (Hybrasyl, ground truth):**
  - `GroupServerPacketType` (0x63): `Ask=1, Member=2, RecruitInfo=4, RecruitAsk=5` — `server/hybrasyl/Subsystems/Players/Grouping/UserGroup.cs:380-386`
  - `GroupClientPacketType` (0x2E): `Request=2, Answer=3, RecruitInit=4, RecruitInfo=5, RecruitEnd=6, RecruitAsk=7` — same file, lines 370-378.
- **Client handler:** `Chaos.Client/Screens/WorldScreen.ServerHandlers.cs:638-695` `HandleGroupInviteReceived` switches on `ServerGroupSwitch` and only covers `Invite`, `RequestToJoin`, `ShowGroupBox`. **`Member` (value 2) is unhandled** — that's a real gap. After accept, the server's roster-update packet arrives and is silently dropped.
- **Wire-format suspicion (client→server):** Hybrasyl's `PacketHandler_0x2E_GroupRequest` in `server/hybrasyl/Servers/World.cs` reads the partner name then carries a `// TODO: currently leaving five bytes on the table here` comment — i.e. it expects 5 trailing bytes the Chaos.Client doesn't send. That mismatch could explain why Hybrasyl never advances the flow when the client initiates.
- **Existing reference:** `docs/hybrasyl-compat-matrix.md` row 0x63 — marked `DIVERGENT | UNINSPECTED`. §4.13's enum values are **stale** (don't match the server source above) and must be refreshed as part of this work.
- **Capture available:** the user can produce a retail packet capture of a working `0x2E GroupRequest` to disambiguate the 5-byte trailing payload. We should ask for that capture before guessing at the layout.

### 4. CTD: `DivideByZeroException` in `GetSteppedWalkOffset` (item-triggered dialog while walking)

User report + stack trace (2026-04-28, retail server, Hybrasyl client `US Dark Ages 7.41`):

```text
Unhandled exception. System.DivideByZeroException: Attempted to divide by zero.
   at Chaos.Client.Systems.AnimationSystem.GetSteppedWalkOffset(Vector2 startOffset, Int32 frameIndex, Int32 frameCount) in AnimationSystem.cs:line 654
   at Chaos.Client.Systems.AnimationSystem.AdvanceWalk(WorldEntity entity, Single elapsedMs, Boolean smoothScroll) in AnimationSystem.cs:line 221
   at Chaos.Client.Systems.AnimationSystem.Advance(...) in AnimationSystem.cs:line 182
   at Chaos.Client.Screens.WorldScreen.Update(GameTime gameTime) in WorldScreen.Update.cs:line 44
```

#### Root cause (concrete)

[`AnimationSystem.GetSteppedWalkOffset`](../Chaos.Client/Systems/AnimationSystem.cs#L637) line 654 computes `var y = x * startY / startX;`. The function guards `frameCount <= 0` (line 639) but never guards `startX == 0`. When the entity's `WalkStartOffset` is `Vector2.Zero`, `startX = 0` and the division throws.

`WalkStartOffset` is set by `StartWalk` from [`GetWalkOffset(direction)`](../Chaos.Client/Systems/AnimationSystem.cs#L623) which only returns nonzero values for `Direction.Up/Right/Down/Left`. Any other Direction value (e.g., `Direction.Invalid`, `Direction.None`, an out-of-range numeric cast) hits the `_ => Vector2.Zero` default, after which the entity is in `EntityAnimState.Walking` with `WalkStartOffset = (0, 0)` — and the next `WorldScreen.Update` frame crashes.

The dialog/item is the **trigger** that sets walk state with a bad Direction; it's not the locus of the bug. The crash is the next-frame consequence.

#### Why retail-only (likely)

The trigger packet that produces a `Direction.Default` walk-start is presumably a retail-shape packet — a refresh, teleport, or movement-response with an unusual Direction byte that Hybrasyl emits differently. Possible candidates worth checking against `docs/hybrasyl-compat-matrix.md`:

- `0x0B ClientWalkResponse` — direction-byte semantics (matrix §3 lists this as MATCH on Hybrasyl, but doesn't audit retail's variant for items that interrupt walk).
- `0x07 DisplayVisibleEntities` — adds the player back at a new tile after item-teleport; if the encoded direction is novel, our enum cast may fall through to default.
- `0x04 Location` / `0x05 UserId` — repositions the player; matrix §4.1 / §4.2 already note divergent trailing bytes.

If we can identify the trigger packet, the secondary fix is to validate Direction at parse time (clamp to a known value or refuse to enter `Walking` with an undefined direction). The primary fix is independent: make `GetSteppedWalkOffset` not crash when its preconditions aren't met.

#### Minimal fix (proposed, not yet implemented)

Add a `startX == 0` early return alongside the existing `frameCount <= 0` guard:

```csharp
private static Vector2 GetSteppedWalkOffset(Vector2 startOffset, int frameIndex, int frameCount)
{
    if (frameCount <= 0)
        return Vector2.Zero;

    var startX = (int)startOffset.X;
    var startY = (int)startOffset.Y;

    //a zero-x start offset means StartWalk was called with a non-cardinal Direction —
    //there's no x to anchor y on via the 2:1 isometric ratio, and the entity has no
    //visible walk to interpolate. drop straight to zero rather than dividing by zero.
    if (startX == 0)
        return Vector2.Zero;

    var framesLeft = frameCount - (frameIndex + 1);
    var x = startX * framesLeft / frameCount;

    if ((x & 1) != 0)
        x += x > 0 ? -1 : 1;

    var y = x * startY / startX;

    return new Vector2(x, y);
}
```

This stops the CTD immediately and is safe — when `startX == 0`, the previous code's `x = 0 * framesLeft / frameCount = 0` already produces no horizontal offset, so the entity wouldn't have visibly slid horizontally even if the divide didn't crash. Returning `Vector2.Zero` outright is consistent with that intent.

#### Follow-up work (separate from the CTD fix)

1. **Find the trigger packet.** Identify which item / dialog flow produces the bad walk state. Add it to `hybrasyl-compat-matrix.md` if it's a divergent retail packet shape.
2. **Validate `Direction` at parse boundary.** Don't enter `EntityAnimState.Walking` with a Direction that isn't one of the four cardinals — either clamp to a default, or skip the `StartWalk` call entirely. Defense-in-depth so future bad Direction sources can't recreate this class of crash.
3. **Audit other consumers of `GetWalkOffset`** (and any cousins) for the same `Vector2.Zero` blind-spot. The `_ => Vector2.Zero` pattern in `GetWalkOffset` looks like a defensive default that callers don't actually defend against.

#### Critical files

- [`Chaos.Client/Systems/AnimationSystem.cs:637-657`](../Chaos.Client/Systems/AnimationSystem.cs#L637) — `GetSteppedWalkOffset` (fix site).
- [`Chaos.Client/Systems/AnimationSystem.cs:623-631`](../Chaos.Client/Systems/AnimationSystem.cs#L623) — `GetWalkOffset` (origin of the zero-vector default).
- [`Chaos.Client/Systems/AnimationSystem.cs:43-66`](../Chaos.Client/Systems/AnimationSystem.cs#L43) — `StartWalk` (where bad Direction enters walk state).
- `Chaos.Client/Collections/WorldState.cs:435,456` and `Chaos.Client/Screens/WorldScreen.ServerHandlers.cs:161,201` — `StartWalk` callsites.
- `Chaos.Client/Screens/WorldScreen.ServerHandlers.cs` `HandleClientWalkResponse` and any teleport/refresh handlers — candidates for where the Direction value originates.

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

**Status (2026-04-28): blocked on observational data.** See the doors section above for the full investigation. Static analysis is exhausted; the client looks axis-correct at every layer. The leading hypothesis is that retail validates trailing payload bytes on the `0x43 PointClick` packet that Chaos.Client doesn't emit — and that this has likely been broken since the client's inception, masked by Hybrasyl's lenience. Resume when one of:

- Wire-byte capture from the legacy DA client clicking an N/S door on retail (gold-standard).
- Documentation of retail's `0x43`-with-`ClickType=3` trailing payload from prior reverse-engineering.
- Brute-force experiments appending candidate fields to `ClickArgs` and watching retail's response.

Once the missing payload is identified, the change likely lands in `Chaos.Networking`'s `ClickConverter` (or a client-side fork of it).

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
