#region
using Brigid.Collections;
using Brigid.Controls.Components;
using Brigid.Controls.World.Hud;
using Brigid.Controls.World.Hud.Panel;
using Brigid.Controls.World.Popups.Boards;
using Brigid.Controls.World.Popups.Options;
using Brigid.Extensions;
using Brigid.Systems;
using Chaos.DarkAges.Definitions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Screens;

public sealed partial class WorldScreen
{
    #region Server Event Wiring
    private void WireServerEvents()
    {
        //player identity
        Game.Connection.OnUserId += HandleUserId;

        //map assembly events
        Game.Connection.OnMapInfo += HandleMapInfo;
        Game.Connection.OnMapData += HandleMapData;
        Game.Connection.OnMapLoadComplete += HandleMapLoadComplete;
        Game.Connection.OnLocationChanged += HandleLocationChanged;

        //entity events
        //worldstate updates (entity add/remove/walk/turn) are wired in chaosgame so they
        //work during world entry before this screen exists. we subscribe here only for
        //screen-specific side effects (hud updates, cache cleanup).
        Game.Connection.OnDisplayAisling += HandleDisplayAisling;
        Game.Connection.OnRemoveEntity += HandleRemoveEntity;
        Game.Connection.OnClientWalkResponse += HandleClientWalkResponse;


        //chat events
        Game.Connection.OnDisplayPublicMessage += HandleDisplayPublicMessage;
        Game.Connection.OnServerMessage += HandleServerMessage;

        //npc dialog/menu
        WorldState.NpcInteraction.DialogChanged += HandleDialogChanged;
        WorldState.NpcInteraction.MenuChanged += HandleMenuChanged;

        //refresh response
        Game.Connection.OnRefreshResponse += HandleRefreshResponse;

        WorldState.Exchange.AmountRequested += HandleExchangeAmountRequested;
        WorldState.Exchange.Closed += HandleExchangeClosed;

        //board — subscribe to state events
        WorldState.Board.PostListChanged += HandleBoardPostListChanged;
        WorldState.Board.PostViewed += HandleBoardPostViewed;
        WorldState.Board.BoardListReceived += HandleBoardListReceived;
        WorldState.Board.ResponseReceived += HandleBoardResponse;

        //group invite — subscribe to state event
        WorldState.GroupInvite.Received += HandleGroupInviteReceived;

        //profiles
        Game.Connection.OnEditableProfileRequest += HandleEditableProfileRequest;
        Game.Connection.OnSelfProfile += HandleSelfProfile;
        Game.Connection.OnOtherProfile += HandleOtherProfile;

        //animations / effects / sound
        Game.Connection.OnBodyAnimation += HandleBodyAnimation;
        Game.Connection.OnAnimation += HandleAnimation;
        Game.Connection.OnSound += HandleSound;
        Game.Connection.OnCancelCasting += CastingSystem.CancelChant;

        //map transitions
        Game.Connection.OnMapChangePending += HandleMapChangePending;

        //logout / disconnect
        Game.Connection.OnExitResponse += HandleExitResponse;
        Game.Connection.StateChanged += HandleStateChanged;
        Game.Connection.OnRedirectReceived += HandleRedirectReceived;

        //health bars
        Game.Connection.OnHealthBar += HandleHealthBar;

        //status effects
        Game.Connection.OnEffect += HandleEffect;

        //light level
        Game.Connection.OnLightLevel += HandleLightLevel;

        //metadata sync — reload metadata consumers after server handshake completes
        Game.OnMetaDataSyncComplete += HandleMetaDataSyncComplete;

        //notepad popups
        Game.Connection.OnDisplayReadonlyNotepad += HandleDisplayReadonlyNotepad;
        Game.Connection.OnDisplayEditableNotepad += HandleDisplayEditableNotepad;

        //world map
        Game.Connection.OnWorldMap += HandleWorldMap;

        //doors
        Game.Connection.OnDoor += HandleDoor;
    }
    #endregion

