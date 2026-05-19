#region
using Chaos.Client.Collections;
using Chaos.Client.Controls.Generic;
using Chaos.Client.Data;
using Chaos.Client.Data.Repositories;
using Chaos.Client.Data.Utilities;
using Chaos.Client.Extensions;
using Chaos.Client.Models;
using Chaos.Client.Networking;
using Chaos.Client.Networking.Definitions;
using Chaos.Client.Rendering.Models;
using Chaos.Client.Systems;
using Chaos.Client.ViewModel;
using Chaos.DarkAges.Definitions;
using Chaos.Extensions.Common;
using Chaos.Geometry.Abstractions.Definitions;
using Chaos.Networking.Entities.Server;
using DALib.Drawing;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Screens;

public sealed partial class WorldScreen
{
    #region Server Event Handlers
    //--- entity display / removal ---

    private void HandleDisplayAisling(DisplayAislingArgs args)
    {
        //update player name in hud when the player's own aisling is displayed
        if (args.Id == Game.Connection.AislingId)
        {
            WorldState.PlayerName = args.Name;
            UpdateHuds(HudOps.SetPlayerName, args.Name);
            UpdateHuds(HudOps.SetServerName, Game.Connection.ServerName);
            DataContext.LocalPlayerSettings.Initialize(args.Name);
            LoadPlayerFamilyList();
            LoadPlayerFriendList();
            LoadPlayerMacros();
            WorldState.ReloadChants();
            PlayerPortrait = LoadPortraitFile(args.Name);
            StatusBook.SetProfileText(LoadProfileText());
        }

        //check for idle animation ("04") frames on this aisling's body
        var entity = WorldState.GetEntity(args.Id);

        if (entity?.Appearance is { } appearance)
        {
            entity.IdleAnimFrameCount = Game.AislingRenderer.GetIdleAnimFrameCount(in appearance);

            //start idle cycling if entity is currently idle
            if (entity.AnimState == EntityAnimState.Idle)
                AnimationSystem.ResetToIdle(entity);
        }
    }

    private void HandleRemoveEntity(uint id)
    {
        //capture creature sprite for death dissolve before removing from worldstate
        var entity = WorldState.GetEntity(id);

        if (entity is { Type: ClientEntityType.Creature })
            CreateDyingEffect(entity);

        //clean up aisling composited texture cache
        Game.AislingRenderer.RemoveCachedEntity(id);

        //clean up all overlay caches (name tag, chat bubble, health bar, chant)
        Overlays.RemoveEntity(id);

        //clean up cached debug label texture
        DebugRenderer.RemoveEntity(id);

        //remove entity from worldstate (chaosgame skips removal when worldscreen is active)
        WorldState.RemoveEntity(id);
    }

    private void CreateDyingEffect(WorldEntity entity)
    {
        var creatureRenderer = Game.CreatureRenderer;
        var animInfo = creatureRenderer.GetAnimInfo(entity.SpriteId);

        if (animInfo is null)
            return;

        var info = animInfo.Value;
        (var frameIndex, var flip) = AnimationSystem.GetCreatureFrame(entity, in info);

        var spriteFrame = creatureRenderer.GetFrame(entity.SpriteId, frameIndex);

        if (spriteFrame is null)
            return;

        var frame = spriteFrame.Value;

        var dyingEffect = new EntityRemovalAnimation(
            Device,
            frame.Texture,
            entity.TileX,
            entity.TileY,
            frame.CenterX,
            frame.CenterY,
            frame.Left,
            frame.Top,
            flip);

        WorldState.DyingEffects.Add(dyingEffect);
    }

    //--- movement ---

    /// <summary>
    ///     Client-side prediction: sends Walk packet and immediately starts the walk animation locally without waiting for
    ///     server confirmation. The server response reconciles position if needed.
    /// </summary>
    private void PredictAndWalk(WorldEntity player, Direction direction)
    {
        //bounds check — don't walk off the map edge
        (var dx, var dy) = direction.ToTileOffset();
        var newX = player.TileX + dx;
        var newY = player.TileY + dy;

        if (MapFile is null || (newX < 0) || (newY < 0) || (newX >= MapFile.Width) || (newY >= MapFile.Height))
            return;

        //swimming gate — retail behavior, off by default, toggled via GlobalSettings.RequireSwimmingSkill
        if (GlobalSettings.RequireSwimmingSkill
            && player.IsOnSwimmingTile
            && !IsGameMaster
            && !WorldState.SkillBook.HasSkillByName("swimming"))
        {
            WorldState.Chat.AddMessage("You need to learn how to swim.", Color.White);

            return;
        }

        //collision check — gm bypasses all collision
        if (!IsGameMaster && !IsTilePassable(newX, newY))
            return;

        Game.Connection.Walk(direction);

        //one more ack outstanding from the server
        InFlightWalkAcks++;

        //predict position locally
        player.TileX = newX;
        player.TileY = newY;
        WorldState.MarkSortDirty();

        var walkFrames = player.UsesCreatureWalkTiming ? Game.CreatureRenderer.GetWalkFrameCount(player.SpriteId) : null;

        AnimationSystem.StartWalk(
            player,
            direction,
            player.UsesCreatureWalkTiming,
            true,
            walkFrames);
        UpdateHuds(HudOps.SetCoords, player.TileX, player.TileY);
    }

