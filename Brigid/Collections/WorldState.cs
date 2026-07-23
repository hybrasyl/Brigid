#region
using Brigid.Data;
using Brigid.Data.Models;
using Brigid.Data.Utilities;
using Brigid.Extensions;
using Brigid.Models;
using Brigid.Networking;
using Brigid.Systems;
using Brigid.ViewModel;
using Chaos.DarkAges.Definitions;
using Chaos.Extensions.Common;
using Chaos.Geometry;
using Chaos.Geometry.Abstractions;
using Chaos.Geometry.Abstractions.Definitions;
using DALib.Networking.Packets.Server;
#endregion

namespace Brigid.Collections;

/// <summary>
///     Tracks all visible entities in the current map and exposes all authoritative game state (inventory, skills, spells,
///     equipment, chat, etc.) via static ViewModel properties. Updated from network packets, read by WorldScreen for rendering.
/// </summary>
public static class WorldState
{
    private static readonly Dictionary<uint, WorldEntity> Entities = [];
    private static readonly List<WorldEntity> SortBuffer = [];
    private static int SortVersion;
    private static int LastSortedVersion = -1;

    //cached chant data — loaded once per login, invalidated on save
    private static List<SkillChantEntry>? CachedSkillChants;
    private static List<SpellChantEntry>? CachedSpellChants;

    /// <summary>
    ///     The player's entity ID, assigned by the server.
    /// </summary>
    public static uint PlayerEntityId { get; set; }

    /// <summary>
    ///     The player's character name, set from the initial SelfProfile packet.
    /// </summary>
    public static string PlayerName { get; set; } = string.Empty;

    /// <summary>
    ///     Whether the player has attained master class status. Set from the SelfProfile packet's EnableMasterQuestMetaData flag.
    /// </summary>
    public static bool IsMaster { get; set; }

    /// <summary>
    ///     Active spell/effect animations currently playing in the world.
    /// </summary>
    public static List<Animation> ActiveEffects { get; } = [];

    /// <summary>
    ///     Active projectile animations currently in flight.
    /// </summary>
    public static List<Projectile> ActiveProjectiles { get; } = [];

    /// <summary>
    ///     Authoritative player attributes (stats, HP/MP, exp, etc.).
    /// </summary>
    public static PlayerAttributes Attributes { get; } = new();

    /// <summary>
    ///     Authoritative bulletin board / mail state.
    /// </summary>
    public static Board Board { get; } = new();

    /// <summary>
    ///     Authoritative chat and orange bar message state.
    /// </summary>
    public static Chat Chat { get; } = new();

    /// <summary>
    ///     Active creature death dissolve animations.
    /// </summary>
    public static List<EntityRemovalAnimation> DyingEffects { get; } = [];

    /// <summary>
    ///     Authoritative equipment state.
    /// </summary>
    public static Equipment Equipment { get; } = new();

    /// <summary>
    ///     Authoritative exchange (trade) state.
    /// </summary>
    public static Exchange Exchange { get; } = new();

    /// <summary>
    ///     Derived per-frame state (sort order, hover, tile under cursor) populated only by
    ///     <see cref="Brigid.Screens.WorldScreen" />.Update; NOT authoritative game state like Inventory/Equipment.
    /// </summary>
    public static DrawState CurrentFrame { get; } = new();

    /// <summary>
    ///     Authoritative group/party membership state.
    /// </summary>
    public static GroupState Group { get; } = new();

    /// <summary>
    ///     Authoritative group invite state.
    /// </summary>
    public static GroupInvite GroupInvite { get; } = new();

    /// <summary>
    ///     Authoritative inventory state with gold tracking.
    /// </summary>
    public static Inventory Inventory { get; } = new();

    /// <summary>
    ///     Authoritative NPC dialog/menu interaction state.
    /// </summary>
    public static NpcInteraction NpcInteraction { get; } = new();

    /// <summary>
    ///     Authoritative skill book state with cooldown timers.
    /// </summary>
    public static SkillBook SkillBook { get; } = new();

    /// <summary>
    ///     Authoritative spell book state with cooldown timers.
    /// </summary>
    public static SpellBook SpellBook { get; } = new();

