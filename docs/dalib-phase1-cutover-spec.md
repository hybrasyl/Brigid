# Brigid — DALib Networking Migration, Phase 1 Cutover Spec

**Status:** Authoritative execution spec. Drafted 2026-06-27 by Imbas after full verification of DALib packet/enum shapes and Brigid consumer field usage. Supersedes the session-log mapping where they disagree (corrections flagged ⚠️).

**Goal of this cutover:** Take the build from RED → GREEN (0 warnings / 0 errors) by finishing the atomic swap of the `Brigid.Networking` layer off `Chaos.Networking` / `Chaos.Packets` / `Chaos.Cryptography` / `Chaos.IO` and onto DALib's `DALib.Networking.*`. Step 1 (GameClient transport collapse) is already done and sits uncommitted in the working tree. This spec covers Steps 2–3: `Delegates.cs`, `ConnectionManager.cs`, and all consumers.

---

## 0. Locked decisions (do not relitigate)

1. **Remove `*Args` entirely.** ConnectionManager events carry **DALib packet types directly** (J, 2026-06-27). No Brigid-local DTO layer. The `*Args` were Chaos DTOs over what are already typed packet records.
2. **Variant dispatch moves to the consumer.** Board/Exchange/Group are abstract-base + sealed-subclass → consumers `is`-pattern on the subclass. Dialog/Menu are sealed + nested abstract body → consumers switch on the `*Type` discriminator / `is`-pattern the body.
3. **Attributes merge relocates to `PlayerAttributes` ViewModel.** ConnectionManager fires the raw `AttributesPacket`; the accumulate-partial-updates logic (currently `MergeAttributes`) moves into `PlayerAttributes`, which already owns the cached view.
4. **Execution by subagent in a git worktree, against this spec, in fresh context.** Review gates per CLAUDE.md.

## 0.1 OUT OF SCOPE for this cutover (do not touch)

This is the **networking** cutover only. The "trivial bucket" from `chaos-networking-removal-direction.md` Phase 1 is **separate, ship-anytime** work and MUST NOT be conflated here:
- `Chaos.Common` case-insensitive string extensions (`EqualsI`, `StartsWithI`, `ContainsI`, `ReplaceI`) — stay.
- `Chaos.Geometry` `Point`/`Rectangle` — stay (except where a networking type forces a swap; see §6 RedirectInfo).
- `Chaos.Pathfinding` — stays.
- `Chaos.DarkAges.Definitions` enums that are NOT touched by networking code — stay on Chaos for now (enum migration is parent Phase 3a, separate).

Many of the 154 client-project Chaos refs are these out-of-scope categories. **The build going green does NOT require removing them.** It requires only that `Brigid.Networking` + the event consumers compile against DALib packets. Leaving `Chaos.DarkAges`/`Chaos.Common`/`Chaos.Geometry` PackageReferences in place is correct for this cutover. Only `Chaos.Networking` (+ its transitive Packets/Cryptography/IO) usage must be eliminated from `Brigid.Networking`.

---

## 1. New GameClient API surface (Step 1, DONE — call into this)

```
public CryptoState Crypto { get; }                 // DALib.Networking.Crypto.CryptoState
public void ResetCrypto()                          // fresh unkeyed CryptoState
public void ApplyCryptoKey(byte seed, byte[] key, string? keyTableSeed)
public void SetSequence(byte newSequence)          // = Crypto.ClientOrdinal
public void Send(IClientPacket packet)             // DALib.Networking.Wire.IClientPacket
public int DrainPackets(List<IServerPacket> buffer, int maxCount = int.MaxValue)
public event DisconnectedHandler? OnDisconnected;
// Connect/ConnectAsync/Disconnect/Connected/RemoteEndPoint/TryGetTcpSmoothedRttMs unchanged
```
- Heartbeats are answered **inside** GameClient (`DispatchInbound`). ConnectionManager no longer sees/handles them.
- Inbound packets are **already deserialized** by the codec → `DrainPackets` returns typed `IServerPacket`. ConnectionManager handlers **cast + fire**; they no longer call `Client.Deserialize<T>`.
- `Client.Deserialize<T>(in pkt)` and the raw `Client.Send(ref Packet)` overload are **gone**. The three hand-rolled raw-Packet workarounds (ClickDoor/ClickFloorTile/ForceClientPacket) must use typed DALib packets (§6).

## 2. ConnectionManager dispatch plumbing changes