    private void HandleClientWalkResponse(Direction direction, int oldX, int oldY)
    {
        var player = WorldState.GetPlayerEntity();

        if (player is null)
            return;

        //if any predicted walk is still un-acked, this packet is the FIFO ack for one of them.
        //treat it as a silent confirmation: the predicted state already reflects the move,
        //and any later Location packet will override if there was a true divergence.
        if (InFlightWalkAcks > 0)
        {
            InFlightWalkAcks--;

            return;
        }

        //no prediction in flight — this is a genuine server-initiated walk (push tile, knockback,
        //admin teleport). snap to the server's source position and animate the walk to the destination.
        QueuedWalkDirection = null;

        (var dx, var dy) = direction.ToTileOffset();
        var serverX = oldX + dx;
        var serverY = oldY + dy;

        player.TileX = serverX;
        player.TileY = serverY;
        WorldState.MarkSortDirty();

        var walkFrames = player.UsesCreatureWalkTiming ? Game.CreatureRenderer.GetWalkFrameCount(player.SpriteId) : null;

        AnimationSystem.StartWalk(
            player,
            direction,
            player.UsesCreatureWalkTiming,
            true,
            walkFrames);

        UpdateHuds(HudOps.SetCoords, serverX, serverY);
        Pathfinding.Clear();
    }

    //--- attributes ---

    private void HandleAttributes(AttributesArgs args)
        => IsGameMaster = args.StatUpdateType.HasFlag(StatUpdateType.GameMasterA)
                          || args.StatUpdateType.HasFlag(StatUpdateType.GameMasterB);

    //--- chat / messages ---

    private void HandleDisplayPublicMessage(DisplayPublicMessageArgs args)
    {
        var entityExists = WorldState.GetEntity(args.SourceId) is not null;

        if (args.PublicMessageType == PublicMessageType.Chant)
        {
            if (entityExists)
                Overlays.AddChantOverlay(args.SourceId, args.Message);

            return;
        }

        var entity = WorldState.GetEntity(args.SourceId);
        var isNpc = entity is not null && entity.Type is not ClientEntityType.Aisling;

        var color = args.PublicMessageType switch
        {
            PublicMessageType.Shout => TextColors.Shout,
            _                       => LegendColors.White
        };

        if (!isNpc || ClientSettings.RecordNpcChat)
            WorldState.Chat.AddMessage(args.Message, color);

        if (entity is null)
            return;

        var isShout = args.PublicMessageType == PublicMessageType.Shout;
        Overlays.AddChatBubble(args.SourceId, args.Message, isShout);
    }

    private void HandleServerMessage(ServerMessageArgs args)
    {
        switch (args.ServerMessageType)
        {
            case ServerMessageType.Whisper:
                WorldState.Chat.AddMessage(args.Message, TextColors.Whisper);
                WorldState.Chat.AddOrangeBarMessage(args.Message, TextColors.Whisper);
                SystemMessagePane.AddMessage(args.Message, TextColors.Whisper);

                break;

            case ServerMessageType.GroupChat:
                WorldState.Chat.AddMessage(args.Message, TextColors.GroupChat);
                WorldState.Chat.AddOrangeBarMessage(args.Message, TextColors.GroupChat);
                SystemMessagePane.AddMessage(args.Message, TextColors.GroupChat);

                break;

            case ServerMessageType.GuildChat:
                WorldState.Chat.AddMessage(args.Message, TextColors.GuildChat);
                WorldState.Chat.AddOrangeBarMessage(args.Message, TextColors.GuildChat);
                SystemMessagePane.AddMessage(args.Message, TextColors.GuildChat);

                break;

            case ServerMessageType.ActiveMessage:
                WorldState.Chat.AddOrangeBarMessage(args.Message);
                SystemMessagePane.AddMessage(args.Message);

                break;

            case ServerMessageType.OrangeBar1
                 or ServerMessageType.OrangeBar2
                 or ServerMessageType.OrangeBar3
                 or ServerMessageType.AdminMessage
                 or ServerMessageType.OrangeBar5:
                WorldState.Chat.AddOrangeBarMessage(args.Message);

                break;

            case ServerMessageType.PersistentMessage:
                UpdateHuds(HudOps.ShowPersistentMessage, args.Message);

                break;

            case ServerMessageType.ScrollWindow:
                TextPopup.Show(args.Message);

                break;

            case ServerMessageType.NonScrollWindow:
                TextPopup.Show(args.Message, PopupStyle.NonScroll);

                break;

            case ServerMessageType.WoodenBoard:
                TextPopup.Show(args.Message, PopupStyle.Wooden);

                break;

            case ServerMessageType.UserOptions:
                ParseUserOptions(args.Message);

                break;

            case ServerMessageType.ClosePopup:
                TextPopup.Hide();

                break;

            default:
                WorldState.Chat.AddOrangeBarMessage(args.Message);

                break;
        }
    }