    /// <summary>
    ///     Authoritative server-controlled user option toggles.
    /// </summary>
    public static UserOptions UserOptions { get; } = new();

    /// <summary>
    ///     Authoritative online players list state.
    /// </summary>
    public static WorldList WorldList { get; } = new();

    /// <summary>
    ///     Per-ability metadata for the player's class, parsed from the client's SClass meta file (the class is only
    ///     revealed by the self-profile packet, so this stays null until the first one arrives). Look an ability up by
    ///     name with <see cref="Data.Models.AbilityMetadata.TryGet" />.
    /// </summary>
    public static AbilityMetadata? AbilityMetadata { get; set; }

    /// <summary>
    ///     Adds or updates an aisling entity from a DisplayAisling packet.
    /// </summary>
    public static void AddOrUpdateAisling(DisplayUserPacket args)
    {
        if (!Entities.TryGetValue(args.Id, out var entity))
        {
            entity = new WorldEntity
            {
                Id = args.Id,
                Type = ClientEntityType.Aisling
            };

            Entities[args.Id] = entity;
        }

        SortVersion++;

        entity.Type = ClientEntityType.Aisling;
        entity.TileX = args.X;
        entity.TileY = args.Y;
        entity.Direction = (Direction)(byte)args.Direction;
        entity.Name = args.Name;
        entity.NameTagStyle = (NameTagStyle)args.NameTagStyle;
        entity.GroupBoxText = args.GroupName;

        switch (args.Appearance)
        {
            //morph mode (creature form)
            case CreatureSpriteAppearance creatureForm:
                //wire creature sprites carry the 0x4000 creature-range offset; strip it (see the creature
                //branch in AddOrUpdateVisibleEntities)
                entity.SpriteId = (ushort)(creatureForm.Sprite - 0x4000);
                entity.Appearance = null;
                entity.IsHidden = false;
                entity.IsTransparent = false;
                entity.IsDead = false;

                //sprite-form players default to a small lantern when the server hasn't sent one
                if (entity.LanternSize == LanternSize.None)
                    entity.LanternSize = LanternSize.Small;

                break;

            case EquipmentAppearance eq:
                entity.SpriteId = 0;

                //wire body byte: high nibble = body form, low nibble = pants dye (0 = undyed)
                var pantsColor = (byte)(eq.BodySprite & 0x0F);
                var bodySprite = (BodySprite)(eq.BodySprite & 0xF0);

                //an aisling is invisible either via the wire "Hide" transparent flag (Hybrasyl sends the normal body
                //plus the flag) or the retail MaleInvis/FemaleInvis body form. Fully hidden (admin) is the bodiless
                //form + flag and is not drawn at all (IsHidden). The see-through case is drawn translucent.
                var isInvisibleForm = bodySprite is BodySprite.MaleInvis or BodySprite.FemaleInvis;
                entity.IsHidden = eq.IsHidden && eq.BodySprite == 0;
                entity.IsTransparent = !entity.IsHidden && (eq.IsHidden || isInvisibleForm);
                entity.IsDead = bodySprite is BodySprite.MaleGhost or BodySprite.FemaleGhost;
                entity.LanternSize = (LanternSize)eq.LanternSize;
                entity.RestPosition = (RestPosition)eq.RestPosition;

                entity.Appearance = new AislingAppearance
                {
                    Gender = DataUtilities.DetermineGender(bodySprite),
                    //an invisible aisling renders as the gender-neutral "invisible" body outline (mb003) with its real
                    //equipment drawn on top, the whole avatar translucent (IsTransparent). A visible aisling uses its
                    //normal body form.
                    BodySpriteId = entity.IsTransparent ? 3 : GetBodySpriteId(bodySprite),
                    BodyColor = eq.BodyColor,
                    HeadSprite = eq.HeadSprite,
                    HeadColor = (DisplayColor)eq.HeadColor,
                    FaceSprite = eq.FaceSprite,
                    ArmorSprite1 = eq.ArmorSprite1,
                    ArmorSprite2 = eq.ArmorSprite2,
                    ArmorColor = DisplayColor.Default,
                    OvercoatSprite = eq.OvercoatSprite,
                    OvercoatColor = (DisplayColor)eq.OvercoatColor,
                    BootsSprite = eq.BootsSprite,
                    BootsColor = (DisplayColor)eq.BootsColor,
                    WeaponSprite = eq.WeaponSprite,
                    ShieldSprite = eq.ShieldSprite,
                    Accessory1Sprite = eq.AccessorySprite1,
                    Accessory1Color = (DisplayColor)eq.AccessoryColor1,
                    Accessory2Sprite = eq.AccessorySprite2,
                    Accessory2Color = (DisplayColor)eq.AccessoryColor2,
                    Accessory3Sprite = eq.AccessorySprite3,
                    Accessory3Color = (DisplayColor)eq.AccessoryColor3,
                    PantsColor = pantsColor == 0 ? null : (DisplayColor)pantsColor
                };

                //diagnostic: dump every aisling appearance packet (wire fields + Brigid's derived flags) to
                //notice-debug.log to reverse-engineer how retail marks stealthed/invisible players. Remove once the
                //client-side sight gate is built.
                NoticeDebugLog.Write(
                    $"[appearance] id={args.Id} name='{args.Name}' nameTag={args.NameTagStyle}({(NameTagStyle)args.NameTagStyle}) "
                    + $"bodyByte=0x{eq.BodySprite:X2} form={bodySprite} wireHide={eq.IsHidden} "
                    + $"head={eq.HeadSprite} arm1={eq.ArmorSprite1} arm2={eq.ArmorSprite2} coat={eq.OvercoatSprite} "
                    + $"weap={eq.WeaponSprite} shield={eq.ShieldSprite} "
                    + $"=> IsHidden={entity.IsHidden} IsTransparent={entity.IsTransparent} bodyId={entity.Appearance?.BodySpriteId}");

                break;
        }

        AnimationSystem.CancelAllAnimations(entity);
    }