- `private readonly Action<IServerPacket>?[] PacketHandlers = new Action<IServerPacket>?[byte.MaxValue + 1];`
- `public void ProcessPackets(List<IServerPacket> buffer)` (was `List<ServerPacket>`). Drop the `ArrayPool<byte>.Shared.Return(pkt.Data)` in `finally` (no longer renting raw byte buffers per packet — codec owns lifetime). Keep the try/catch + `NoticeDebugLog` instrumentation; log `pkt.Opcode` (lowercase `Opcode` on `IPacket`). Hex-dump logging that read `pkt.Data` must adapt or drop (IServerPacket has no `.Data`; use `pkt.ToBody()` if a dump is still wanted, else drop the hex line).
- `private void HandlePacket(IServerPacket pkt)` → `PacketHandlers[pkt.Opcode]?.Invoke(pkt)`.
- `IndexHandlers()` keys stay `(byte)ServerOpcode.Xxx` but now `DALib.Networking.Wire.ServerOpcode` (note casing: DALib is `ServerOpcode`, Chaos was `ServerOpCode`).
- `ChaosGame.cs:36` `List<ServerPacket> PacketBuffer` → `List<IServerPacket>`.
- `SendIfWorld<T>` constraint `where T : IPacketSerializable` → `where T : IClientPacket`.
- Remove `using Chaos.*` from ConnectionManager; add `using DALib.Networking.Packets.Client; using DALib.Networking.Packets.Server; using DALib.Networking.Wire; using DALib.Enums;` (+ Crypto as needed).

## 3. Delegates.cs rewrite

- **GameClient region:** `DisconnectedHandler` unchanged.
- **Primitive-signature ConnectionManager delegates (keep, swap enum namespace):**
  `ConnectionStateChangedHandler`, `ConnectionErrorHandler`, `CancelCastingHandler`, `RefreshResponseHandler`, `RemoveEntityHandler`, `UserIdHandler`, `WorldEntryCompleteHandler`, `EditableProfileRequestHandler`, `MapChangePendingHandler`, `MapLoadCompleteHandler`, `LocationChangedHandler` — unchanged signatures.
  - `ClientWalkResponseHandler(Direction, int, int)`, `CreatureTurnHandler(uint, Direction)`, `CreatureWalkHandler(uint, int, int, Direction)` — `Direction` now `DALib.Enums.Direction`.
  - `RedirectReceivedHandler(RedirectInfo info)` — unchanged (RedirectInfo struct changes internally, §6).
  - `ServerTableReceivedHandler(ServerTableData data)` — see §6 (ServerTableData may be retired in favor of `IList<ServerEntry>`; if retired, signature becomes `(IList<ServerEntry>)`).
- **DELETE the entire "Args-Based Delegates" region.** Every `*Handler(XxxArgs)` delegate is removed. The corresponding `ConnectionManager` events are re-typed to carry the DALib packet (see §5 table). Recommended: drop the named-delegate indirection and declare events as `event Action<DalibPacketType>?` directly, OR keep named delegates re-typed to the DALib packet — executor's choice for readability, but no `*Args` may remain.
- Remove `using Chaos.Geometry.Abstractions.Definitions; using Chaos.Networking.Entities.Server;`. Add `using DALib.Enums;` and DALib packet namespaces as needed.

---

## 4. Outbound send mapping (ConnectionManager public methods → DALib client packet)

All sends go through `Client.Send(<packet>)`. `SendIfWorld` guard unchanged. ⚠️ = verify before trusting.