    #region Exchange Wiring
    // Exchange subscriptions are intentionally layered across ExchangeControl and WorldScreen:
    //   - ExchangeControl subscribes to Started/ItemAdded/GoldSet/OtherAccepted/Closed — updates its own UI
    //   - WorldScreen (WireServerEvents) subscribes to AmountRequested (spawn amount popup) + Closed (screen-level teardown)
    // Closed is intentionally double-subscribed: the control hides itself, the screen runs side effects.
    // Don't collapse them — they serve different layers.
    private void WireExchange()
    {
        Exchange.OnOk += () => Game.Connection.SendExchangeInteraction(ExchangeRequestType.Accept, Exchange.OtherUserId);

        Exchange.OnCancel += () =>
        {
            Game.Connection.SendExchangeInteraction(ExchangeRequestType.Cancel, Exchange.OtherUserId);
            WorldState.Exchange.Close();
        };
    }
    #endregion

    #region NPC Session Wiring
    private void WireNpcSession()
    {
        NpcSession.OnClose += () =>
        {
            //only 0x30 dialogs are closed by echoing the current pursuitId/pursuitIndex back (0x3A-dialog-use.md).
            //Merchant menus (bank/shop, 0x2F shown / 0x39 responses) are NOT part of the dialog-pursuit state machine
            //— retail's own client closes them client-side with no packet, so we must not emit a 0x3A close here or a
            //retail server would see a bogus (pursuitId, 0) index and could drop the connection (±1 validation).
            if (NpcSession is { SourceId: { } sourceId, IsDialogOpcode: true })
                Game.Connection.SendDialogResponse(
                    NpcSession.SourceObjectType,
                    sourceId,
                    NpcSession.PursuitId,
                    NpcSession.DialogId);
        };

        NpcSession.OnTop += () =>
        {
            if (NpcSession.SourceId is { } sourceId)
                Game.Connection.ClickEntity(sourceId);
        };

        NpcSession.OnNext += () =>
        {
            if (NpcSession.SourceId is { } sourceId)
                Game.Connection.SendDialogResponse(
                    NpcSession.SourceObjectType,
                    sourceId,
                    NpcSession.PursuitId,
                    (ushort)(NpcSession.DialogId + 1));
        };

        NpcSession.OnPrevious += () =>
        {
            if (NpcSession.SourceId is { } sourceId)
                Game.Connection.SendDialogResponse(
                    NpcSession.SourceObjectType,
                    sourceId,
                    NpcSession.PursuitId,
                    (ushort)(NpcSession.DialogId - 1));
        };

        NpcSession.OnOptionSelected += optionIndex =>
        {
            if (NpcSession.SourceId is not { } sourceId)
                return;

            if (NpcSession.IsDialogOpcode)
                Game.Connection.SendDialogResponse(
                    NpcSession.SourceObjectType,
                    sourceId,
                    NpcSession.PursuitId,
                    (ushort)(NpcSession.DialogId + 1),
                    DialogArgsType.MenuResponse,
                    (byte)(optionIndex + 1));
            else
            {
                var pursuitId = NpcSession.GetOptionPursuitId(optionIndex);

                if (NpcSession.MenuArgs is not null)
                    Game.Connection.SendMenuResponse(
                        NpcSession.SourceObjectType,
                        sourceId,
                        pursuitId,
                        args: [NpcSession.MenuArgs]);
                else
                    Game.Connection.SendMenuResponse(NpcSession.SourceObjectType, sourceId, pursuitId);
            }
        };

        NpcSession.OnTextSubmit += text =>
        {
            if (NpcSession.SourceId is not { } sourceId)
                return;

            if (NpcSession.IsDialogOpcode)
            {
                //speak: broadcast the combined prompt + input + epilog as a public say first
                if (NpcSession.CurrentDialogType is DialogType.Speak)
                {
                    var sayParts = new[]
                    {
                        NpcSession.SpeakPrompt,
                        text,
                        NpcSession.SpeakEpilog
                    };

                    var sayText = string.Join(" ", sayParts.Where(s => !string.IsNullOrEmpty(s)));

                    Game.Connection.SendPublicMessage(sayText);
                }

                Game.Connection.SendDialogResponse(
                    NpcSession.SourceObjectType,
                    sourceId,
                    NpcSession.PursuitId,
                    (ushort)(NpcSession.DialogId + 1),
                    DialogArgsType.TextResponse,
                    args: [text]);
            } else
            {
                //include previous args for textentrywithargs
                var prevArgs = NpcSession.GetMenuTextPreviousArgs();

                if (prevArgs is not null)
                    Game.Connection.SendMenuResponse(
                        NpcSession.SourceObjectType,
                        sourceId,
                        NpcSession.PursuitId,
                        args:
                        [
                            prevArgs,
                            text
                        ]);
                else
                    Game.Connection.SendMenuResponse(
                        NpcSession.SourceObjectType,
                        sourceId,
                        NpcSession.PursuitId,
                        args: [text]);
            }
        };

        NpcSession.OnProtectedSubmit += (id, password) =>
        {
            if (NpcSession.SourceId is not { } sourceId)
                return;

            Game.Connection.SendDialogResponse(
                NpcSession.SourceObjectType,
                sourceId,
                NpcSession.PursuitId,
                (ushort)(NpcSession.DialogId + 1),
                DialogArgsType.TextResponse,
                args:
                [
                    id,
                    password
                ]);
        };

        NpcSession.OnMerchantItemSelected += selectedIndex =>
        {
            if (NpcSession.SourceId is not { } sourceId)
                return;

            var name = NpcSession.GetMerchantEntryName(selectedIndex);

            if (name is not null)
                Game.Connection.SendMenuResponse(
                    NpcSession.SourceObjectType,
                    sourceId,
                    NpcSession.PursuitId,
                    args: [name]);
        };

        NpcSession.OnListItemSelected += selectedIndex =>
        {
            if (NpcSession.SourceId is not { } sourceId)
                return;

            var slot = NpcSession.GetListEntrySlot(selectedIndex);

            if (slot is null)
                return;

            if (NpcSession.CurrentMenuType is MenuType.ShowPlayerItems or MenuType.ShowPlayerSkills or MenuType.ShowPlayerSpells)
                Game.Connection.SendMenuResponse(
                    NpcSession.SourceObjectType,
                    sourceId,
                    NpcSession.PursuitId,
                    slot.Value);
            else
            {
                var name = NpcSession.GetListEntryName(selectedIndex);

                if (name is not null)
                    Game.Connection.SendMenuResponse(
                        NpcSession.SourceObjectType,
                        sourceId,
                        NpcSession.PursuitId,
                        args: [name]);
            }
        };
    }
    #endregion