    /// <summary>
    ///     Parses the server's UserOptions response. Two formats:
    ///     Full request: "0{desc}:{state}\t{desc}:{state}\t..." — '0' prefix, digits stripped, options ordered by position.
    ///     Single toggle: "{digit}{desc}:{state}" — leading digit identifies the option (1-based).
    /// </summary>
    private void ParseUserOptions(string message)
    {
        if (message.Length < 2)
            return;

        //single option toggle response: "{digit}{description,-25}:{on/off,-3}"
        if (message[0] != '0')
        {
            if (!char.IsDigit(message[0]))
                return;

            var optionIndex = message[0] - '1';

            if (optionIndex is < 0 or >= UserOptions.SETTING_COUNT)
                return;

            ParseOptionEntry(optionIndex, message[1..]);

            return;
        }

        //full request response: "0{opt1_desc}:{state}\t{opt2_desc}:{state}\t..."
        //leading '0' prefix, then 8 options in order with digits stripped
        var entries = message[1..]
            .Split('\t', StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; (i < entries.Length) && (i < 8); i++)
            ParseOptionEntry(i, entries[i]);
    }

    /// <summary>
    ///     Parses a single option entry in the format "{description,-25}:{ON/OFF,-3}" and applies it.
    /// </summary>
    private void ParseOptionEntry(int optionIndex, string entry)
    {
        if (!UserOptions.IsServerSetting(optionIndex))
            return;

        var colonIdx = entry.LastIndexOf(':');

        if (colonIdx < 1)
            return;

        var stateStr = entry[(colonIdx + 1)..]
            .Trim();
        var isOn = stateStr.StartsWithI("ON");

        //server settings: use the full formatted text as the display name (includes :on/:off)
        SettingsDialog.SetSettingName(optionIndex, entry.TrimEnd());
        WorldState.UserOptions.SetValue(optionIndex, isOn);
    }

    //--- npc dialog / menu ---

    private void HandleDialogChanged()
    {
        var dialog = WorldState.NpcInteraction.CurrentDialog;

        if (dialog is null || (dialog.DialogType == DialogType.CloseDialog))
        {
            NpcSession.HideAll();

            return;
        }

        NpcSession.ShowDialog(dialog);
        RenderNpcSessionPortrait();
    }

    private void HandleMenuChanged()
    {
        var menu = WorldState.NpcInteraction.CurrentMenu;

        if (menu is null)
            return;

        NpcSession.ShowMenu(menu);
        RenderNpcSessionPortrait();
    }

    private void RenderNpcSessionPortrait()
    {
        //phase 1: try full-art illustration spf. The original DA client attempts this unconditionally for every
        //dialog/menu packet — the only gate is whether the NPC name matches an entry in the merged illustration
        //metadata (npci.tbl inside npcbase.dat + server-pushed NPCIllust metafile). IllustrationIndex picks which
        //filename variant to load when a name has multiple.
        if (!string.IsNullOrEmpty(NpcSession.NpcName))
        {
            var illustTexture = TryLoadNpcIllustration(NpcSession.NpcName, NpcSession.IllustrationIndex);

            if (illustTexture is not null)
            {
                NpcSession.SetPortrait(illustTexture, true);

                return;
            }
        }

        //phase 2: fall back to entity sprite portrait based on entitytype
        if (NpcSession.PortraitSpriteId == 0)
        {
            NpcSession.SetPortrait(null, false);

            return;
        }

        switch (NpcSession.SourceEntityType)
        {
            case EntityType.Creature:
            {
                var frame = RenderCreaturePortrait(NpcSession.PortraitSpriteId);

                if (frame is not null)
                    NpcSession.SetPortrait(frame.Value);
                else
                    NpcSession.SetPortrait(null, false);

                break;
            }

            case EntityType.Item:
            {
                var sprite = Game.ItemRenderer.GetSprite(NpcSession.PortraitSpriteId, (byte)NpcSession.PortraitColor);
                NpcSession.SetPortrait(sprite?.Texture, false);

                break;
            }

            default:
                NpcSession.SetPortrait(null, false);

                break;
        }
    }

    /// <summary>
    ///     Attempts to load a full-art NPC illustration SPF from <c>npcbase.dat</c>. Looks up <paramref name="npcName" />
    ///     in the merged illustration metadata (npci.tbl + server NPCIllust metafile) and picks the filename at
    ///     <paramref name="variant" />. Returns null if the NPC has no entries, the variant index is out of range,
    ///     or the SPF file is missing.
    /// </summary>
    private static Texture2D? TryLoadNpcIllustration(string npcName, byte variant)
    {
        var illustMeta = DataContext.MetaFiles.GetNpcIllustrationMetadata();

        if (!illustMeta.Illustrations.TryGetValue(npcName, out var filenames) || (filenames.Count == 0))
            return null;

        if (variant >= filenames.Count)
            return null;

        var spfFileName = filenames[variant];

        if (!DatArchives.Npcbase.TryGetValue(spfFileName, out var entry))
            return null;

        var spf = SpfFile.FromEntry(entry);

        if (spf.Count == 0)
            return null;

        using var image = SpfRenderer.RenderFrame(spf, 0);

        return TextureConverter.ToTexture2D(image);
    }

    

    private SpriteFrame? RenderCreaturePortrait(ushort spriteId)
    {
        var info = Game.CreatureRenderer.GetAnimInfo(spriteId);

        if (info is null)
            return null;

        (var frameIndex, _) = AnimationSystem.GetCreatureIdleFrame(info.Value, Direction.Down);

        return Game.CreatureRenderer.GetFrame(spriteId, frameIndex);
    }

    private void HandleRefreshResponse()
        =>

            //server acknowledged the refresh request — re-center camera
            FollowPlayerCamera();

    //--- exchange ---