    /// <summary>
    ///     Adds or updates visible entities (creatures + ground items) from a batch packet.
    /// </summary>
    public static void AddOrUpdateVisibleEntities(DrawObjectsPacket args)
    {
        foreach (var obj in args.Objects)
        {
            if (!Entities.TryGetValue(obj.Id, out var entity))
            {
                entity = new WorldEntity
                {
                    Id = obj.Id
                };
                Entities[obj.Id] = entity;
            }

            entity.TileX = obj.X;
            entity.TileY = obj.Y;
            entity.SpriteId = obj.Sprite;
            entity.Appearance = null;

            switch (obj)
            {
                case CreatureWorldObject creature:
                    entity.Type = ClientEntityType.Creature;
                    //wire creature sprites carry the 0x4000 creature-range offset; strip it so mns/asset-pack
                    //lookups (which use the real sprite id) resolve.
                    entity.SpriteId = (ushort)(creature.Sprite - 0x4000);
                    entity.CreatureType = (CreatureType)creature.Type;
                    entity.Direction = (Direction)creature.Direction;
                    entity.Name = creature.Name;

                    break;

                case ItemWorldObject groundItem:
                    entity.Type = ClientEntityType.GroundItem;
                    //wire item sprites carry the 0x8000 item-range flag; strip it so pack lookups resolve
                    entity.SpriteId = (ushort)(entity.SpriteId & 0x7FFF);
                    entity.ItemColor = groundItem.Color;

                    break;
            }

            AnimationSystem.CancelAllAnimations(entity);
        }

        SortVersion++;
    }

    /// <summary>
    ///     Clears all tracked entities and active effects. Call on map change.
    /// </summary>
    public static void Clear()
    {
        Entities.Clear();
        ActiveEffects.Clear();
        ActiveProjectiles.Clear();

        foreach (var dying in DyingEffects)
            dying.Dispose();

        DyingEffects.Clear();
        SortVersion++;
    }