    #region Board/Mail Wiring
    private void WireBoardControls()
    {
        WireBoardsModal();

        DeleteConfirm.OnOk += () =>
        {
            PendingDeleteAction?.Invoke();
            PendingDeleteAction = null;
            DeleteConfirm.Hide();
        };

        DeleteConfirm.OnCancel += () =>
        {
            PendingDeleteAction = null;
            DeleteConfirm.Hide();
        };

        WorldState.Board.SessionClosed += HideAllBoardControls;
    }

    private void ToggleSocialStatusPicker()
    {
        if (SocialStatusPicker.Visible)
        {
            SocialStatusPicker.Hide();

            WorldHud.EmoteButton?.IsSelected = false;

            return;
        }

        var emoteBtn = WorldHud.EmoteButton;
        var viewport = WorldHud.ViewportBounds;

        if (emoteBtn is not null)
        {
            SocialStatusPicker.X = emoteBtn.ScreenX - SocialStatusPicker.Width / 2 + emoteBtn.Width / 2;
            SocialStatusPicker.Y = emoteBtn.ScreenY - SocialStatusPicker.Height - 2 + 24;
        } else
        {
            //fallback positioning when no emote button exists
            SocialStatusPicker.CenterHorizontallyIn(viewport);
            SocialStatusPicker.Y = viewport.Y + viewport.Height - SocialStatusPicker.Height;
        }

        if (SocialStatusPicker.X < viewport.X)
            SocialStatusPicker.X = viewport.X;

        if ((SocialStatusPicker.X + SocialStatusPicker.Width) > (viewport.X + viewport.Width))
            SocialStatusPicker.X = viewport.X + viewport.Width - SocialStatusPicker.Width;

        emoteBtn?.IsSelected = true;

        SocialStatusPicker.Show();
    }