    private void HandleExchangeAmountRequested(byte fromSlot)
    {
        ItemAmount.X = Exchange.X + (Exchange.Width - ItemAmount.Width) / 2;
        ItemAmount.Y = Exchange.Y + (Exchange.Height - ItemAmount.Height) / 2;
        ItemAmount.ShowForSlot(fromSlot);

        //surface the slot's hover description (e.g. "Apple[ 10 ]") in the HUD bar while the popup
        //is open — matches retail behavior of pinning the operated-on item's tooltip text.
        WorldHud.SetDescription(WorldState.Inventory.GetSlot(fromSlot).Name);
    }

    private void HandleExchangeClosed(string? message)
    {
        if (!string.IsNullOrEmpty(message))
            ExchangeResultPopup.Show(message);
    }

    //--- board / mail ---

    private void HandleBoardResponse(string message, bool success)
    {
        if (success)
            PendingBoardSuccessAction?.Invoke();

        PendingBoardSuccessAction = null;
        BoardResponsePopup.Show(message);
    }

    private void HandleRedirectReceived(RedirectInfo _) => RedirectInProgress = true;

    private void HandleBoardListReceived()
    {
        var boards = WorldState.Board.AvailableBoards;

        if (boards is null or { Count: 0 })
            return;

        WorldState.Board.OpenSession();

        BoardList.ShowBoards(
            boards.Select(b => (b.BoardId, b.Name))
                  .ToList());
    }

    private void HandleBoardPostListChanged()
    {
        var board = WorldState.Board;
        var posts = board.Posts.ToList();

        //ensure session is open — server can send board data directly (e.g. tile click) without going through BoardList
        if (!board.IsSessionOpen)
            board.OpenSession();

        if (board.IsPublicBoard)
        {
            if (LoadingMoreBoardPosts && ArticleList.Visible && (ArticleList.BoardId == board.BoardId))
                ArticleList.AppendEntries(posts);
            else
            {
                HideAllBoardControls();
                ArticleList.ShowArticles(board.BoardId, posts);
                ArticleList.SetHighlightEnabled(IsGameMaster);
            }
        } else
        {
            if (LoadingMoreBoardPosts && MailList.Visible && (MailList.BoardId == board.BoardId))
                MailList.AppendEntries(posts);
            else
            {
                HideAllBoardControls();
                MailList.ShowMailList(board.BoardId, posts);
            }
        }

        LoadingMoreBoardPosts = false;
    }

    private void HandleBoardPostViewed()
    {
        var post = WorldState.Board.CurrentPost;

        if (post is not { } p)
            return;

        var board = WorldState.Board;

        //ensure session is open — server can send a post directly without going through BoardList
        if (!board.IsSessionOpen)
            board.OpenSession();

        HideAllBoardControls();

        if (board.IsPublicBoard)
        {
            ArticleRead.BoardId = board.BoardId;

            ArticleRead.ShowArticle(
                p.PostId,
                p.Author,
                p.MonthOfYear,
                p.DayOfMonth,
                p.Subject,
                p.Message,
                board.EnablePrevButton);
        } else
        {
            MailRead.BoardId = board.BoardId;

            MailRead.ShowMail(
                p.PostId,
                p.Author,
                p.MonthOfYear,
                p.DayOfMonth,
                p.Subject,
                p.Message,
                board.EnablePrevButton);
        }
    }

    //--- group ---

    private void HandleGroupInviteReceived()
    {
        var invite = WorldState.GroupInvite.Current;

        if (invite is null)
            return;

        var sourceName = invite.SourceName;

        switch (invite.ServerGroupSwitch)
        {
            case ServerGroupSwitch.Invite:
            {
                WorldState.Chat.AddOrangeBarMessage($"{sourceName} invites you to join a group.");

                if (!ClientSettings.UseGroupWindow)
                {
                    Game.Connection.SendGroupInvite(ClientGroupSwitch.AcceptInvite, sourceName);

                    break;
                }

                ShowGroupInvitePopup($"{sourceName} invites you to join a group.", sourceName);

                break;
            }

            case ServerGroupSwitch.RequestToJoin:
            {
                // Retail behavior: the leader's client silently auto-forwards as TryInvite
                // with no UI prompt. Ref: docs/research/group-ui-original-re.md §5.1 / §7.1
                // (verified round-2). The orange-bar notice is a QoL addition retail omits.
                WorldState.Chat.AddOrangeBarMessage($"{sourceName} wants to join your group.");
                Game.Connection.SendGroupInvite(ClientGroupSwitch.TryInvite, sourceName);

                break;
            }

            case ServerGroupSwitch.ShowGroupBox:
            {
                if (invite.GroupBoxInfo is not { } groupBoxInfo)
                {
                    WorldState.Chat.AddOrangeBarMessage($"{sourceName} has an open group box.");

                    break;
                }

                if (sourceName.EqualsI(WorldState.PlayerName))
                {
                    WorldState.Group.MarkGroupBoxActive();
                    GroupPanel.ShowRecruitOwnerEdit(groupBoxInfo);
                } else
                    GroupBoxViewer.ShowAsViewer(sourceName, groupBoxInfo);

                break;
            }
        }
    }