    /// <summary>
    ///     Resets all character-specific state. Call on logout / character switch before any new character data arrives.
    /// </summary>
    public static void ResetAll()
    {
        Clear();
        InvalidateChantCache();
        PlayerEntityId = 0;
        PlayerName = string.Empty;
        Inventory.Clear();
        SkillBook.Clear();
        SpellBook.Clear();
        Equipment.Clear();
        Attributes.Clear();
        Chat.Clear();
        Board.CloseSession();
        Group.ResetAll();
        GroupInvite.Clear();
        NpcInteraction.Close();
        Exchange.Close();
        WorldList.Clear();
        UserOptions.ClearServerSettings();

        //character-scoped: a re-login as a different class must not resolve ability names against the old SClass table.
        AbilityMetadata = null;
    }

    /// <summary>
    ///     Returns tile positions of all blocking entities (creatures except WalkThrough, and aislings excluding the player).
    /// </summary>
    public static List<IPoint> GetBlockedPoints()
    {
        var blocked = new List<IPoint>();

        foreach (var entity in Entities.Values)
        {
            if (entity.Id == PlayerEntityId)
                continue;

            if (!IsBlockingEntity(entity))
                continue;

            blocked.Add(new Point(entity.TileX, entity.TileY));
        }

        return blocked;
    }

    //MaleInvis/FemaleInvis are intentionally absent: an invisible aisling is forced to the mb003 outline via the
    //IsTransparent path in AddOrUpdateAisling, so this only maps visible body forms.
    private static int GetBodySpriteId(BodySprite bodySprite)
        => bodySprite switch
        {
            BodySprite.MaleGhost or BodySprite.FemaleGhost => 2,
            BodySprite.MaleJester                          => 4,
            _                                              => 1
        };

    /// <summary>
    ///     Returns an entity by ID, or null if not tracked.
    /// </summary>
    public static WorldEntity? GetEntity(uint id) => Entities.GetValueOrDefault(id);

    /// <summary>
    ///     Returns the first entity at the specified tile, prioritizing creatures/aislings over ground items.
    /// </summary>
    public static WorldEntity? GetEntityAt(int tileX, int tileY)
    {
        WorldEntity? groundItem = null;

        foreach (var entity in Entities.Values)
        {
            if ((entity.TileX != tileX) || (entity.TileY != tileY))
                continue;

            //prefer clickable entities over ground items
            if (entity.Type is ClientEntityType.Aisling or ClientEntityType.Creature)
                return entity;

            groundItem ??= entity;
        }

        return groundItem;
    }

    /// <summary>
    ///     Returns the first ground item at the specified tile, or null.
    /// </summary>
    public static WorldEntity? GetGroundItemAt(int tileX, int tileY)
    {
        foreach (var entity in Entities.Values)
            if ((entity.Type == ClientEntityType.GroundItem) && (entity.TileX == tileX) && (entity.TileY == tileY))
                return entity;

        return null;
    }

    /// <summary>
    ///     Returns the player entity, or null if not yet tracked.
    /// </summary>
    public static WorldEntity? GetPlayerEntity() => Entities.GetValueOrDefault(PlayerEntityId);

    /// <summary>
    ///     Returns all entities sorted by depth (TileX + TileY), then by TileX ascending. Reuses an internal buffer to avoid
    ///     per-frame allocation.
    /// </summary>
    public static IReadOnlyList<WorldEntity> GetSortedEntities()
    {
        if (SortVersion == LastSortedVersion)
            return SortBuffer;

        LastSortedVersion = SortVersion;

        SortBuffer.Clear();
        SortBuffer.AddRange(Entities.Values);

        SortBuffer.Sort(static (a, b) =>
        {
            var depthCmp = a.SortDepth.CompareTo(b.SortDepth);

            if (depthCmp != 0)
                return depthCmp;

            var tileCmp = a.TileX.CompareTo(b.TileX);

            if (tileCmp != 0)
                return tileCmp;

            //newer entities (higher id) sort later so they render on top
            return a.Id.CompareTo(b.Id);
        });

        return SortBuffer;
    }

    public static Dictionary<uint, WorldEntity>.ValueCollection GetEntities() => Entities.Values;

    /// <summary>
    ///     Updates a tracked entity's facing direction from a server CreatureTurn packet.
    /// </summary>
    public static void HandleCreatureTurn(uint id, Direction direction)
    {
        if (!Entities.TryGetValue(id, out var entity))
            return;

        // Only cancel animations when the direction actually changes. A turn to the same direction
        // the entity is already walking/facing is a no-op and must not snap the walk to its destination.
        if (entity.Direction == direction)
            return;

        entity.Direction = direction;
        AnimationSystem.CancelAllAnimations(entity);
    }