    private bool IsAnyBoardPanelVisible() => BoardsModal.Visible;

    /// <summary>
    ///     Closes all Q/W/E/R toggle panels except the one identified by <paramref name="except" />. Button
    ///     deselection is handled by the OnClose/SessionClosed events the close raises.
    /// </summary>
    private void ForceCloseOtherTogglePanels(Keys except)
    {
        if ((except != Keys.Q) && PauseMenu.Visible)
        {
            OptionsModal.Hide();
            PauseMenu.Hide();
        }

        if ((except != Keys.W) && IsAnyBoardPanelVisible())
            WorldState.Board.CloseSession();

        if ((except != Keys.E) && WorldList.Visible)
            WorldList.Close();

        if ((except != Keys.R) && SocialStatusPicker.Visible)
        {
            SocialStatusPicker.Hide();

            WorldHud.EmoteButton?.IsSelected = false;
        }
    }

    private void HideAllBoardControls()
    {
        BoardsModal.Hide();

        //a confirm popup can outlive the session that armed it (closing the board UI while it is up). Drop the
        //pending actions so an OK afterwards can't send a delete against a closed session.
        PendingDeleteAction = null;
        PendingBoardSuccessAction = null;
        DeleteConfirm.Hide();
    }

    /// <summary>
    ///     UIPanel subclass that delegates root-level input events back to WorldScreen.
    ///     Used as the Root panel so the dispatcher's bubble-up terminates with WorldScreen's handlers.
    /// </summary>
    private sealed class WorldRootPanel : UIPanel
    {
        private readonly WorldScreen Screen;

        public WorldRootPanel(WorldScreen screen) => Screen = screen;

        public override void OnKeyDown(KeyDownEvent e) => Screen.OnRootKeyDown(e);
        public override void OnMouseDown(MouseDownEvent e) => Screen.OnRootMouseDown(e);
        public override void OnClick(ClickEvent e) => Screen.OnRootClick(e);
        public override void OnMouseScroll(MouseScrollEvent e) => Screen.OnRootMouseScroll(e);
        public override void OnDoubleClick(DoubleClickEvent e) => Screen.OnRootDoubleClick(e);
        public override void OnDragMove(DragMoveEvent e) => Screen.OnRootDragMove(e);
        public override void OnDragDrop(DragDropEvent e) => Screen.OnRootDragDrop(e);
    }

