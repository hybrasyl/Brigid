#region
using Chaos.Client.Data;
using Chaos.Client.Data.Models;
using Chaos.Client.Extensions;
using Chaos.Client.Models;
using Chaos.Client.Networking;
using Chaos.Client.Systems;
using Chaos.Client.ViewModel;
using Chaos.DarkAges.Definitions;
using Chaos.Extensions.Common;
using Chaos.Geometry;
using Chaos.Geometry.Abstractions;
using Chaos.Geometry.Abstractions.Definitions;
using Chaos.Networking.Entities.Server;
#endregion

namespace Chaos.Client.Collections;

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
    ///     <see cref="Chaos.Client.Screens.WorldScreen" />.Update; NOT authoritative game state like Inventory/Equipment.
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
    ///     Adds or updates an aisling entity from a DisplayAisling packet.
    /// </summary>
    public static void AddOrUpdateAisling(DisplayAislingArgs args)
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
        entity.Direction = args.Direction;
        entity.Name = args.Name;
        entity.IsHidden = args.IsHidden;
        entity.IsTransparent = args.IsTransparent;
        entity.IsDead = args.IsDead;
        entity.LanternSize = args.LanternSize;
        entity.NameTagStyle = args.NameTagStyle;
        entity.RestPosition = args.RestPosition;
        entity.GroupBoxText = args.GroupBoxText;

        //check for morph mode (creature form)
        if (args.Sprite.HasValue)
        {
            entity.SpriteId = args.Sprite.Value;
            entity.Appearance = null;

            //sprite-form players default to a small lantern when the server hasn't sent one
            if (entity.LanternSize == LanternSize.None)
                entity.LanternSize = LanternSize.Small;
        } else
        {
            entity.SpriteId = 0;

            entity.Appearance = new AislingAppearance
            {
                Gender = args.BodySprite is BodySprite.Female or BodySprite.FemaleGhost ? Gender.Female : Gender.Male,
                BodySpriteId = GetBodySpriteId(args.BodySprite),
                BodyColor = (int)args.BodyColor,
                HeadSprite = args.HeadSprite,
                HeadColor = args.HeadColor,
                FaceSprite = args.FaceSprite,
                ArmorSprite = args.ArmorSprite1,
                ArmorColor = DisplayColor.Default,
                OvercoatSprite = args.OvercoatSprite,
                OvercoatColor = args.OvercoatColor,
                BootsSprite = args.BootsSprite,
                BootsColor = args.BootsColor,
                WeaponSprite = args.WeaponSprite,
                ShieldSprite = args.ShieldSprite,
                Accessory1Sprite = args.AccessorySprite1,
                Accessory1Color = args.AccessoryColor1,
                Accessory2Sprite = args.AccessorySprite2,
                Accessory2Color = args.AccessoryColor2,
                Accessory3Sprite = args.AccessorySprite3,
                Accessory3Color = args.AccessoryColor3,
                PantsColor = args.PantsColor
            };
        }

        AnimationSystem.CancelAllAnimations(entity);
    }

    /// <summary>
    ///     Adds or updates visible entities (creatures + ground items) from a batch packet.
    /// </summary>
    public static void AddOrUpdateVisibleEntities(DisplayVisibleEntitiesArgs args)
    {
        foreach (var obj in args.VisibleObjects)
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
                case CreatureInfo creature:
                    entity.Type = ClientEntityType.Creature;
                    entity.CreatureType = creature.CreatureType;
                    entity.Direction = creature.Direction;
                    entity.Name = creature.Name ?? string.Empty;

                    break;

                case GroundItemInfo groundItem:
                    entity.Type = ClientEntityType.GroundItem;
                    entity.ItemColor = (byte)groundItem.Color;

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

    private static int GetBodySpriteId(BodySprite bodySprite)
        => bodySprite switch
        {
            BodySprite.MaleGhost or BodySprite.FemaleGhost => 2,
            BodySprite.MaleInvis or BodySprite.FemaleInvis => 3,
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
            var chant = LookupSkillChant(args.Skill.PanelName);

            SkillBook.SetSlot(
                args.Skill.Slot,
                args.Skill.Sprite,
                args.Skill.PanelName,
                chant);
        };

        connection.OnRemoveSkillFromPane += args => SkillBook.ClearSlot(args.Slot);

        connection.OnAddSpellToPane += args =>
        {
            var chants = LookupSpellChants(args.Spell.PanelName);

            SpellBook.SetSlot(
                args.Spell.Slot,
                args.Spell.Sprite,
                args.Spell.PanelName,
                args.Spell.SpellType,
                args.Spell.Prompt,
                args.Spell.CastLines,
                chants);
        };

        connection.OnRemoveSpellFromPane += args => SpellBook.ClearSlot(args.Slot);

        connection.OnCooldown += args =>
        {
            if (args.IsSkill)
                SkillBook.SetCooldown(args.Slot, args.CooldownSecs);
            else
                SpellBook.SetCooldown(args.Slot, args.CooldownSecs);
        };

        //inventory
        connection.OnAddItemToPane += args => Inventory.SetSlot(
            args.Item.Slot,
            args.Item.Sprite,
            args.Item.Color,
            args.Item.Name,
            args.Item.Stackable,
            args.Item.Count ?? 0,
            args.Item.MaxDurability,
            args.Item.CurrentDurability);

        connection.OnRemoveItemFromPane += args => Inventory.ClearSlot(args.Slot);

        //equipment
        connection.OnEquipment += args => Equipment.SetSlot(
            args.Slot,
            args.Item.Sprite,
            args.Item.Color,
            args.Item.Name,
            args.Item.MaxDurability,
            args.Item.CurrentDurability);

        connection.OnDisplayUnequip += args => Equipment.ClearSlot(args.EquipmentSlot);

        //attributes (stats, hp/mp, etc.) — gold also routed to inventory
        connection.OnAttributes += args =>
        {
            Attributes.Update(args);
            Inventory.SetGold(args.Gold);
        };

        //exchange
        connection.OnDisplayExchange += args =>
        {
            switch (args.ExchangeResponseType)
            {
                case ExchangeResponseType.StartExchange:
                    Exchange.Start(args.OtherUserId!.Value, args.OtherUserName);

                    break;

                case ExchangeResponseType.RequestAmount:
                    if (args.FromSlot.HasValue)
                        Exchange.RequestAmount(args.FromSlot.Value);

                    break;

                case ExchangeResponseType.AddItem:
                    if (args is { RightSide: not null, ExchangeIndex: not null, ItemSprite: not null })
                        Exchange.AddItem(
                            args.RightSide.Value,
                            args.ExchangeIndex.Value,
                            args.ItemSprite.Value,
                            args.ItemColor ?? DisplayColor.Default,
                            args.ItemName);

                    break;

                case ExchangeResponseType.SetGold:
                    if (args is { RightSide: not null, GoldAmount: not null })
                        Exchange.SetGold(args.RightSide.Value, args.GoldAmount.Value);

                    break;

                case ExchangeResponseType.Accept:
                    if (args.PersistExchange == true)
                        Exchange.SetOtherAccepted();
                    else
                        Exchange.Close(args.Message);

                    break;

                case ExchangeResponseType.Cancel:
                    Exchange.Close(args.Message);

                    break;
            }
        };

        //npc dialog/menu
        connection.OnDisplayDialog += args => NpcInteraction.ShowDialog(args);
        connection.OnDisplayMenu += args => NpcInteraction.ShowMenu(args);

        //board/mail
        connection.OnDisplayBoard += args =>
        {
            switch (args.Type)
            {
                case BoardOrResponseType.BoardList:
                    if (args.Boards is not null)
                        Board.ShowBoardList(args.Boards);

                    break;

                case BoardOrResponseType.PublicBoard or BoardOrResponseType.MailBoard:
                    Board.IsBoardListPending = false;

                    if (args.Board is not null)
                    {
                        var isPublic = args.Type == BoardOrResponseType.PublicBoard;

                        var entries = args.Board
                                          .Posts
                                          .Select(p => new MailEntry(
                                              p.PostId,
                                              p.Author,
                                              p.MonthOfYear,
                                              p.DayOfMonth,
                                              p.Subject,
                                              p.IsHighlighted))
                                          .ToList();

                        if (args.StartPostId.HasValue)
                            Board.AppendPosts(entries);
                        else
                            Board.ShowPostList(args.Board.BoardId, entries, isPublic);
                    }

                    break;

                case BoardOrResponseType.PublicPost or BoardOrResponseType.MailPost:
                    if (args.Post is not null)
                    {
                        if (args.Post.PostId == 0)
                        {
                            Board.HandleResponse("No such post.", false);

                            break;
                        }

                        Board.ShowPost(
                            args.Post.PostId,
                            args.Post.Author,
                            args.Post.MonthOfYear,
                            args.Post.DayOfMonth,
                            args.Post.Subject,
                            args.Post.Message,
                            args.EnablePrevBtn);
                    }

                    break;

                case BoardOrResponseType.SubmitPostResponse
                     or BoardOrResponseType.DeletePostResponse
                     or BoardOrResponseType.HighlightPostResponse:
                    if (args.ResponseMessage is not null)
                        Board.HandleResponse(args.ResponseMessage, args.Success is true);

                    break;
            }
        };

        //group invite
        connection.OnDisplayGroupInvite += args => GroupInvite.Set(args);

        //world list (online players)
        connection.OnWorldList += args =>
        {
            var entries = args.CountryList
                              .Select(m => new WorldListEntry(
                                  m.Name,
                                  m.Title,
                                  m.BaseClass,
                                  m.IsMaster,
                                  m.IsGuilded,
                                  m.Color,
                                  m.SocialStatus))
                              .ToList();

            WorldList.Update(entries, args.WorldMemberCount);
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