#region
using Brigid.Collections;
using Brigid.Controls.Generic;
using Brigid.Data;
using Brigid.Data.AssetPacks;
using Brigid.Data.Repositories;
using Brigid.Data.Utilities;
using Brigid.Extensions;
using Brigid.Models;
using Brigid.Networking;
using Brigid.Networking.Definitions;
using Brigid.Rendering.Models;
using Brigid.Systems;
using Brigid.ViewModel;
using Chaos.DarkAges.Definitions;
using Chaos.Extensions.Common;
using Chaos.Geometry.Abstractions.Definitions;
using DALib.Drawing;
using DALib.Networking.Packets.Server;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Screens;

public sealed partial class WorldScreen
{
    #region Server Event Handlers
    //--- entity display / removal ---

    private void HandleDisplayAisling(DisplayUserPacket args)
    {
        //update player name in hud when the player's own aisling is displayed.
        //a hidden aisling (Hide/invisibility) is broadcast with a blank name so no floating name tag renders
        //over the invisible sprite — but that blank must NOT reach the persistent HUD "ID" box (which shows who
        //you are, not your on-map visibility). Guard on a non-empty name so hide/unhide never clears it; the
        //on-map tag still hides correctly via entity.Name in AddOrUpdateAisling.
        if (args.Id == Game.Connection.AislingId && !string.IsNullOrEmpty(args.Name))
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

    //--- chat / messages ---

    private void HandleDisplayPublicMessage(PublicMessagePacket args)
    {
        var messageType = (PublicMessageType)args.Type;
        var entityExists = WorldState.GetEntity(args.SourceId) is not null;

        if (messageType == PublicMessageType.Chant)
        {
            if (entityExists)
                Overlays.AddChantOverlay(args.SourceId, args.Message);

            return;
        }

        var entity = WorldState.GetEntity(args.SourceId);
        var isNpc = entity is not null && entity.Type is not ClientEntityType.Aisling;

        var color = messageType switch
        {
            PublicMessageType.Shout => TextColors.Shout,
            _                       => LegendColors.White
        };

        if (!isNpc || ClientSettings.RecordNpcChat)
            WorldState.Chat.AddMessage(args.Message, color);

        if (entity is null)
            return;

        var isShout = messageType == PublicMessageType.Shout;
        Overlays.AddChatBubble(args.SourceId, args.Message, isShout);
    }

    private void HandleServerMessage(SystemMessagePacket args)
    {
        //Hybrasyl extension value — not part of the retail block DALib names or the switch below models
        if (args.MessageType == HybrasylMessageType.MarkdownNotice)
        {
            MarkdownNotice.Show(args.Message);

            return;
        }

        switch ((ServerMessageType)(byte)args.MessageType)
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
                MarkdownNotice.Hide();

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

        if (dialog is null || (dialog.DialogType == NpcDialogType.Close))
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
        //the BankShopPanel (ShowItems) suppresses the portrait entirely — don't load/own an illustration for it
        if (NpcSession.ChromeSuppressed)
        {
            NpcSession.SetPortrait(null, false);

            return;
        }

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
    ///     Attempts to load a full-art NPC illustration. Resolves <paramref name="npcName" /> through the merged
    ///     NPCIllust metadata (npci.tbl + server NPCIllust metafile) to get the portrait key for
    ///     <paramref name="variant" /> — for Hybrasyl this is the literal value of the XML <c>Portrait</c> attribute
    ///     (e.g. <c>"inn.spf"</c>, <c>"Gobalt"</c>). The modern <c>npc_portraits</c> pack is probed with that key
    ///     first; on miss, falls back to <c>npcbase.dat</c> SPF lookup which only succeeds when the key happens to
    ///     name a real SPF file in the archive. Returns null if neither path produces an image (caller falls through
    ///     to the entity sprite portrait).
    /// </summary>
    private static Texture2D? TryLoadNpcIllustration(string npcName, byte variant)
    {
        var illustMeta = DataContext.MetaFiles.GetNpcIllustrationMetadata();

        if (!illustMeta.Illustrations.TryGetValue(npcName, out var filenames) || (filenames.Count == 0))
            return null;

        if (variant >= filenames.Count)
            return null;

        var portraitKey = filenames[variant];

        var portraitPack = AssetPackRegistry.GetNpcPortraitPack();

        if (portraitPack is not null && portraitPack.TryGetIllustration(portraitKey, out var packImage) && packImage is not null)
            using (packImage)
                return TextureConverter.ToTexture2D(packImage);

        if (!DatArchives.Npcbase.TryGetValue(portraitKey, out var entry))
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

    private void HandleRedirectReceived(RedirectInfo info)
    {
        RedirectInProgress = true;

        //server-initiated world transfer (dachaidh, express ship, Temuair↔Medenia): cover the reconnect gap
        //with the map loading screen and stop drawing the now-stale map, matching retail's black screen +
        //progress bar. a logout redirect (TargetState=Login) is bound for the login screen, so skip it there.
        if (info.TargetState == ConnectionState.World)
        {
            MapPreloaded = false;
            MapLoading.Show();
        }
    }

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

        //reply to a scroll-paging request: the list itself tracks whether it asked for a page. append only while that
        //list is still open on this board; if the user left before the reply arrived, drop it — never reopen the board.
        if (board.IsPublicBoard)
        {
            if (ArticleList.IsPaging)
            {
                if (ArticleList.Visible && (ArticleList.BoardId == board.BoardId))
                    ArticleList.AppendEntries(posts);
                else
                    ArticleList.CancelPaging();

                return;
            }
        } else if (MailList.IsPaging)
        {
            if (MailList.Visible && (MailList.BoardId == board.BoardId))
                MailList.AppendEntries(posts);
            else
                MailList.CancelPaging();

            return;
        }

        //fresh open (or server-pushed board, e.g. signpost click): replace the list. ensure the session is open first —
        //the server can send board data directly without going through the board list.
        if (!board.IsSessionOpen)
            board.OpenSession();

        HideAllBoardControls();

        if (board.IsPublicBoard)
        {
            ArticleList.ShowArticles(board.BoardId, posts);
            ArticleList.SetHighlightEnabled(IsGameMaster);
        } else
            MailList.ShowMailList(board.BoardId, posts);
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
        if (WorldState.GroupInvite.Current is not { } response)
            return;

        switch (response)
        {
            case GroupPromptPacket prompt:
            {
                var sourceName = prompt.SourceName;

                //RecruitAsk: request-to-join. Retail behavior: the leader's client silently
                //auto-forwards as TryInvite with no UI prompt. Ref: docs/research/group-ui-original-re.md
                //§5.1 / §7.1 (verified round-2). The orange-bar notice is a QoL addition retail omits.
                if (prompt.ResponseType == GroupResponseType.RecruitAsk)
                {
                    WorldState.Chat.AddOrangeBarMessage($"{sourceName} wants to join your group.");
                    Game.Connection.SendGroupInvite(ClientGroupSwitch.TryInvite, sourceName);

                    break;
                }

                //Ask: a standard group invitation.
                WorldState.Chat.AddOrangeBarMessage($"{sourceName} invites you to join a group.");

                if (!ClientSettings.UseGroupWindow)
                {
                    Game.Connection.SendGroupInvite(ClientGroupSwitch.AcceptInvite, sourceName);

                    break;
                }

                ShowGroupInvitePopup($"{sourceName} invites you to join a group.", sourceName);

                break;
            }

            case GroupRecruitInfoPacket recruit:
            {
                var info = recruit.Info;
                var sourceName = info.RecruiterName;

                if (sourceName.EqualsI(WorldState.PlayerName))
                {
                    WorldState.Group.MarkGroupBoxActive();
                    GroupPanel.ShowRecruitOwnerEdit(info);
                } else
                    GroupBoxViewer.ShowAsViewer(sourceName, info);

                break;
            }
        }
    }

    private void ShowGroupInvitePopup(string message, string sourceName)
    {
        if (Root is null)
            return;

        //high ZIndex so the popup floats above all panels (StatusBook, GroupTabControl, dialogs, ...).
        //10 matches DisconnectPopup, the only other interruption-class modal.
        var popup = new OkPopupMessageControl(true)
        {
            Name = "GroupInvitePopup",
            ZIndex = 10
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

    private void HandleSelfProfile(SelfProfilePacket args)
    {
        //DALib's self-profile carries no master-quest flag; default off.
        WorldState.IsMaster = false;

        //nation emblem and text
        StatusBook.SetNation(args.NationFlag);

        //social status display
        var status = SocialStatusPicker.CurrentStatus;
        StatusBook.SetEmoticonState((byte)status, UiComponentRepository.GetSocialStatusName(status));

        //populate and show the status book
        StatusBook.SetPlayerInfo(
            WorldHud.PlayerName,
            args.ClassName,
            args.GuildName,
            args.GuildRank,
            args.CurrentTitle);

        //legend marks
        var marks = args.Legend
                        .Select(m => new LegendMarkEntry(
                            m.Text,
                            MapMarkColor((MarkColor)m.Color),
                            m.Icon,
                            m.Prefix))
                        .ToList();

        StatusBook.SetLegendMarks(marks);

        //ability metadata (skills/spells from sclass file)
        var abilityMetadata = DataContext.MetaFiles.GetAbilityMetadata(args.Class);

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

            foreach (var mark in args.Legend)
                completedEventIds.Add(mark.Prefix);

            StatusBook.SetEvents(
                eventMetadata,
                completedEventIds,
                (BaseClass)args.Class,
                false);
        } else
            StatusBook.ClearEvents();

        //family info — DALib self-profile has no spouse field; derived below from the group string.
        StatusBook.SetFamilyInfo(string.Empty);
        LoadPlayerFamilyList();

        //paperdoll — render the player's full aisling at south-facing idle
        var playerEntity = WorldState.GetPlayerEntity();

        if (playerEntity?.Appearance is { } appearance)
            StatusBook.SetPaperdoll(Game.AislingRenderer, in appearance);

        //group open state — server is source of truth, sync all ui
        StatusBook.SetGroupOpen(args.CanGroup);
        WorldState.UserOptions.SetValue(12, args.CanGroup);
        WorldHud.SetGroupOpen(args.CanGroup);

        //group members — parse groupstring into state, ui subscribes via event
        if (!string.IsNullOrEmpty(args.GroupStatusText))
        {
            if (args.GroupStatusText.StartsWithI(GROUP_MEMBERS_PREFIX))
                WorldState.Group.ParseAndSet(args.GroupStatusText);
            else if (args.GroupStatusText.StartsWithI(SPOUSE_PREFIX))
            {
                var spouseName = args.GroupStatusText[SPOUSE_PREFIX.Length..]
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

    private void HandleOtherProfile(ProfilePacket args)
    {
        var marks = args.Legend
                        .Select(m => new LegendMarkEntry(
                            m.Text,
                            MapMarkColor((MarkColor)m.Color),
                            m.Icon,
                            m.Prefix))
                        .ToList();

        OtherProfile.Show(args, marks, Game.AislingRenderer);
    }

    //--- animations / effects / sound ---

    private void HandleBodyAnimation(PlayerAnimationPacket args)
    {
        var entity = WorldState.GetEntity(args.SourceId);

        if (entity is null)
            return;

        //emotes are body animations — ignore if any body anim or emote overlay is already playing
        if ((entity.AnimState == EntityAnimState.BodyAnim) || (entity.ActiveEmoteFrame >= 0))
            return;

        var bodyAnimation = (BodyAnimation)args.Animation;

        //creatures use their mpffile attack frame counts; aislings use epf suffix-based frame counts
        if (entity.Type == ClientEntityType.Creature)
        {
            var animInfo = Game.CreatureRenderer.GetAnimInfo(entity.SpriteId);

            if (animInfo is { } info)
                AnimationSystem.StartCreatureBodyAnimation(
                    entity,
                    bodyAnimation,
                    args.Speed,
                    in info);
        } else
        {
            (_, var framesPerDir, _, _) = AnimationSystem.ResolveBodyAnimParams(bodyAnimation);

            if (framesPerDir > 0)
            {
                if (entity.Appearance.HasValue && !Game.AislingRenderer.HasArmorAnimation(entity.Appearance.Value, bodyAnimation))
                    return;

                AnimationSystem.StartBodyAnimation(entity, bodyAnimation, args.Speed);
            } else if (DataUtilities.IsEmote(bodyAnimation))
            {
                //emote overlay — face/bubble icon composited into the aisling sprite
                (var startFrame, var frameCount, var durationMs) = AnimationSystem.ResolveEmoteFrames(bodyAnimation);

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
    }

    //TargetAnimation values in [PROJECTILE_ANIMATION_BASE, PROJECTILE_ANIMATION_MAX_EXCLUSIVE) are MEFC projectiles;
    //the meffect id is recovered by subtracting the base.
    private const int PROJECTILE_ANIMATION_BASE = 10000;
    private const int PROJECTILE_ANIMATION_MAX_EXCLUSIVE = 12000;

    private void HandleAnimation(SpellAnimationPacket args)
    {
        //projectile (MEFC): targeted form with a projectile-range target animation
        if (!args.IsAreaEffect
            && args is { SourceId: > 0, TargetAnimation: >= PROJECTILE_ANIMATION_BASE and < PROJECTILE_ANIMATION_MAX_EXCLUSIVE })
        {
            var meffectId = args.TargetAnimation - PROJECTILE_ANIMATION_BASE;
            CreateProjectile(meffectId, args.SourceId, args.TargetId);

            if (args.SourceAnimation > 0)
                CreateEffect(args.SourceAnimation, args.Speed, args.SourceId);

            return;
        }

        //ground-targeted (area) effect
        if (args.IsAreaEffect && (args.TargetAnimation > 0))
            CreateEffect(
                args.TargetAnimation,
                args.Speed,
                targetTileX: args.X,
                targetTileY: args.Y);

        //entity-targeted effect on target
        if (!args.IsAreaEffect && args is { TargetId: > 0, TargetAnimation: > 0 })
            CreateEffect(args.TargetAnimation, args.Speed, args.TargetId);

        //source-side effect (caster visual)
        if (!args.IsAreaEffect && args is { SourceId: > 0, SourceAnimation: > 0 })
            CreateEffect(args.SourceAnimation, args.Speed, args.SourceId);
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

    private void HandleSound(PlaySoundPacket args)
    {
        if (args.IsMusic)
        {
            if (args.MusicTrack is { } track)
                Game.SoundSystem.PlayMusic(track);
        } else
            Game.SoundSystem.PlaySound(args.Sound);
    }

    //--- world / map / doors ---

    private void HandleWorldMap(WorldMapPacket args) => WorldMap.Show(args);

    private void HandleDoor(DoorPacket args)
    {
        if (MapFile is null)
            return;

        foreach (var door in args.Doors)
        {
            if (door.X >= MapFile.Width || door.Y >= MapFile.Height)
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

    private void HandleEffect(StatusBarPacket args)
        => WorldHud.EffectBar.SetEffect((byte)args.Icon, (EffectColor)(byte)args.Color);

    private void HandleHealthBar(HealthBarPacket args)
    {
        Overlays.AddOrResetHealthBar(args.SourceId, args.HealthPercent);

        if (args.Sound != 0xFF)
            Game.SoundSystem.PlaySound(args.Sound);
    }

    private void HandleLightLevel(LightLevelPacket args) => DarknessRenderer.OnLightLevel((LightLevel)args.LightLevel);

    private void HandleMetaDataSyncComplete()
    {
        DarknessRenderer.ReloadMetadata();
        DarknessRenderer.ReapplyLightLevel();
        DataContext.MetaFiles.BuildItemIndex();
    }

    //--- notepad ---

    private void HandleDisplayReadonlyNotepad(ReadonlyPaperPacket args)
    {
        ItemTooltip.Hide();

        Notepad.ShowReadonly(
            (byte)args.Type,
            args.Width,
            args.Height,
            args.Text);
    }

    private void HandleDisplayEditableNotepad(EditablePaperPacket args)
    {
        ItemTooltip.Hide();

        Notepad.ShowEditable(
            args.Slot,
            (byte)args.Type,
            args.Width,
            args.Height,
            args.Text);
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

    private void HandleExitResponse(ConfirmExitPacket args)
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

        //server transfer (e.g. Temuair↔Medenia) reconnected straight back into World: clear the redirect
        //latch so a genuine drop afterward still surfaces the "connection lost" popup.
        if (newState == ConnectionState.World)
        {
            RedirectInProgress = false;

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