    private void WireBoardsModal()
    {
        //tab click -> ask the server for that family. Board 0 is the player's mailbox (0x3B: "Target mailbox
        //(always 0 for player mail)"), so Mail is a direct request rather than a trip through the board list.
        BoardsModal.TabRequested += isMail =>
        {
            if (!isMail)
            {
                Game.Connection.SendBoardInteraction(BoardRequestType.BoardList);

                return;
            }

            Game.Connection.SendBoardInteraction(
                BoardRequestType.ViewBoard,
                BoardsModalControl.MAIL_BOARD_ID,
                startPostId: short.MaxValue);
        };

        BoardsModal.BoardSelected += boardId =>
        {
            Game.Connection.SendBoardInteraction(BoardRequestType.ViewBoard, boardId, startPostId: short.MaxValue);
        };

        BoardsModal.PostSelected += postId => Game.Connection.SendBoardInteraction(
            BoardRequestType.ViewPost,
            BoardsModal.BoardId,
            postId,
            controls: BoardControls.RequestPost);

        BoardsModal.StepRequested += (currentPostId, forward) =>
        {
            //post ids run newest-first, so "next" walks down and "prev" walks up.
            var target = forward
                ? (short)Math.Max(currentPostId - 1, 1)
                : (short)Math.Min(currentPostId + 1, short.MaxValue);

            Game.Connection.SendBoardInteraction(
                BoardRequestType.ViewPost,
                BoardsModal.BoardId,
                target,
                controls: forward ? BoardControls.NextPage : BoardControls.PreviousPage);
        };

        BoardsModal.DeleteRequested += postId =>
        {
            var boardId = BoardsModal.BoardId;

            PendingDeleteAction = () =>
            {
                PendingBoardSuccessAction = () => BoardsModal.RemovePost(postId);
                Game.Connection.SendBoardInteraction(BoardRequestType.Delete, boardId, postId);
            };

            DeleteConfirm.Show("Delete this post?");
        };

        BoardsModal.HighlightRequested += postId =>
        {
            PendingBoardSuccessAction = () => BoardsModal.ToggleHighlight(postId);
            Game.Connection.SendBoardInteraction(BoardRequestType.Highlight, BoardsModal.BoardId, postId);
        };

        BoardsModal.LoadMoreRequested += lastPostId
            => Game.Connection.SendBoardInteraction(BoardRequestType.ViewBoard, BoardsModal.BoardId, startPostId: lastPostId);

        BoardsModal.SubmitRequested += (recipient, subject, body) =>
        {
            var boardId = BoardsModal.BoardId;

            Game.Connection.SendBoardInteraction(
                recipient is null ? BoardRequestType.NewPost : BoardRequestType.SendMail,
                boardId,
                to: recipient,
                subject: subject,
                message: body);

            //re-request the post list — compose stays visible until the server responds
            Game.Connection.SendBoardInteraction(BoardRequestType.ViewBoard, boardId, startPostId: short.MaxValue);
        };

        BoardsModal.SessionEndRequested += () => WorldState.Board.CloseSession();
        BoardsModal.OnClose += () => WorldState.Board.CloseSession();
    }

    #endregion