    /// <summary>
    ///     Updates a tracked entity's position and starts its walk animation from a server CreatureWalk packet.
    /// </summary>
    public static void HandleCreatureWalk(
        uint id,
        int oldX,
        int oldY,
        Direction direction,
        int? walkFrameCount = null)
    {
        if (!Entities.TryGetValue(id, out var entity))
            return;

        //compute new position from oldpoint + direction
        (var dx, var dy) = direction.ToTileOffset();
        entity.TileX = oldX + dx;
        entity.TileY = oldY + dy;
        entity.Direction = direction;

        SortVersion++;

        AnimationSystem.StartWalk(
            entity,
            direction,
            entity.UsesCreatureWalkTiming,
            walkFrameOverride: walkFrameCount);
    }

    /// <summary>
    ///     Updates the player entity's position and starts its walk animation after the server confirms the walk.
    /// </summary>
    public static void HandlePlayerWalk(Direction direction, int oldX, int oldY)
    {
        if (!Entities.TryGetValue(PlayerEntityId, out var entity))
            return;

        (var dx, var dy) = direction.ToTileOffset();
        entity.TileX = oldX + dx;
        entity.TileY = oldY + dy;
        entity.Direction = direction;
        SortVersion++;

        AnimationSystem.StartWalk(
            entity,
            direction,
            entity.UsesCreatureWalkTiming,
            true);
    }