    private void ShowGroupInvitePopup(string message, string sourceName)
    {
        if (Root is null)
            return;

        var popup = new OkPopupMessageControl(true)
        {
            Name = "GroupInvitePopup"
        };
        Root.AddChild(popup);

        popup.OnOk += () =>
        {
            Game.Connection.SendGroupInvite(ClientGroupSwitch.AcceptInvite, sourceName);
            popup.Hide();
            Root.RemoveChild(popup.Name);
        };

        popup.OnCancel += () =>
        {
            popup.Hide();
            Root.RemoveChild(popup.Name);
        };

        popup.Show(message);
    }

    //--- profiles ---

    private void HandleEditableProfileRequest() => Game.Connection.SendEditableProfile(PlayerPortrait, StatusBook.GetProfileText());

    private static byte[] LoadPortraitFile(string name)
    {
        if (!DataContext.LocalPlayerSettings.IsInitialized || string.IsNullOrEmpty(name))
            return [];

        var jpgPath = DataContext.LocalPlayerSettings.GetFilePath($"{name}.jpg");

        if (File.Exists(jpgPath))
            return File.ReadAllBytes(jpgPath);

        var noExtPath = DataContext.LocalPlayerSettings.GetFilePath(name);

        if (File.Exists(noExtPath))
            return File.ReadAllBytes(noExtPath);

        return [];
    }

    private static string LoadProfileText()
    {
        if (!DataContext.LocalPlayerSettings.IsInitialized)
            return string.Empty;

        var profilePath = DataContext.LocalPlayerSettings.GetFilePath("profile.txt");

        return File.Exists(profilePath) ? File.ReadAllText(profilePath) : string.Empty;
    }

    private static void SaveProfileText(string text)
    {
        if (!DataContext.LocalPlayerSettings.IsInitialized)
            return;

        var profilePath = DataContext.LocalPlayerSettings.GetFilePath("profile.txt");
        File.WriteAllText(profilePath, text);
    }

    private void HandleSelfProfile(SelfProfileArgs args)
    {
        WorldState.IsMaster = args.EnableMasterQuestMetaData;

        //nation emblem and text
        StatusBook.SetNation((byte)args.Nation);

        //social status display
        var status = SocialStatusPicker.CurrentStatus;
        StatusBook.SetEmoticonState((byte)status, UiComponentRepository.GetSocialStatusName(status));

        //populate and show the status book
        StatusBook.SetPlayerInfo(
            WorldHud.PlayerName,
            args.DisplayClass,
            args.GuildName ?? string.Empty,
            args.GuildRank ?? string.Empty,
            args.Title ?? string.Empty);

        //legend marks
        var marks = args.LegendMarks
                        .Select(m => new LegendMarkEntry(
                            m.Text,
                            MapMarkColor(m.Color),
                            (byte)m.Icon,
                            m.Key))
                        .ToList();

        StatusBook.SetLegendMarks(marks);

        //ability metadata (skills/spells from sclass file)
        var abilityMetadata = DataContext.MetaFiles.GetAbilityMetadata((byte)args.BaseClass);

        if (abilityMetadata is not null)
            StatusBook.SetAbilityMetadata(abilityMetadata);
        else
            StatusBook.ClearSkills();

        //event metadata (quests from sevent files)
        var eventMetadata = DataContext.MetaFiles.GetEventMetadata();

        if (eventMetadata.Count > 0)
        {
            //build a set of completed event ids from legend marks for o(1) lookup
            var completedEventIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var mark in args.LegendMarks)
                completedEventIds.Add(mark.Key);

            StatusBook.SetEvents(
                eventMetadata,
                completedEventIds,
                args.BaseClass,
                args.EnableMasterQuestMetaData);
        } else
            StatusBook.ClearEvents();

        //family info
        StatusBook.SetFamilyInfo(args.SpouseName ?? string.Empty);
        LoadPlayerFamilyList();

        //paperdoll — render the player's full aisling at south-facing idle
        var playerEntity = WorldState.GetPlayerEntity();

        if (playerEntity?.Appearance is { } appearance)
            StatusBook.SetPaperdoll(Game.AislingRenderer, in appearance);

        //group open state — server is source of truth, sync all ui
        StatusBook.SetGroupOpen(args.GroupOpen);
        WorldState.UserOptions.SetValue(12, args.GroupOpen);
        WorldHud.SetGroupOpen(args.GroupOpen);

        //group members — parse groupstring into state, ui subscribes via event
        if (!string.IsNullOrEmpty(args.GroupString))
        {
            if (args.GroupString.StartsWithI(GROUP_MEMBERS_PREFIX))
                WorldState.Group.ParseAndSet(args.GroupString);
            else if (args.GroupString.StartsWithI(SPOUSE_PREFIX))
            {
                var spouseName = args.GroupString[SPOUSE_PREFIX.Length..]
                                     .Trim();
                StatusBook.SetFamilyInfo(spouseName);
                WorldState.Group.Clear();
            } else
                WorldState.Group.Clear();
        } else
            WorldState.Group.Clear();