    #region HUD Panel Wiring
    private void WireHudPanels(IWorldHud hud)
    {
        //layout/expand
        if (hud.ChangeLayoutButton is not null)
            hud.ChangeLayoutButton.Clicked += SwapHudLayout;

        if (hud.ExpandButton is not null)
            hud.ExpandButton.Clicked += () => hud.ToggleExpand();

        //action buttons
        //option button toggles the pause menu (Escape/Q also open it); reflects open state via IsSelected
        if (hud.OptionButton is not null)
        {
            hud.OptionButton.Clicked += () =>
            {
                if (PauseMenu.Visible)
                    PauseMenu.Hide();
                else
                {
                    hud.OptionButton!.IsSelected = true;
                    PauseMenu.Show();
                }
            };

            PauseMenu.OnClose += () => hud.OptionButton.IsSelected = false;
        }

        if (hud.HelpButton is not null)
            hud.HelpButton.Clicked += () => HotkeyHelp.Show();

        if (hud.SettingsButton is not null)
            hud.SettingsButton.Clicked += () => OptionsModal.Show(OptionsModalControl.OptionsTab.Settings);

        if (hud.GroupButton is not null)
            hud.GroupButton.Clicked += () =>
            {
                GroupPanel.ShowMembers();
                Game.Connection.RequestSelfProfile();
            };

        if (hud.GroupIndicator is not null)
            //route through UserOptions.Toggle so the F4 settings panel and StatusBook indicator stay in sync
            hud.GroupIndicator.Clicked += () => WorldState.UserOptions.Toggle(12);

        if (hud.UsersButton is not null)
        {
            hud.UsersButton.Clicked += () =>
            {
                if (WorldList.Visible)
                {
                    WorldList.Hide();

                    return;
                }

                hud.UsersButton!.IsSelected = true;
                Game.Connection.RequestWorldList();
            };

            WorldList.OnClose += () => hud.UsersButton.IsSelected = false;
        }

        WorldList.OnWhisperRequested += name => WorldHud.ChatInput.Focus($"-> {name}: ", TextColors.Whisper);

        if (hud.BulletinButton is not null)
        {
            hud.BulletinButton.Clicked += () =>
            {
                hud.BulletinButton!.IsSelected = true;
                Game.Connection.SendBoardInteraction(BoardRequestType.BoardList);
            };

            WorldState.Board.SessionClosed += ResetBulletinButtonSelection;
        }

        hud.InventoryReactivated += () =>
        {
            SelfProfileRequested = true;
            SelfProfileRequestedTab = StatusBookTab.Equipment;
            Game.Connection.RequestSelfProfile();
        };

        if (hud.LegendButton is not null)
            hud.LegendButton.Clicked += () =>
            {
                SelfProfileRequested = true;
                SelfProfileRequestedTab = StatusBookTab.Legend;
                Game.Connection.RequestSelfProfile();
            };

        if (hud.TownMapButton is not null)
            hud.TownMapButton.Clicked += () =>
            {
                if (TownMapControl.Visible)
                    TownMapControl.Hide();
                else
                {
                    var player = WorldState.GetPlayerEntity();

                    if (player is not null)
                        TownMapControl.Show(CurrentMapId, player.TileX, player.TileY);
                }
            };

        if (hud.EmoteButton is not null)
            hud.EmoteButton.Clicked += ToggleSocialStatusPicker;

        if (hud.EmoteButton is not null)
            SocialStatusPicker.OnClosed += () => hud.EmoteButton.IsSelected = false;

        if (hud.CharScreenshotButton is not null)
            hud.CharScreenshotButton.Clicked += () => Game.RequestScreenshot();

        if (hud.MailButton is not null)
        {
            hud.MailButton.Clicked += () =>
            {
                hud.MailButton!.IsSelected = true;
                Game.Connection.SendBoardInteraction(BoardRequestType.BoardList);
            };

            WorldState.Board.SessionClosed += ResetMailButtonSelection;
        }

        //chat input events
        hud.ChatInput.MessageSent += msg => Game.Connection.SendPublicMessage(msg);
        hud.ChatInput.ShoutSent += msg => Game.Connection.SendShout(msg);
        hud.ChatInput.WhisperSent += (target, msg) => Game.Connection.SendWhisper(target, msg);
        hud.ChatInput.IgnoreAdded += name => Game.Connection.SendAddIgnore(name);
        hud.ChatInput.IgnoreRemoved += name => Game.Connection.SendRemoveIgnore(name);
        hud.ChatInput.IgnoreListRequested += () => Game.Connection.SendIgnoreRequest();

        //slot events
        hud.Inventory.OnSlotClicked += HandleInventorySlotClicked;
        hud.Inventory.OnSlotSwapped += (s, t) => Game.Connection.SwapSlot(PanelType.Inventory, s, t);
        hud.Inventory.OnSlotDroppedOutside += HandleInventoryDropInViewport;
        hud.SkillBook.OnSlotClicked += HandleSkillSlotClicked;
        hud.SkillBook.OnSlotSwapped += (s, t) => Game.Connection.SwapSlot(PanelType.SkillBook, s, t);
        hud.SkillBookAlt.OnSlotClicked += HandleSkillSlotClicked;
        hud.SkillBookAlt.OnSlotSwapped += (s, t) => Game.Connection.SwapSlot(PanelType.SkillBook, s, t);
        hud.SpellBook.OnSlotClicked += HandleSpellSlotClicked;
        hud.SpellBook.OnSlotSwapped += (s, t) => Game.Connection.SwapSlot(PanelType.SpellBook, s, t);
        hud.SpellBook.OnSlotDroppedOutside += HandleSpellSlotDropped;
        hud.SpellBookAlt.OnSlotClicked += HandleSpellSlotClicked;
        hud.SpellBookAlt.OnSlotSwapped += (s, t) => Game.Connection.SwapSlot(PanelType.SpellBook, s, t);
        hud.SpellBookAlt.OnSlotDroppedOutside += HandleSpellSlotDropped;

        //tools (h tab) — page 3 world abilities
        hud.Tools.WorldSkills.OnSlotClicked += HandleSkillSlotClicked;
        hud.Tools.WorldSkills.OnSlotSwapped += (s, t) => Game.Connection.SwapSlot(PanelType.SkillBook, s, t);
        hud.Tools.WorldSpells.OnSlotClicked += HandleSpellSlotClicked;
        hud.Tools.WorldSpells.OnSlotSwapped += (s, t) => Game.Connection.SwapSlot(PanelType.SpellBook, s, t);
        hud.Tools.WorldSpells.OnSlotDroppedOutside += HandleSpellSlotDropped;

        WireAbilityRightClicks(hud.SkillBook);
        WireAbilityRightClicks(hud.SkillBookAlt);
        WireAbilityRightClicks(hud.SpellBook);
        WireAbilityRightClicks(hud.SpellBookAlt);
        WireAbilityRightClicks(hud.Tools.WorldSkills);
        WireAbilityRightClicks(hud.Tools.WorldSpells);

        hud.StatsPanel.OnRaiseStat += stat => Game.Connection.RaiseStat(stat);

        hud.StatsPanel.OnHoverEnter += count => WorldHud.SetDescription($"Level Up Point: {count}");
        hud.StatsPanel.OnHoverExit += () => WorldHud.SetDescription(null);

        hud.Inventory.OnSlotHoverEnter += HandleInventoryHoverEnter;
        hud.Inventory.OnSlotHoverExit += HandleInventoryHoverExit;

        foreach (var panel in new PanelBase[]
                 {
                     hud.Inventory,
                     hud.SkillBook,
                     hud.SkillBookAlt,
                     hud.SpellBook,
                     hud.SpellBookAlt,
                     hud.Tools.WorldSkills,
                     hud.Tools.WorldSpells
                 })
        {
            panel.OnSlotHoverEnter += slot => WorldHud.SetDescription(slot.SlotName);
            panel.OnSlotHoverExit += () => WorldHud.SetDescription(null);
        }

        //large hud: show a tooltip popup (matching the equipment tab's style) when hovering skill/spell slots so the
        //full ability name + level details are visible above the slot
        if (hud is LargeWorldHudControl largeHud)
            foreach (var panel in new PanelBase[]
                     {
                         hud.SkillBook,
                         hud.SkillBookAlt,
                         hud.SpellBook,
                         hud.SpellBookAlt,
                         hud.Tools.WorldSkills,
                         hud.Tools.WorldSpells
                     })
            {
                panel.OnSlotHoverEnter += largeHud.ShowSlotTooltip;
                panel.OnSlotHoverExit += largeHud.HideSlotTooltip;
            }
    }