| Method | Chaos *Args | DALib client packet |
|---|---|---|
| ChangePassword | PasswordChangeArgs | `ChangePasswordPacket{Name,CurrentPassword,NewPassword}` (0x26) |
| ClickEntity | ClickArgs(TargetId) | `ClickPacket.Entity(targetId)` |
| ClickTile / ClickFloorTile / ClickDoor | ClickArgs + raw | `ClickPacket.Point(x,y,flag)` — **collapse all three.** doors→flag 0, floor/signpost/ground→flag 1. Drop `ClickDoor`'s `layer` param + the spurious `0x00`; WorldScreen stops computing LFG/RFG (InputHandlers ~1496/1684). |
| ClickWorldMapNode | WorldMapClickArgs | `MapPointClickPacket{CheckSum,MapId,X,Y}` (0x3F) |
| CreateCharFinalize | CreateCharFinalizeArgs | `CreateCharFinalizePacket{HairStyle,Gender,HairColor}` (0x04). HairColor: Chaos `DisplayColor`→`byte` (DALib field is `byte HairColor`). |
| CreateCharInitial | CreateCharInitialArgs | `CreateCharRequestPacket{Name,Password,Email}` (0x02). ⚠️ Chaos had no Email — pass `""`; verify Hybrasyl tolerates. |
| DropGold | GoldDropArgs | `DropGoldPacket{Amount,X,Y}` (0x24) |
| DropGoldOnCreature | GoldDroppedOnCreatureArgs | `DropGoldOnCreaturePacket{Amount,TargetId}` (0x2A) |
| DropItem | ItemDropArgs | `DropItemPacket{Slot,X,Y,Count}` (0x08) |
| DropItemOnCreature | ItemDroppedOnCreatureArgs | `DropItemOnCreaturePacket{Slot,TargetId,Count}` (0x29) |
| Login | LoginArgs | `LoginPacket{Name,Password}` (0x03). Chaos ClientId1/2/IsValid have no DALib field — drop (LoginPacket's optional Rand/hash fields are codec-internal). ⚠️ confirm login succeeds without them. |
| PickupItem | PickupArgs | `PickupItemPacket{Slot,X,Y}` (0x07) |
| RaiseStat | RaiseStatArgs(Stat) | `StatPointPacket{Stat=<bit>}` (0x47). Map `DALib.Enums.Stat` flag → the single selector byte (Str=0x01,Dex=0x02,Int=0x04,Wis=0x08,Con=0x10). |
| RequestExit(isRequest) | ExitRequestArgs | `ClientExitPacket{Signal = isRequest ? ExitSignal.Request : ExitSignal.Confirm}` (0x0B) |
| RequestHomepage | HomepageRequestArgs | `RequestHomepagePacket()` (0x68) |
| RequestMapData | MapDataRequestArgs | ⚠️ `RequestMapPacket{X,Y}` (0x05) takes coords — Chaos `MapDataRequestArgs` was bare. Verify what Hybrasyl expects; may need current player X/Y or 0,0. **EXECUTOR-VERIFY.** |
| RequestNotice | NoticeRequestArgs | `RequestNotificationPacket()` (0x4B) |
| RequestRefresh | RefreshRequestArgs | `RefreshPacket()` (0x38) |
| RequestSelfProfile | SelfProfileRequestArgs | `RequestProfilePacket()` (0x2D) |
| RequestServerTable | ServerTableRequestArgs(RequestTable) | `ServerTableRequestPacket()` (0x57) |
| SelectServer(id) | ServerTableRequestArgs(ServerId) | `ServerTableSelectPacket{ServerId}` (0x57 sibling variant) |
| RequestWorldList | WorldListRequestArgs | `RequestWorldListPacket()` (0x18) |
| SendAddIgnore / SendRemoveIgnore / SendIgnoreRequest | IgnoreArgs | `IgnorePacket.AddUser(name)` / `.RemoveUser(name)` / `.Request()` (0x0D) |
| SendBeginChant | BeginChantArgs(CastLineCount) | `BeginCastingPacket{Lines}` (0x4D) |
| SendChant | ChantArgs(ChantMessage) | `CastLinePacket{Line}` (0x4E) |
| SendBoardInteraction | BoardInteractionArgs | `BoardRequestPacket` variants (0x3B): map `BoardRequestType`→{BoardListPacket, ViewBoardPacket, ViewPostPacket, NewPostPacket, DeletePostPacket, SendMailPacket, HighlightPostPacket}. **EXECUTOR: map each BoardRequestType case to the right variant + fields.** |
| SendCreateGroupBox | GroupInviteArgs(CreateGroupbox)+CreateGroupBoxInfo | `GroupRequestPacket.Groupbox(leader,title,note,minLevel,maxLevel,maxWarrior,maxWizard,maxRogue,maxPriest,maxMonk)` (0x2E). leader = playerName. |
| SendGroupInvite(action,name) | GroupInviteArgs(ClientGroupSwitch) | `GroupRequestPacket` factories by action: TryInvite/AcceptInvite/RemoveGroupBox/RecruitJoin. ⚠️ **ClientGroupSwitch has no DALib enum** — map each ClientGroupSwitch value → the matching factory/Stage byte. **EXECUTOR-VERIFY** the ClientGroupSwitch→Stage correspondence against Brigid's current usage + DALib GroupRequestPacket stages. |
| SendDialogResponse | DialogInteractionArgs(DialogArgsType,...) | `DialogUsePacket` variants (0x3A): None→`DialogNavigationPacket`, option→`DialogOptionResponsePacket{Option}`, text→`DialogTextResponsePacket{Text}`. Shared prefix ObjectType/ObjectId/PursuitId/PursuitIndex. ⚠️ **DialogArgsType has no DALib enum** — branch on it to pick the variant. Map Chaos `dialogId`→`PursuitIndex`. **EXECUTOR-VERIFY** the prefix field correspondence. |
| SendEditableProfile | EditableProfileArgs | `SetProfilePacket{PortraitData,ProfileText}` (0x4F) |
| SendEmote | EmoteArgs(BodyAnimation) | `EmotePacket{EmoteIndex=(byte)bodyAnimation}` (0x1D). ⚠️ verify emote index basis (BodyAnimation enum value vs an offset). |
| SendExchangeInteraction | ExchangeInteractionArgs(ExchangeRequestType) | `ExchangePacket` variants (0x4A): StartExchange/AddExchangeItem/AddExchangeStackableItem/SetExchangeGold/CancelExchange/AcceptExchange, all with `OtherUserId`. Map ExchangeRequestType→variant. |
| SendMenuResponse | MenuInteractionArgs | `NpcMainMenuPacket` variants (0x39): select/text/text-pair/option/option-arg/handle. Map by slot/args presence. **EXECUTOR-VERIFY** which variant each current call path needs. |
| SendMetaDataRequest | MetaDataRequestArgs | `RequestMetafilePacket.AllCheckSums()` / `.ForName(name)` (0x7B) keyed by MetaDataRequestType. |
| SendOptionToggle | OptionToggleArgs(UserOption) | `SettingsPacket{SettingNumber}` (0x1B). ⚠️ **UserOption has no DALib enum** — map UserOption→setting number. **EXECUTOR-VERIFY** the mapping (likely (byte)UserOption, but confirm against retail/Hybrasyl semantics). |
| SendPublicMessage / SendShout | PublicMessageArgs(PublicMessageType) | `TalkPacket{ChatType=Say|Shout, Message}` (0x0E) |
| SendSetNotepad | SetNotepadArgs | `SetNotepadPacket{Slot,Message}` (0x23) |
| SendSocialStatus | SocialStatusArgs | `StatusPacket{Status=(byte)status}` (0x79) |
| SendWhisper | WhisperArgs | `WhisperPacket{Target,Message}` (0x19) |
| Spacebar | SpacebarArgs | ⚠️ `AttackPacket()` (0x13) — **NOT RefreshPacket** (one agent guessed wrong). assail = attack. **EXECUTOR-VERIFY** Brigid's `ServerOpCode.Spacebar`/assail intent maps to 0x13. |
| SwapSlot | SwapSlotArgs | `SwapSlotPacket{Window=(byte)panelType,Slot1,Slot2}` (0x30) |
| ToggleGroup | ToggleGroupArgs | `GroupTogglePacket()` (0x2F) |
| Turn | TurnArgs(Direction) | `TurnPacket{Direction}` (0x11) |
| Unequip | UnequipArgs(EquipmentSlot) | `UnequipPacket{Slot=(byte)slot}` (0x44) |
| UseItem | ItemUseArgs | `UseItemPacket{Slot}` (0x1C) |
| UseSkill | SkillUseArgs | `UseSkillPacket{Slot}` (0x3E) |
| UseSpell / UseSpellOnTarget | SpellUseArgs(ArgsData) | `UseSpellPacket.NoTarget(slot)` / `.Targeted(slot,targetId,x,y)` — prefer the factories over hand-packing the 8-byte argsData. UseSpellOnTarget's manual byte-packing collapses into `.Targeted(...)`. |
| Walk | ClientWalkArgs(Direction,StepCount) | `WalkPacket{Direction, Sequence=WalkStepCount++}` (0x06) |

**Version handshake** (`HandleAcceptConnection`): `VersionArgs{Version}` → `VersionPacket{Version=LobbyClientVersion}` (0x00).

---

## 5. Inbound handler mapping (opcode → DALib server packet; event re-typed to carry it)

Pattern for the simple ones: `private void HandleXxx(IServerPacket p) { var pkt = (XxxPacket)p; OnXxx?.Invoke(pkt); }` and `event Action<XxxPacket>? OnXxx;`. The consumer reads DALib fields (see §7). **EXECUTOR: for every server packet below, read its DALib source for exact field names before remapping consumers — §7 lists the Chaos field names consumers currently read; you must pair them with DALib field names from source.**

Handshake / lifecycle (real logic — preserve choreography):
- AcceptConnection → (no packet body) send `VersionPacket` if PendingLobbyVersion.
- ConnectionInfo (0x00) → `CryptoKeyPacket{Seed,Key}` → `ApplyCryptoKey(Seed, Key, null)`; if Lobby, `RequestServerTable()`.
- ServerTableResponse → `ServerTableDataPacket` — see §6 (exposes parsed `IList<ServerEntry> Servers`; Brigid's `ServerTableData.Parse` is redundant).
- Redirect (0x03) → `RedirectPacket` — see §6.
- LoginMessage / LoginNotice / LoginControl → `LoginMessagePacket` / `LoginNotificationPacket`(⚠️confirm name) / `UrlPacket`(0x66, "LoginControl" = homepage URL). **EXECUTOR-VERIFY** the LoginControl↔UrlPacket mapping & fields.
- UserId → `UserAppearancePacket`(⚠️confirm; reads `.Id` only) — set AislingId, fire OnUserId(id), EntryState |= UserId.
- MapInfo / MapData / MapLoadComplete / MapChangeComplete → `MapInfoPacket` / `MapDataPacket` / (lifecycle) — keep the Hybrasyl synthesized-lifecycle logic in HandleMapInfo verbatim.
- Location → `LocationPacket{X,Y}`.
- Attributes → `AttributesPacket` — see §6 (NO merge here; fire raw).

Entities / world:
- DisplayVisibleEntities → `DrawObjectsPacket`(⚠️confirm name) ; DisplayAisling → `DisplayUserPacket`(⚠️confirm).
- RemoveEntity → `RemoveObjectPacket` (`.ObjectId`).
- CreatureWalk → `CreatureWalkPacket` (**OldX/OldY**, was `args.OldPoint.X/Y`).
- ClientWalkResponse → `ConfirmWalkPacket`.
- CreatureTurn → `CreatureTurnPacket`.

Chat: ServerMessage → `ServerMessagePacket`(⚠️confirm) ; DisplayPublicMessage → `PublicMessagePacket`.
Inventory: AddItemToPane → `AddItemPacket` ; RemoveItemFromPane → `RemoveItemPacket`.
Skills/spells: AddSkillToPane → `AddSkillPacket` ; RemoveSkillFromPane → `RemoveSkillPacket` ; AddSpellToPane → `AddSpellPacket` ; RemoveSpellFromPane → `RemoveSpellPacket`.
Equipment: Equipment → `AddEquipmentPacket` ; DisplayUnequip → `RemoveEquipmentPacket`.
Visual/audio: HealthBar → `HealthBarPacket` ; Sound → `PlaySoundPacket` ; BodyAnimation → `PlayerAnimationPacket`(⚠️confirm) ; Animation → `SpellAnimationPacket` (0x29) ; Cooldown → `CooldownPacket`.
- ⚠️ **Effect → ???** Chaos `EffectArgs{EffectIcon,EffectColor}` is the status-effect icon bar (NOT SpellAnimation). **EXECUTOR-VERIFY:** read Brigid's `ServerOpCode.Effect` value, find the DALib server packet at that opcode (candidates: an effect/status packet). If none exists in DALib, this is a **blocker to surface** — do not fabricate.
World state: LightLevel → `LightLevelPacket` ; Door → `DoorPacket` ; RefreshResponse → `RefreshPacket`(server, ⚠️confirm) ; MapChangePending → lifecycle.
NPC: DisplayMenu → `NpcMenuPacket` ; DisplayDialog → `NpcDialogPacket` ; DisplayBoard → `BoardResponsePacket` ; DisplayExchange → `ExchangeResponsePacket` ; DisplayGroupInvite → `GroupResponsePacket` (§6).
Profiles/lists: EditableProfileRequest → (no body) ; SelfProfile → `SelfProfilePacket` ; OtherProfile → `ProfilePacket`(⚠️confirm) ; WorldList → `UserListPacket` (0x36) ; WorldMap → `WorldMapPacket`(⚠️confirm).
Notepads: DisplayEditableNotepad → `EditablePaperPacket` ; DisplayReadonlyNotepad → `ReadonlyPaperPacket`.
Misc: ExitResponse → `ConfirmExitPacket`(⚠️confirm) ; ForceClientPacket → `BouncePacket` (§6) ; CancelCasting → `CancelCastPacket` ; MetaData → `MetafilePacket`.

---

## 6. Structural deltas — the real work (spelled out)

### 6.1 Attributes (the big one)
- `HandleAttributes`: `var pkt = (AttributesPacket)p; OnAttributes?.Invoke(pkt); EntryState |= Attributes; CheckWorldEntryComplete();` — **no MergeAttributes.**
- **Delete** `MergeAttributes` and the `public AttributesArgs? Attributes` cached property from ConnectionManager. (Its only reads are PlayerAttributes.Current + ShowStatusBook — both move to read from PlayerAttributes.)
- `event Action<AttributesPacket>? OnAttributes;`
- **PlayerAttributes ViewModel** (`Brigid/ViewModel/PlayerAttributes.cs`) becomes the merge owner. `Update(AttributesPacket pkt)`: overlay non-null sub-records onto accumulated flat state. Field map (Chaos flat → DALib nested):
  - Primary (pkt.Primary != null): Level, Ability, MaxHp←Primary.MaxHp, MaxMp←Primary.MaxMp, Str/Int/Wis/Con/Dex, UnspentPoints, MaxWeight, CurrentWeight.
  - Current (pkt.Current != null): CurrentHp←Current.Hp, CurrentMp←Current.Mp.
  - Experience (pkt.Experience != null): TotalExp←Experience.Experience, ToNextLevel←Experience.ExpToLevel, TotalAbility←Experience.AbilityExp, ToNextAbility←Experience.NextAB, GamePoints←Experience.Gp, Gold←Experience.Gold.
  - Secondary (pkt.Secondary != null): Ac←Secondary.Ac (sbyte), Dmg←Secondary.DmgRating, Hit←Secondary.HitRating, MagicResistance←Secondary.MrRating, OffenseElement←Secondary.OffensiveElement, DefenseElement←Secondary.DefensiveElement, Blind←Secondary.Blinded.
  - HasUnreadMail ← `pkt.UnreadMail` (top-level on AttributesPacket) — not in a sub-record.
  - IsAdmin/IsSwimming (Chaos GameMasterA/B flags): no DALib equivalent in AttributesPacket — **EXECUTOR-VERIFY** whether MovementMode or another field carries swimming; if not, drop or default these (consumers: check who reads IsAdmin/IsSwimming).
- **StatsPanel / ExtendedStatsPanel** read the flat fields off PlayerAttributes (unchanged) — only PlayerAttributes' internal storage type changes; keep its flat public surface so the panels need minimal/no change.
- **WorldScreen.ServerHandlers HandleAttributes (line 210)** read `args.StatUpdateType`. DALib has no StatUpdateType — replace with sub-record presence checks (`pkt.Primary is not null` etc.) for whatever the handler gated on. **EXECUTOR: read line 210 to see what it did with StatUpdateType and translate.**

### 6.2 The five variant inbound packets → consumer dispatches
- Board (0x31) `BoardResponsePacket` (abstract) → subclasses `BoardListPacket`/`BoardIndexPacket`/`BoardPostPacket`/`BoardResultPacket`, discriminator `ResponseType` (BoardResponseType). Consumer (WorldState.cs:719 lambda + Board.cs) `is`-patterns. Field pairing (Chaos→DALib): Chaos `args.Type`→ResponseType; `args.Boards`→BoardListPacket.Boards; `args.Board.Posts[]` {PostId,Author,MonthOfYear,DayOfMonth,Subject,IsHighlighted}→BoardIndexPacket.Messages[] {confirm fields}; `args.Post` {PostId,Author,MonthOfYear,DayOfMonth,Subject,Message}→BoardPostPacket {PostId,Author,Month,Day,Subject,Body}; `args.Success/ResponseMessage`→BoardResultPacket {Success,Message}. **EXECUTOR: confirm BoardIndexPacket.Messages element field names.**
- Exchange (0x42) `ExchangeResponsePacket` (abstract) → Start/RequestAmount/AddItem/SetGold/Cancel/Accept variants, discriminator `ResponseType` (ExchangeResponseType). Consumer WorldState.cs:667 reads a flat union today; switch to `is`-pattern. Pair Chaos fields {ExchangeResponseType,OtherUserId,OtherUserName,FromSlot,RightSide,ExchangeIndex,ItemSprite,ItemColor,ItemName,GoldAmount,PersistExchange,Message} → the per-variant DALib fields (StartExchangeResponsePacket{OtherUserId,OtherUserName}; RequestExchangeAmountPacket{SourceSlot}; AddExchangeItemResponsePacket{RightSide,ExchangeIndex,Sprite,Color,Name}; SetExchangeGoldResponsePacket{RightSide,GoldAmount}; Cancel/AcceptExchangeResponsePacket{RightSide,Message}).
- Group (0x63) `GroupResponsePacket` (abstract) → `GroupPromptPacket{SourceName}` (Ask=1, RecruitAsk=5) + `GroupRecruitInfoPacket{Info:GroupRecruitInfo}` (RecruitInfo=4). **This replaces the hand-rolled `HandleDisplayGroupInvite` SpanReader entirely** — delete it; handler becomes cast+fire. Consumer (GroupInvite.cs + WorldScreen HandleGroupInviteReceived + GroupRecruitPanel) currently reads {SourceName, ServerGroupSwitch, GroupBoxInfo.*}. ServerGroupSwitch has no DALib enum → consumer `is`-patterns the subclass: GroupPromptPacket→(invite/ask), GroupRecruitInfoPacket→(recruit-info, with Info.{RecruiterName,GroupName,Note,StartingLevel,EndingLevel,WarriorsWanted,CurrentWarriors,WizardsWanted,CurrentWizards,RoguesWanted,CurrentRogues,PriestsWanted,CurrentPriests,MonksWanted,CurrentMonks}). Map GroupRecruitPanel's GroupBoxInfo field reads → GroupRecruitInfo field names. The subtype 2/5 "log+drop" branches: Group only has Ask/RecruitInfo/RecruitAsk variants — `Member` (old subtype 2) isn't a DALib variant, so it can't arrive typed; no handling needed.
- Dialog (0x30) `NpcDialogPacket` (sealed, `DialogType:NpcDialogType` + nested `NpcDialog` body subclasses Text/Options/TextInput/Close). Consumer NpcInteraction.cs + NpcSessionControl reads {DialogType,EntityType,SourceId,PursuitId,DialogId,Name,Sprite,Color,IllustrationIndex,Text,HasNextButton,HasPreviousButton,Options[],TextBoxPrompt,TextBoxLength}. Pair to NpcDialogPacket top-level fields + body subclass (OptionsDialog.Options, TextInputDialog.{TopCaption,InputLength,BottomCaption}). **EXECUTOR: read NpcDialogPacket.cs for the exact top-level field names (Name/Sprite/Color/Illustration/Text/next/prev) — agent #1 only enumerated the body subclasses.**
- Menu (0x2F) `NpcMenuPacket` (sealed, `MenuType:NpcMenuType` + nested `NpcMenu` body subclasses). Consumer NpcSessionControl.ShowMenu + MenuListPanel + MenuShopPanel read {MenuType,EntityType,SourceId,PursuitId,Name,Sprite,Color,IllustrationIndex,Args,Text,Options[](Text,Pursuit),Slots,Skills[],Spells[],Items[]}. Pair to NpcMenuPacket top-level + body subclasses (OptionsMenu.Options:NpcMenuOption(Text,Pursuit); ItemListMenu.Items:NpcMenuItem(Sprite,Color,Cost,Name,Description); SpellListMenu/SkillListMenu.{Spells,Skills}:NpcMenuCastable(IconType,Icon,Color,Name); PlayerItemListMenu.Slots). **EXECUTOR: read NpcMenuPacket.cs top-level field names; pair the populate methods.**

### 6.3 RedirectInfo + redirect handshake
- `RedirectInfo` record struct (CM.cs:2105): `Key` field `string → byte[]`. (RedirectPacket.EncryptionKey is byte[].)
- `HandleRedirect`: `var pkt = (RedirectPacket)p;` → build `RedirectInfo(pkt's IpAddress+Port as IPEndPoint, pkt.EncryptionSeed, pkt.EncryptionKey, pkt.Name, pkt.RedirectId, targetState)`. Keep the fire-before-Disconnect ordering note.
- `FollowPendingRedirect`: replace `Client.Crypto = new Crypto(seed, key, keySaltSeed)` with `Client.ApplyCryptoKey(redirect.Seed, redirect.Key, keySaltSeed)`; keySaltSeed unchanged (empty lobby→login, character name login→world). Replace `ClientRedirectedArgs{Id,Seed,Key,Name}` send with `ClientJoinPacket{EncryptionSeed=redirect.Seed, EncryptionKey=redirect.Key, Name=redirect.Name, RedirectId=redirect.Id}` (0x10) — or `ClientJoinPacket.FromRedirect(redirectPacket)` if the RedirectPacket is still in scope. **EXECUTOR-VERIFY** ClientJoinPacket field names.
- `ConnectToLobbyAsync`/`HandleConnectionInfo`: replace `Client.Crypto = new Crypto(...)` with `ResetCrypto()` (lobby start) / `ApplyCryptoKey(seed,key,null)` (ConnectionInfo). `SetSequence(0)` calls stay valid.

### 6.4 ForceClientPacket → BouncePacket + RawClientPacket
- `HandleForceClientPacket`: `var pkt = (BouncePacket)p;` (fields `ClientOpcode`, `Data` — ⚠️confirm names). The current code rents a buffer + builds raw `Packet` + `Client.Send(ref packet)`. Replace with a Brigid-local `RawClientPacket : ClientPacket` whose `Opcode => forcedOpcode` and `WriteBody(writer)` writes `Data`; `Client.Send(new RawClientPacket(pkt.ClientOpcode, pkt.Data))`. Then `OnForceClientPacket?.Invoke(pkt)` — **but no consumer subscribes** (agent #2 found zero subscriptions), so the event can be deleted along with its delegate, OR kept dormant. Recommend: keep the re-send behavior, delete the unused event.
- Place `RawClientPacket` in `Brigid.Networking` (small internal class). **EXECUTOR-VERIFY** the `ClientPacket` base + `IPacketWriter` write-method names (likely `WriteBytes`/`Write`).

### 6.5 ServerTableData
- DALib `ServerTableDataPacket.Servers` is already parsed (`IList<ServerEntry>{Id,IpAddress,Port,Name}`). Brigid's `ServerTableData.Parse` (+ the whole `ServerTableData.cs`) is **redundant**. Two options:
  - (a) Retire `ServerTableData`; change `OnServerTableReceived`/`ServerTableReceivedHandler` to carry `IList<ServerEntry>`; update the lobby consumer (LobbyLoginScreen) to read ServerEntry fields. Cleaner.
  - (b) Keep `ServerTableData` as a thin shape, build it from `Servers`. Less consumer churn.
  - **Lean (a)** but ⚠️ check whether `ServerTableData` carried a `ShowServerList` flag the consumer needs (HandleServerTableResponse logs `ShowServerList`); if so, preserve that signal. **EXECUTOR-VERIFY** the lobby consumer's needs, then pick.

---

## 7. Consumer remap index (where the field-level work lands)

Files that subscribe to re-typed events / store `*Args` and must be remapped to DALib packet fields. Full per-event field lists are in the verification reports; key files:
- `Brigid/Collections/WorldState.cs` — the largest consumer (lambdas for AddItem/RemoveItem/AddSkill/AddSpell/Cooldown/Equipment/Unequip/Attributes/Exchange/Board/Dialog/Menu/GroupInvite/WorldList). Lines ~598–791.
- `Brigid/ChaosGame.cs` — DisplayAisling (135), DisplayVisibleEntities (134), MetaData (127); `PacketBuffer` type (36).
- `Brigid/Screens/WorldScreen.ServerHandlers.cs` — Animation/BodyAnimation/Effect/HealthBar/LightLevel/Door/SelfProfile/OtherProfile/Attributes(210)/PublicMessage/ServerMessage/ExitResponse; `ShowStatusBook` reads cm.Attributes (907).
- `Brigid/Screens/WorldScreen.Map.cs` — MapInfo (26, reads cm.MapInfo at 32), MapData (106).
- `Brigid/Screens/WorldScreen.Wiring.cs` — subscription wiring (re-typed delegates).
- `Brigid/Screens/LobbyLoginScreen.cs` — LoginMessage/LoginNotice/LoginControl + server table.
- `Brigid/ViewModel/*.cs` — PlayerAttributes (merge owner now), NpcInteraction (CurrentDialog/CurrentMenu types), GroupInvite (Current type), Board (BoardInfo type), Equipment, Inventory, SkillBook, SpellBook, Exchange, GroupState.
- `Brigid/Controls/World/Popups/Dialog/*` (NpcSessionControl, MenuListPanel, MenuShopPanel), `.../GroupRecruitPanel.cs`, `.../Profile/*`, `.../WorldList/WorldListControl.cs`, `.../Boards/*`, `.../Exchange/*`.

**Discipline for the executor:** for each consumer, (1) read the DALib packet's source for exact field names, (2) replace the Chaos `*Args` field reads with the DALib equivalents, (3) where the event now carries an abstract/variant packet, add the `is`-pattern / `*Type` switch at the consumer. Do **not** guess a DALib field name — read the source. An unmapped field is a finding to surface, not a guess to make.

---

## 8. EXECUTOR-MUST-VERIFY list (genuine gaps — surface, don't fabricate)

1. **Effect status-bar packet** (§5) — find DALib packet at Brigid's `ServerOpCode.Effect` opcode. If absent → BLOCKER.
2. **Spacebar → AttackPacket(0x13)** (§4) — confirm assail intent.
3. **UserOption → SettingsPacket.SettingNumber** mapping (§4).
4. **ClientGroupSwitch → GroupRequestPacket stage/factory** correspondence (§4).
5. **DialogArgsType → DialogUsePacket variant** + prefix field mapping (§4).
6. **RequestMapData** — does `RequestMapPacket{X,Y}` need real coords (§4)?
7. **Login** without ClientId1/2/IsValid succeeds (§4).
8. DALib server packet **field names** for all simple inbound packets (§5/§7) — read source per packet.
9. **ClientJoinPacket / BouncePacket / CryptoKeyPacket / UserAppearancePacket / DrawObjectsPacket / DisplayUserPacket / ProfilePacket / WorldMapPacket / LoginNotificationPacket / ConfirmExitPacket / UrlPacket** exact names + fields (the ⚠️confirm tags in §5).
10. **IsAdmin/IsSwimming** (GameMaster flags) — any DALib carrier, or drop (§6.1)?
11. `IPacketWriter` write-method names for `RawClientPacket` (§6.4).
12. `ServerTableData` ShowServerList signal needed by lobby (§6.5)?

---

## 9. Execution order + gates

1. **Branch/worktree** off current working tree (which already has Step-1 GameClient changes + dev csproj ProjectReferences uncommitted). Do NOT discard those.
2. Resolve the §8 verify list first (read DALib source) — produce a short "resolved gaps" note; if any item is a true blocker (e.g. Effect packet absent), STOP and surface.
3. `Delegates.cs` (§3) → `ConnectionManager.cs` inbound handlers + dispatch (§2,§5) → outbound sends (§4) → structural deltas (§6). Build stays red; that's expected.
4. Consumer remap (§7) until build reaches **0 errors / 0 warnings**.
5. **Gate A (bug/regression review):** correctness, field-mapping accuracy, the §8 resolutions, crypto/redirect choreography intact, no swallowed exceptions, `trash` for any deletions.
6. **Gate B (architecture/design review):** consistency with array-indexed dispatch, events-carry-packets pattern, variant dispatch in consumers, no leftover `*Args`/`Chaos.Networking` in `Brigid.Networking`.
7. Hand back to J for the live lobby→login→world test against qa.hybrasyl.com:2610 (and retail smoke for the click/door anchor-flag + group paths).

## 10. Definition of done
- `grep -rn "Chaos.Networking\|Chaos.Packets\|Chaos.Cryptography\|Chaos.IO" Brigid.Networking/` → empty.
- No `*Args` types referenced anywhere in the solution.
- `dotnet build Brigid.slnx` → 0/0.
- `Chaos.DarkAges`/`Chaos.Common`/`Chaos.Geometry`/`Chaos.Pathfinding` PackageReferences may remain (out of scope §0.1).
- Live handshake verified by J.