    /// <summary>
    ///     Returns true if any blocking entity (aisling, non-WalkThrough creature) occupies the tile,
    ///     excluding the specified entity ID (typically the player).
    /// </summary>
    public static bool HasBlockingEntityAt(int tileX, int tileY, uint excludeId)
    {
        foreach (var entity in Entities.Values)
        {
            if ((entity.TileX != tileX) || (entity.TileY != tileY) || (entity.Id == excludeId))
                continue;

            if (IsBlockingEntity(entity))
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Returns true if there is a ground item at the specified tile.
    /// </summary>
    public static bool HasGroundItemAt(int tileX, int tileY) => GetGroundItemAt(tileX, tileY) is not null;

    /// <summary>
    ///     Invalidates the cached chant data, forcing a reload on the next spell/skill addition. Call after chant editing is
    ///     saved.
    /// </summary>
    public static void InvalidateChantCache()
    {
        CachedSkillChants = null;
        CachedSpellChants = null;
    }

    private static bool IsBlockingEntity(WorldEntity entity)
        => (entity.Type == ClientEntityType.Aisling)
           || ((entity.Type == ClientEntityType.Creature) && (entity.CreatureType != CreatureType.WalkThrough));

    private static string? LookupSkillChant(string? name)
    {
        if (string.IsNullOrEmpty(name) || !DataContext.LocalPlayerSettings.IsInitialized)
            return null;

        CachedSkillChants ??= DataContext.LocalPlayerSettings.LoadSkillChants();

        foreach (var entry in CachedSkillChants)
            if (entry.Name.EqualsI(name))
                return entry.Chant;

        return null;
    }

    private static string[]? LookupSpellChants(string? name)
    {
        if (string.IsNullOrEmpty(name) || !DataContext.LocalPlayerSettings.IsInitialized)
            return null;

        CachedSpellChants ??= DataContext.LocalPlayerSettings.LoadSpellChants();

        foreach (var entry in CachedSpellChants)
            if (entry.Name.EqualsI(name))
                return entry.Chants;

        return null;
    }

    /// <summary>
    ///     Marks the entity sort buffer as dirty so the next <see cref="GetSortedEntities" /> call re-sorts. Call when entity
    ///     positions are modified outside of WorldState methods (e.g. client-side prediction).
    /// </summary>
    public static void MarkSortDirty() => SortVersion++;

    /// <summary>
    ///     Re-applies chant data to all occupied skill/spell slots. Call after PlayerData becomes available (first
    ///     DisplayAisling for the player).
    /// </summary>
    public static void ReloadChants()
    {
        InvalidateChantCache();

        for (byte i = 1; i <= SpellBook.MAX_SLOTS; i++)
        {
            var spell = SpellBook.GetSlot(i);

            if (spell.IsOccupied)
            {
                var chants = LookupSpellChants(spell.AbilityName);

                SpellBook.SetSlot(
                    i,
                    spell.Sprite,
                    spell.Name,
                    spell.SpellType,
                    spell.Prompt,
                    spell.CastLines,
                    chants);
            }
        }

        for (byte i = 1; i <= SkillBook.MAX_SLOTS; i++)
        {
            var skill = SkillBook.GetSlot(i);

            if (skill.IsOccupied)
            {
                var chant = LookupSkillChant(skill.AbilityName);

                SkillBook.SetSlot(
                    i,
                    skill.Sprite,
                    skill.Name,
                    chant);
            }
        }
    }

    /// <summary>
    ///     Removes an entity from tracking.
    /// </summary>
    public static void RemoveEntity(uint id)
    {
        Entities.Remove(id);
        SortVersion++;
    }

    /// <summary>
    ///     Subscribes to ConnectionManager events and routes them to state mutations.
    ///     Call once at startup after ConnectionManager is constructed.
    /// </summary>
    public static void SubscribeTo(ConnectionManager connection)
    {
        connection.OnAddSkillToPane += args =>
        {
            var chant = LookupSkillChant(args.Name);

            SkillBook.SetSlot(
                args.Slot,
                args.Icon,
                args.Name,
                chant);
        };

        connection.OnRemoveSkillFromPane += args => SkillBook.ClearSlot(args.Slot);

        connection.OnAddSpellToPane += args =>
        {
            var chants = LookupSpellChants(args.Name);

            SpellBook.SetSlot(
                args.Slot,
                args.Icon,
                args.Name,
                (SpellType)(byte)args.UseType,
                args.Prompt,
                args.CastLines,
                chants);
        };

        connection.OnRemoveSpellFromPane += args => SpellBook.ClearSlot(args.Slot);

        connection.OnCooldown += args =>
        {
            if (args.IsSkill)
                SkillBook.SetCooldown(args.Slot, args.Seconds);
            else
                SpellBook.SetCooldown(args.Slot, args.Seconds);
        };

        //inventory
        connection.OnAddItemToPane += args => Inventory.SetSlot(
            args.Slot,
            args.Sprite,
            (DisplayColor)args.Color,
            args.Name,
            args.Stackable,
            args.Count,
            (int)args.MaxDurability,
            (int)args.CurrentDurability);

        connection.OnRemoveItemFromPane += args => Inventory.ClearSlot(args.Slot);

        //equipment
        connection.OnEquipment += args => Equipment.SetSlot(
            (EquipmentSlot)(byte)args.Slot,
            args.Sprite,
            (DisplayColor)args.Color,
            args.Name,
            (int)args.MaxDurability,
            (int)args.CurrentDurability);

        connection.OnDisplayUnequip += args => Equipment.ClearSlot((EquipmentSlot)(byte)args.Slot);

        //attributes (stats, hp/mp, etc.) — gold also routed to inventory
        connection.OnAttributes += args =>
        {
            Attributes.Update(args);

            if (args.Experience is { } exp)
                Inventory.SetGold(exp.Gold);
        };

        //exchange
        connection.OnDisplayExchange += args =>
        {
            switch (args)
            {
                case StartExchangeResponsePacket start:
                    Exchange.Start(start.OtherUserId, start.OtherUserName);

                    break;

                case RequestExchangeAmountPacket req:
                    Exchange.RequestAmount(req.SourceSlot);

                    break;

                case AddExchangeItemResponsePacket add:
                    Exchange.AddItem(
                        add.RightSide,
                        add.ExchangeIndex,
                        add.Sprite,
                        (DisplayColor)add.Color,
                        add.Name);

                    break;

                case SetExchangeGoldResponsePacket gold:
                    Exchange.SetGold(gold.RightSide, (int)gold.GoldAmount);

                    break;

                //confirm byte: 1 = the other side accepted (exchange still open), 0 = completed
                //(Hybrasyl ServerPackets/Exchange.cs writes Side ? 0 : 1; the message rides both forms)
                case AcceptExchangeResponsePacket accept:
                    if (accept.RightSide)
                        Exchange.SetOtherAccepted();
                    else
                        Exchange.Close(accept.Message);

                    break;

                case CancelExchangeResponsePacket cancel:
                    Exchange.Close(cancel.Message);

                    break;
            }
        };

        //npc dialog/menu
        connection.OnDisplayDialog += args => NpcInteraction.ShowDialog(args);
        connection.OnDisplayMenu += args => NpcInteraction.ShowMenu(args);

        //board/mail
        connection.OnDisplayBoard += args =>
        {
            switch (args)
            {
                case BoardListPacket list:
                    Board.ShowBoardList(list.Boards);

                    break;

                case BoardIndexPacket index:
                    var isPublic = index.ResponseType == BoardResponseType.PublicBoard;

                    var entries = index.Messages
                                       .Select(m => new MailEntry(
                                           (short)m.PostId,
                                           m.Author,
                                           m.Month,
                                           m.Day,
                                           //subjects can carry embedded line breaks (e.g. retail post 306); flatten them
                                           //to a single space so each list row stays on one line like retail.
                                           m.Subject.ReplaceLineEndings(" "),
                                           m.Highlight))
                                       .ToList();

                    //DALib carries no append cursor (old StartPostId); always replace.
                    Board.ShowPostList(index.BoardId, entries, isPublic);

                    break;

                case BoardPostPacket post:
                    if (post.PostId == 0)
                    {
                        Board.HandleResponse("No such post.", false);

                        break;
                    }

                    //RefreshFlag is the enable-prev-button byte (Hybrasyl sends 0x03 for mail)
                    NoticeDebugLog.Write(
                        $"[Board] post id={post.PostId} type={post.ResponseType} refreshFlag=0x{post.RefreshFlag:X2}");
                    Board.ShowPost(
                        (short)post.PostId,
                        post.Author,
                        post.Month,
                        post.Day,
                        post.Subject,
                        post.Body,
                        post.RefreshFlag != 0);

                    break;

                case BoardResultPacket result:
                    Board.HandleResponse(result.Message, result.Success);

                    break;
            }
        };

        //group invite
        connection.OnDisplayGroupInvite += args => GroupInvite.Set(args);

        //world list (online players)
        connection.OnWorldList += args =>
        {
            //class byte: bits 0-2 = base class, bit 3 = guilded (Hybrasyl sends a plain class byte;
            //Chaos-convention servers pack the guild bit)
            var entries = args.Users
                              .Select(m => new WorldListEntry(
                                  m.Name,
                                  m.Title,
                                  (BaseClass)(m.Class & 7),
                                  m.IsMaster,
                                  (m.Class & 8) != 0,
                                  (WorldListColor)m.Color,
                                  (Chaos.DarkAges.Definitions.SocialStatus)(byte)m.SocialStatus))
                              .ToList();

            WorldList.Update(entries, args.TotalUserCount ?? (ushort)args.Users.Count);
        };
    }

    /// <summary>
    ///     Advances all active spell/effect animations and cooldown timers by the given elapsed time.
    /// </summary>
    public static void UpdateEffects(float elapsedMs)
    {
        SkillBook.Update(elapsedMs);
        SpellBook.Update(elapsedMs);

        for (var i = ActiveEffects.Count - 1; i >= 0; i--)
        {
            var effect = ActiveEffects[i];
            effect.ElapsedMs += elapsedMs;

            while (effect.ElapsedMs >= effect.FrameIntervalMs)
            {
                effect.CurrentFrame++;
                effect.ElapsedMs -= effect.FrameIntervalMs;
            }

            if (effect.IsComplete)
                ActiveEffects.RemoveAt(i);
        }

        for (var i = DyingEffects.Count - 1; i >= 0; i--)
        {
            var dying = DyingEffects[i];
            dying.Update(elapsedMs);

            if (dying.IsComplete)
            {
                dying.Dispose();
                DyingEffects.RemoveAt(i);
            }
        }
    }
}