    private void ResetBulletinButtonSelection() => WorldHud.BulletinButton?.IsSelected = false;

    private void ResetMailButtonSelection() => WorldHud.MailButton?.IsSelected = false;

    private void SwapHudLayout()
    {
        WorldHud.Inventory.ForceHoverExit();

        var activeTab = WorldHud.ActiveTab;

        ((UIPanel)WorldHud).Visible = false;
        WorldHud = WorldHud == SmallHud ? LargeHud : SmallHud;
        ((UIPanel)WorldHud).Visible = true;
        WorldHud.ShowTab(activeTab);

        var viewport = WorldHud.ViewportBounds;
        Camera.Resize(viewport.Width, viewport.Height);
        UpdateCameraOffset(viewport);
        SystemMessagePane.SetViewportBounds(viewport);
        PauseMenu.SetViewportBounds(viewport);
        WorldList.SetViewportBounds(viewport);
        MarkdownNotice.SetViewportBounds(viewport);

        FollowPlayerCamera();

        //rebuild darkness texture immediately so this frame's draw uses the new viewport size —
        //DarknessRenderer.Update runs earlier in the frame (before ProcessInput), so without this
        //the first frame after the swap would draw the old-sized texture over the new viewport
        if (DarknessRenderer.IsActive)
        {
            Lighting.Gather(MapFile, CurrentMapFlags, Camera);
            DarknessRenderer.Update(Camera, viewport, Lighting.Sources);
        }

        //weather uses fresh viewport each frame via WorldHud.ViewportBounds, but snow needs an
        //immediate respawn into the new bounds so particles don't clump at the old edges
        if (WeatherRenderer.IsActive)
            WeatherRenderer.Update(new GameTime(), viewport);
    }