        if (GroupHighlightRequested)
        {
            GroupHighlightRequested = false;
            ApplyGroupHighlight();
        } else if (SelfProfileRequested)
        {
            SelfProfileRequested = false;
            ShowStatusBook(SelfProfileRequestedTab);
            SelfProfileRequestedTab = StatusBookTab.Equipment;
        }
    }

    private void ApplyGroupHighlight()
    {
        GroupHighlightedIds.Clear();
        Game.AislingRenderer.ClearGroupTintCache();
        Game.CreatureRenderer.ClearTintCaches();

        var members = WorldState.Group.Members;

        if (members.Count == 0)
            return;

        var memberSet = new HashSet<string>(members, StringComparer.OrdinalIgnoreCase);

        foreach (var entity in WorldState.GetSortedEntities())
        {
            if (entity.Type != ClientEntityType.Aisling)
                continue;

            if ((entity.Id != WorldState.PlayerEntityId) && !string.IsNullOrEmpty(entity.Name) && memberSet.Contains(entity.Name))
                GroupHighlightedIds.Add(entity.Id);
        }

        if (GroupHighlightedIds.Count > 0)
            GroupHighlightTimer = 1000f;
    }

    private void ShowStatusBook(StatusBookTab tab = StatusBookTab.Equipment)
    {
        StatusBook.RefreshEquipment();

        if (WorldState.Attributes.Current is { } attrs)
            StatusBook.UpdateEquipmentStats(
                attrs.Str,
                attrs.Int,
                attrs.Wis,
                attrs.Con,
                attrs.Dex,
                attrs.Ac);

        StatusBook.SwitchTab(tab);
        StatusBook.Show();
    }

    private void HandleOtherProfile(OtherProfileArgs args)
    {
        var marks = args.LegendMarks
                        .Select(m => new LegendMarkEntry(
                            m.Text,
                            MapMarkColor(m.Color),
                            (byte)m.Icon,
                            m.Key))
                        .ToList();

        OtherProfile.Show(args, marks, Game.AislingRenderer);
    }

    //--- animations / effects / sound ---

    private void HandleBodyAnimation(BodyAnimationArgs args)
    {
        var entity = WorldState.GetEntity(args.SourceId);

        if (entity is null)
            return;

        //emotes are body animations — ignore if any body anim or emote overlay is already playing
        if ((entity.AnimState == EntityAnimState.BodyAnim) || (entity.ActiveEmoteFrame >= 0))
            return;

        //creatures use their mpffile attack frame counts; aislings use epf suffix-based frame counts
        if (entity.Type == ClientEntityType.Creature)
        {
            var animInfo = Game.CreatureRenderer.GetAnimInfo(entity.SpriteId);

            if (animInfo is { } info)
                AnimationSystem.StartCreatureBodyAnimation(
                    entity,
                    args.BodyAnimation,
                    args.AnimationSpeed,
                    in info);
        } else
        {
            (_, var framesPerDir, _, _) = AnimationSystem.ResolveBodyAnimParams(args.BodyAnimation);

            if (framesPerDir > 0)
            {
                if (entity.Appearance.HasValue && !Game.AislingRenderer.HasArmorAnimation(entity.Appearance.Value, args.BodyAnimation))
                    return;

                AnimationSystem.StartBodyAnimation(entity, args.BodyAnimation, args.AnimationSpeed);
            } else if (DataUtilities.IsEmote(args.BodyAnimation))
            {
                //emote overlay — face/bubble icon composited into the aisling sprite
                (var startFrame, var frameCount, var durationMs) = AnimationSystem.ResolveEmoteFrames(args.BodyAnimation);

                if (startFrame >= 0)
                {
                    entity.EmoteStartFrame = startFrame;
                    entity.EmoteFrameCount = frameCount;
                    entity.ActiveEmoteFrame = startFrame;
                    entity.EmoteDurationMs = durationMs;
                    entity.EmoteElapsedMs = 0;
                    entity.EmoteRemainingMs = durationMs;
                }
            }
        }

        if (args.Sound.HasValue)
            Game.SoundSystem.PlaySound(args.Sound.Value);
    }

    //TargetAnimation values in [PROJECTILE_ANIMATION_BASE, PROJECTILE_ANIMATION_MAX_EXCLUSIVE) are MEFC projectiles;
    //the meffect id is recovered by subtracting the base.
    private const int PROJECTILE_ANIMATION_BASE = 10000;
    private const int PROJECTILE_ANIMATION_MAX_EXCLUSIVE = 12000;

    private void HandleAnimation(AnimationArgs args)
    {
        if (args is { SourceId: > 0, TargetId: > 0, TargetAnimation: >= PROJECTILE_ANIMATION_BASE and < PROJECTILE_ANIMATION_MAX_EXCLUSIVE })
        {
            var meffectId = args.TargetAnimation - PROJECTILE_ANIMATION_BASE;
            CreateProjectile(meffectId, args.SourceId.Value, args.TargetId.Value);

            if (args is { SourceAnimation: > 0 })
                CreateEffect(args.SourceAnimation, args.AnimationSpeed, args.SourceId.Value);

            return;
        }

        //ground-targeted effect
        if (args is { TargetPoint: not null, TargetAnimation: > 0 })
            CreateEffect(
                args.TargetAnimation,
                args.AnimationSpeed,
                targetTileX: args.TargetPoint.Value.X,
                targetTileY: args.TargetPoint.Value.Y);

        //entity-targeted effect on target
        if (args is { TargetId: > 0, TargetAnimation: > 0 })
            CreateEffect(args.TargetAnimation, args.AnimationSpeed, args.TargetId.Value);

        //source-side effect (caster visual)
        if (args is { SourceId: > 0, SourceAnimation: > 0 })
            CreateEffect(args.SourceAnimation, args.AnimationSpeed, args.SourceId.Value);
    }

    private void CreateProjectile(int meffectId, uint sourceEntityId, uint targetEntityId)
    {
        var record = DataContext.Effects.GetMeffectRecord(meffectId);

        if (record is null)
            return;

        var sourceEntity = WorldState.GetEntity(sourceEntityId);
        var targetEntity = WorldState.GetEntity(targetEntityId);

        if (sourceEntity is null || targetEntity is null)
            return;

        if (MapFile is null)
            return;

        var sourceWorld = Camera.TileToWorld(sourceEntity.TileX, sourceEntity.TileY, MapFile.Height);
        var targetWorld = Camera.TileToWorld(targetEntity.TileX, targetEntity.TileY, MapFile.Height);

        var srcX = sourceWorld.X + DaLibConstants.HALF_TILE_WIDTH;
        var srcY = sourceWorld.Y + DaLibConstants.HALF_TILE_HEIGHT;
        var tgtX = targetWorld.X + DaLibConstants.HALF_TILE_WIDTH;
        var tgtY = targetWorld.Y + DaLibConstants.HALF_TILE_HEIGHT;

        var dx = tgtX - srcX;
        var dy = tgtY - srcY;
        var distance = MathF.Sqrt(dx * dx + dy * dy);

        if (distance < 1f)
            return;

        //direction matches server's DirectionalRelationTo (tile space): Up=0, Right=1, Down=2, Left=3
        var direction = GetProjectileDirection(
            targetEntity.TileX - sourceEntity.TileX,
            targetEntity.TileY - sourceEntity.TileY);

        WorldState.ActiveProjectiles.Add(
            new Projectile
            {
                TargetEntityId = targetEntityId,
                MeffectId = meffectId,
                CurrentX = srcX,
                CurrentY = srcY,
                LastKnownTargetX = tgtX,
                LastKnownTargetY = tgtY,
                Step = record.Step,
                StepDelayMs = record.StepDelay,
                InitialDistance = distance,
                ArcRatioV = record.ArcRatioV,
                ArcRatioH = record.ArcRatioH,
                FramesPerDirection = record.FramesPerDirection,
                Direction = direction
            });
    }

    private void CreateEffect(
        int effectId,
        ushort animationSpeed,
        uint? targetEntityId = null,
        int? targetTileX = null,
        int? targetTileY = null)
    {
        var info = Game.EffectRenderer.GetEffectInfo(effectId);

        if (info is null)
            return;

        (var frameCount, var fileIntervalMs, var isEfa, var blendMode) = info.Value;

        //efa effects use the interval from the file; epf effects use the packet's animation speed
        float frameIntervalMs = isEfa
            ? fileIntervalMs > 0 ? fileIntervalMs : 50
            : animationSpeed > 0
                ? animationSpeed
                : 50;

        //cancel any existing effect on the same entity — only one effect per entity at a time
        if (targetEntityId.HasValue)
            WorldState.ActiveEffects.RemoveAll(e => e.TargetEntityId == targetEntityId);

        WorldState.ActiveEffects.Add(
            new Animation
            {
                EffectId = effectId,
                TargetEntityId = targetEntityId,
                TileX = targetTileX,
                TileY = targetTileY,
                FrameCount = frameCount,
                FrameIntervalMs = frameIntervalMs,
                BlendMode = blendMode
            });
    }

    private void HandleSound(SoundArgs args)
    {
        if (args.IsMusic)
            Game.SoundSystem.PlayMusic(args.Sound);
        else
            Game.SoundSystem.PlaySound(args.Sound);
    }

    //--- world / map / doors ---

    private void HandleWorldMap(WorldMapArgs args) => WorldMap.Show(args);

    private void HandleDoor(DoorArgs args)
    {
        if (MapFile is null || args.Doors is null)
            return;

        foreach (var door in args.Doors)
        {
            if ((door.X < 0) || (door.X >= MapFile.Width) || (door.Y < 0) || (door.Y >= MapFile.Height))
                continue;

            //record the server-authoritative state for the Alt+right-click door menu's Open/Close label.
            //done before the sprite swap so the cache reflects packet truth even if DoorTable is a no-op.
            KnownDoorClosedState[(door.X, door.Y)] = door.Closed;

            var tile = MapFile.Tiles[door.X, door.Y];

            if (door.Closed)
            {
                //restore closed tile: find the open tile currently set and swap it back
                var closedLeft = DoorTable.GetClosedTileId(tile.LeftForeground);
                var closedRight = DoorTable.GetClosedTileId(tile.RightForeground);

                if (closedLeft.HasValue)
                    tile.LeftForeground = closedLeft.Value;

                if (closedRight.HasValue)
                    tile.RightForeground = closedRight.Value;
            } else
            {
                //open door: find the closed tile and swap to open
                var openLeft = DoorTable.GetOpenTileId(tile.LeftForeground);
                var openRight = DoorTable.GetOpenTileId(tile.RightForeground);

                if (openLeft.HasValue)
                    tile.LeftForeground = openLeft.Value;

                if (openRight.HasValue)
                    tile.RightForeground = openRight.Value;
            }
        }
    }

    private void HandleMapChangePending()
    {
        MapPreloaded = false;
        QueuedWalkDirection = null;
        Pathfinding.Clear();
        KnownDoorClosedState.Clear();
        //WorldMap.HideMap() intentionally not called here — retail sends MapChangePending (0x67) immediately
        //after the WorldMap (0x2E) packet, which would tear down the worldmap UI before the user could
        //see it. The retail client itself has no handler for 0x67. Worldmap teardown happens naturally
        //via Show()'s ClearNodes/ClearBackground on the next worldmap, or via Escape/click→new MapInfo.
        TownMapControl.Hide();

        //a true map transition is the one place we discard outstanding walk acks: any walks that were
        //in flight on the old map will not be ack'd on the new one. (the same-map F5 refresh path,
        //which also runs ClearTransientState, must NOT touch this counter — its acks are still in flight.)
        InFlightWalkAcks = 0;
    }

    //--- health / effects / light ---

    private void HandleEffect(EffectArgs args) => WorldHud.EffectBar.SetEffect(args.EffectIcon, args.EffectColor);

    private void HandleHealthBar(HealthBarArgs args)
    {
        Overlays.AddOrResetHealthBar(args.SourceId, args.HealthPercent);

        if (args.Sound.HasValue)
            Game.SoundSystem.PlaySound(args.Sound.Value);
    }

    private void HandleLightLevel(LightLevelArgs args) => DarknessRenderer.OnLightLevel(args.LightLevel);

    private void HandleMetaDataSyncComplete()
    {
        DarknessRenderer.ReloadMetadata();
        DarknessRenderer.ReapplyLightLevel();
        DataContext.MetaFiles.BuildItemIndex();
    }

    //--- notepad ---

    private void HandleDisplayReadonlyNotepad(DisplayReadonlyNotepadArgs args)
    {
        ItemTooltip.Hide();

        Notepad.ShowReadonly(
            (byte)args.NotepadType,
            args.Width,
            args.Height,
            args.Message);
    }

    private void HandleDisplayEditableNotepad(DisplayEditableNotepadArgs args)
    {
        ItemTooltip.Hide();

        Notepad.ShowEditable(
            args.Slot,
            (byte)args.NotepadType,
            args.Width,
            args.Height,
            args.Message);
    }

    //--- exit / state ---

    private const float EXIT_CONFIRM_SECONDS = 10f;
    //buggy/older servers (e.g. Hybrasyl, which enqueues the Redirect with a 1200ms TransmitDelay and
    //drops the user from WorldState before the queue flushes) close the socket without sending Redirect.
    //this window says "if disconnect arrives within N seconds of us sending the confirm, treat it as a
    //graceful exit instead of an unexpected drop."
    private const float EXIT_IN_PROGRESS_GRACE_SECONDS = 10f;

    private void BeginExit()
    {
        //guard against re-entry while the popup is already up
        if (ExitConfirmPopup.Visible)
            return;

        //retail-compat signal: announce the exit dialog opened. server responds with a cosmetic 0x4C
        //(no longer auto-confirms — user dismisses the popup or the timer expires before we send 0x0B [0]).
        Game.Connection.RequestExit(true);

        ExitConfirmPopup.Show("Logging out. Press OK to log out now.");
        ExitConfirmSecondsRemaining = EXIT_CONFIRM_SECONDS;
    }

    private void ConfirmExit()
    {
        ExitConfirmSecondsRemaining = 0f;

        if (ExitConfirmPopup.Visible)
            ExitConfirmPopup.Hide();

        Game.Connection.RequestExit(false);
        ExitInProgressSecondsRemaining = EXIT_IN_PROGRESS_GRACE_SECONDS;
    }

    private void HandleExitResponse(ExitResponseArgs args)
    {
        //server's 0x4C ack to the query phase. retail's 0x4C is a state-machine signal, not a control-flow
        //trigger — the user's click on the confirmation popup (or its 10s timeout) drives the actual exit.
        //hook left in place so phase 2 can update the popup text on server ack if desired.
    }

    private void HandleStateChanged(ConnectionState oldState, ConnectionState newState)
    {
        //server redirected us back to login (e.g., after logout)
        //state transitions go world → connecting → login, so just check for login arrival
        if (newState == ConnectionState.Login)
        {
            RedirectInProgress = false;
            ExitInProgressSecondsRemaining = 0f;
            PendingLoginSwitch = true;

            return;
        }

        if ((newState == ConnectionState.Disconnected) && !RedirectInProgress)
        {
            //defensive: a disconnect arriving within the exit-in-progress grace window is the expected
            //logout outcome on servers that drop the socket without flushing Redirect. transition to
            //login instead of showing the unexpected-disconnect popup.
            if (ExitInProgressSecondsRemaining > 0f)
            {
                ExitInProgressSecondsRemaining = 0f;
                PendingLoginSwitch = true;

                return;
            }

            //unexpected disconnect — show reconnect prompt
            DisconnectPopup.Show("Connection lost.");
        }
    }

    //--- helpers ---

    private static Color MapMarkColor(MarkColor color)
    {
        if (color == MarkColor.Invisible)
            return Color.Transparent;

        return LegendColors.Get((int)color);
    }
    #endregion

    //Up=0, Right=1, Down=2, Left=3 — matches server DirectionalRelationTo in tile space
    private static int GetProjectileDirection(int dtx, int dty)
    {
        var absDtx = Math.Abs(dtx);
        var absDty = Math.Abs(dty);

        if (absDtx > absDty)
            return dtx > 0 ? 1 : 3;

        if (absDty > 0)
            return dty < 0 ? 0 : 2;

        return 0;
    }
}