    /// <summary>
    ///     Calls an action on all HUD instances so both stay in sync regardless of which is visible.
    /// </summary>
    private void UpdateHuds<T>(Action<IWorldHud, T> op, T arg)
    {
        op(SmallHud, arg);
        op(LargeHud, arg);
    }

    private void UpdateHuds<T1, T2>(Action<IWorldHud, T1, T2> op, T1 arg1, T2 arg2)
    {
        op(SmallHud, arg1, arg2);
        op(LargeHud, arg1, arg2);
    }

    private static class HudOps
    {
        public static readonly Action<IWorldHud, int, int> SetCoords =
            static (h, x, y) => h.SetCoords(x, y);

        public static readonly Action<IWorldHud, string> SetZoneName =
            static (h, name) => h.SetZoneName(name);

        public static readonly Action<IWorldHud, string> SetPlayerName =
            static (h, name) => h.SetPlayerName(name);

        public static readonly Action<IWorldHud, string> SetServerName =
            static (h, name) => h.SetServerName(name);

        public static readonly Action<IWorldHud, string> ShowPersistentMessage =
            static (h, msg) => h.ShowPersistentMessage(msg);

        public static readonly Action<IWorldHud, Texture2D> SetEmoteIcon = static (h, icon) =>
        {
            if (h.EmoteButton is null)
                return;

            h.EmoteButton.NormalTexture = icon;
            h.EmoteButton.SelectedTexture = icon;
        };
    }
    #endregion

    #region Options Dialog Wiring
    private void WireOptionsDialog()
    {
        PauseMenu.OnSettings += () => OptionsModal.Show(OptionsModalControl.OptionsTab.Settings);

        PauseMenu.OnExit += BeginExit;

        PauseMenu.OnSoundVolumeChanged += volume =>
        {
            Game.SoundSystem.SetSoundVolume(volume);
            ClientSettings.SoundVolume = volume;
            ClientSettings.Save();
        };

        PauseMenu.OnMusicVolumeChanged += volume =>
        {
            Game.SoundSystem.SetMusicVolume(volume);
            ClientSettings.MusicVolume = volume;
            ClientSettings.Save();
        };

        //ambience is a persist-only placeholder — no audio path consumes it yet.
        PauseMenu.OnAmbienceVolumeChanged += volume =>
        {
            ClientSettings.AmbienceVolume = volume;
            ClientSettings.Save();
        };

        //apply saved volume settings
        PauseMenu.SetSoundVolume(ClientSettings.SoundVolume);
        PauseMenu.SetMusicVolume(ClientSettings.MusicVolume);
        PauseMenu.SetAmbienceVolume(ClientSettings.AmbienceVolume);
        Game.SoundSystem.SetSoundVolume(ClientSettings.SoundVolume);
        Game.SoundSystem.SetMusicVolume(ClientSettings.MusicVolume);
    }
    #endregion
}