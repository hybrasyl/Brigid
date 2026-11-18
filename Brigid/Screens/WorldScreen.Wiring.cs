#region
using System.Diagnostics;
using Brigid.Collections;
using Brigid.Controls.Components;
using Brigid.Controls.World.Hud;
using Brigid.Controls.World.Hud.Main;
using Brigid.Controls.World.Hud.Main.Windows;
using Brigid.Controls.Generic;
using Brigid.Controls.World.Hud.Panel;
using Brigid.Controls.World.Popups.Options;
using Brigid.Extensions;
using Brigid.Systems;
using Brigid.Systems.Keybinds;
using Brigid.ViewModel;
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

        //the level starts that a level-up teaches are saved for the character (BRIG-148)
        WorldState.Attributes.SpanLearned += SavePlayerExperience;

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

        //connection details panel. Only the connection half is wired here: WireServerEvents runs from
        //Initialize, and the panel itself does not exist until LoadContent, so its own events are
        //subscribed where it is built.
        Game.Connection.OnEchoReply += HandleEchoReply;

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
    //   - ExchangeControl subscribes to Started/ItemAdded/GoldSet/Accepted/Closed — updates its own UI
    //   - WorldScreen (WireServerEvents) subscribes to AmountRequested (spawn amount popup) + Closed (screen-level teardown)
    // Closed is intentionally double-subscribed: the control hides itself, the screen runs side effects.
    // Don't collapse them — they serve different layers.
    private void WireExchange()
    {
        Exchange.OnOk += () => Game.Connection.SendExchangeInteraction(ExchangeRequestType.Accept, Exchange.OtherUserId);

        //the window stays until the server answers the cancel (BRIG-168). The prompts that add gold or items close now,
        //because the exchange takes no more offers
        Exchange.OnCancel += () =>
        {
            Game.Connection.SendExchangeInteraction(ExchangeRequestType.Cancel, Exchange.OtherUserId);
            HideExchangePrompts();
        };

        //the gold button opens the gold prompt over the exchange; its OnConfirm sends SetGold while the exchange is open
        Exchange.OnGoldClicked += () =>
        {
            GoldDrop.SetViewportBounds(Exchange.Bounds);
            GoldDrop.ShowForExchange();
            WorldHud.SetDescription($"Gold( {WorldState.Inventory.Gold} )");
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
        WorldState.Board.SessionOpened += OnBoardSessionOpened;
    }

    //close the other toggle-group panels whenever a board/mail session opens by ANY route (signpost, mailbox, F7),
    //not just the W hotkey. Keys.W keeps the board itself open. A named method (not a lambda) so it can be removed
    //in the teardown, matching the other Board subscriptions.
    private void OnBoardSessionOpened() => ForceCloseOtherTogglePanels(Keys.W);

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
            hud.GroupIndicator.Clicked += ToggleGroupOpen;

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
        }

        hud.InventoryReactivated += () => RequestStatusBook(StatusBookTab.Equipment);

        if (hud.LegendButton is not null)
            hud.LegendButton.Clicked += () => RequestStatusBook(StatusBookTab.Legend);

        if (hud.ServerBoxButton is not null)
            hud.ServerBoxButton.Clicked += ToggleServerInfo;

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
                OpenMail();
            };
        }

        WireChatInput(hud.ChatInput);

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

    private void OpenChatTabEditor(ChatTab? tab)
    {
        ChatTabEditor.SetViewportBounds(WorldHud.ViewportBounds);
        ChatTabEditor.Show(tab);
    }

    //the sends of a chat input; each hud has its own input
    private void WireChatInput(ChatInputControl input)
    {
        input.MessageSent += msg => Game.Connection.SendPublicMessage(msg);
        input.ShoutSent += msg => Game.Connection.SendShout(msg);
        input.WhisperSent += (target, msg) => Game.Connection.SendWhisper(target, msg);
        input.IgnoreAdded += name => Game.Connection.SendAddIgnore(name);
        input.IgnoreRemoved += name => Game.Connection.SendRemoveIgnore(name);
        input.IgnoreListRequested += () => Game.Connection.SendIgnoreRequest();
    }

    //the status book opens on the self profile that the server sends back
    private void RequestStatusBook(StatusBookTab tab)
    {
        SelfProfileRequested = true;
        SelfProfileRequestedTab = tab;
        Game.Connection.RequestSelfProfile();
    }

    /// <summary>The new HUD's own controls; its legacy panels are the classic HUD's, which WireHudPanels wired.</summary>
    private void WireMainHud()
    {
        MainHud.ServerBoxClicked += ToggleServerInfo;
        MainHud.MailClicked += OpenMail;

        //the player plate opens the status book, as a second press of the inventory key does on the classic HUD
        MainHud.PlateClicked += () => RequestStatusBook(StatusBookTab.Equipment);
        WireChatInput(MainHud.ChatInput);

        //the chat box opens as the player left it
        var chatBox = MainHud.ChatBox;
        chatBox.SetCustomTabs(ClientSettings.ChatTabs);
        chatBox.Select(ClientSettings.SelectedChatTab);
        chatBox.SetCollapsed(ClientSettings.ChatBoxHidden);

        chatBox.StateChanged += () =>
        {
            ClientSettings.SelectedChatTab = chatBox.SelectedTabName;
            ClientSettings.ChatBoxHidden = chatBox.IsCollapsed;
            ClientSettings.ChatTabs = chatBox.CustomTabs;
            ClientSettings.Save();
        };

        //the + button and the menu of a custom tab open the editor; the box changes its tabs on the editor's OK
        chatBox.AddTabRequested += () => OpenChatTabEditor(null);

        chatBox.TabMenuRequested += (tab, x, y) => ChatTabMenu.Show(x, y, tab, () => OpenChatTabEditor(tab), () => chatBox.RemoveCustomTab(tab));
        ChatTabEditor.Confirmed += chatBox.Apply;

        //each button does what its key does; group is the open and closed toggle (D18), and a right click opens its window
        var windows = MainHud.Windows;
        var abilities = new AbilitiesWindowControl(WorldState.SkillBook, WorldState.SpellBook, WorldState.Attributes, () => WorldState.AbilityMetadata);
        abilities.UseRequested += UseAbility;
        abilities.DetailsRequested += ShowAbilityDetails;
        windows.BindPanel(HudWindows.Abilities, abilities);

        var inventory = new InventoryWindowControl(WorldState.Inventory, WorldState.Attributes);
        inventory.UseRequested += HandleInventorySlotClicked;
        inventory.SwapRequested += (from, to) => Game.Connection.SwapSlot(PanelType.Inventory, from, to);
        inventory.HoverChanged += ShowInventoryWindowTooltip;
        //the window button also selects the hidden classic inventory, as A does, so the number keys use the items
        windows.BindPanel(HudWindows.Inventory, inventory, () => MainHud.ShowTab(HudTab.Inventory), inventory.ClearHover);
        windows.BindWindow(HudWindows.Menu, TogglePauseMenu, () => PauseMenu.Visible);
        windows.BindWindow(HudWindows.Mail, ToggleBoards, IsAnyBoardPanelVisible);
        windows.BindWindow(HudWindows.Community, ToggleWorldList, () => WorldList.Visible);
        windows.BindWindow(HudWindows.Map, ToggleTownMap, () => TownMapControl.Visible);

        windows.BindWindow(
            HudWindows.Group,
            ToggleGroupOpen,
            () => WorldState.UserOptions[GROUP_OPEN_OPTION],
            OpenGroupWindow,
            () =>
            [
                TooltipLine.Themed(WorldState.UserOptions[GROUP_OPEN_OPTION] ? "Group: open" : "Group: closed", ThemeRoles.Text),
                TooltipLine.Themed(
                    $"Right click or {KeybindCatalog.Format(Keybinds.Effective(CommandId.World_GroupPanel))}: the group window",
                    ThemeRoles.TextDisabled)
            ]);
        windows.BindWindow(HudWindows.Screenshot, Game.RequestScreenshot);
    }

    //group open and closed, from the indicator of each hud: through UserOptions.Toggle, so the F4 settings panel and the
    //status book indicator stay in step
    private void ToggleGroupOpen() => WorldState.UserOptions.Toggle(GROUP_OPEN_OPTION);

    //the board list, where the mail is; from the mail button of each hud
    private void OpenMail() => Game.Connection.SendBoardInteraction(BoardRequestType.BoardList);

    //the connection details, from the server box of each hud
    private void ToggleServerInfo()
    {
        if (ServerInfo.Visible)
        {
            ServerInfo.Hide();

            return;
        }

        RefreshServerInfo();
        ServerInfo.Show();
        SendEchoProbe();
    }

    //both legacy huds: a board can close while the hud that opened it is hidden
    private void ResetBulletinButtonSelection()
    {
        SmallHud.BulletinButton?.IsSelected = false;
        LargeHud.BulletinButton?.IsSelected = false;
    }

    private void ResetMailButtonSelection()
    {
        SmallHud.MailButton?.IsSelected = false;
        LargeHud.MailButton?.IsSelected = false;
    }

    //'/': the classic and the large hud; nothing while the new hud shows
    private void SwapHudLayout()
    {
        if (HudSwitch.Swap())
            ActivateHud(HudFor(HudSwitch.Active));
    }

    //shift+/: the new hud, and back to the hud that showed before it
    private void ToggleMainHud()
    {
        HudSwitch.ToggleMain();
        ActivateHud(HudFor(HudSwitch.Active));
    }

    private IWorldHud HudFor(HudLayout layout)
        => layout switch
        {
            HudLayout.Classic => SmallHud,
            HudLayout.Large   => LargeHud,
            HudLayout.Main    => MainHud,
            _                 => throw new UnreachableException()
        };

    /// <summary>
    ///     Shows <paramref name="hud" /> in place of the current HUD, and moves the camera and each popup that is placed in
    ///     the world area to its viewport.
    /// </summary>
    private void ActivateHud(IWorldHud hud)
    {
        WorldHud.Inventory.ForceHoverExit();

        //a chat box that has the focus would keep its chat mode while its hud is hidden
        if (WorldHud.ChatInput.Mode != ChatMode.None)
            WorldHud.ChatInput.Unfocus();

        var activeTab = WorldHud.ActiveTab;

        ((UIPanel)WorldHud).Visible = false;
        WorldHud = hud;
        ((UIPanel)WorldHud).Visible = true;
        WorldHud.ShowTab(activeTab);

        var viewport = WorldHud.ViewportBounds;
        Camera.Resize(viewport.Width, viewport.Height);
        UpdateCameraOffset(viewport);
        SystemMessagePane.SetViewportBounds(viewport);

        //the new hud shows these messages in its chat tabs (BRIG-127 D10); the hidden pane does not fade, so it empties
        SystemMessagePane.Visible = WorldHud != MainHud;

        if (!SystemMessagePane.Visible)
            SystemMessagePane.Clear();

        //the chat tab menu and editor belong to the new hud's chat box
        if (WorldHud != MainHud)
        {
            ChatTabMenu.Hide();
            ChatTabEditor.Hide();
        }

        PauseMenu.SetViewportBounds(viewport);
        WorldList.SetViewportBounds(viewport);
        MarkdownNotice.SetViewportBounds(viewport);
        TextPopup.SetViewportBounds(viewport);
        SelfProfileTextEditor.SetViewportBounds(viewport);
        Exchange.SetViewportBounds(viewport);
        //a prompt over the exchange moves with it; a prompt for a drop stays in the viewport
        GoldDrop.SetViewportBounds(GoldDrop.ForExchange ? Exchange.Bounds : viewport);
        ItemAmount.SetViewportBounds(Exchange.Bounds);
        GroupPanel.SetViewportBounds(viewport);
        GroupBoxViewer.SetViewportBounds(viewport);

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
    ///     Calls an action on all HUD instances so they stay in sync regardless of which is visible.
    /// </summary>
    private void UpdateHuds<T>(Action<IWorldHud, T> op, T arg)
    {
        op(SmallHud, arg);
        op(LargeHud, arg);
        op(MainHud, arg);
    }

    private void UpdateHuds<T1, T2>(Action<IWorldHud, T1, T2> op, T1 arg1, T2 arg2)
    {
        op(SmallHud, arg1, arg2);
        op(LargeHud, arg1, arg2);
        op(MainHud, arg1, arg2);
    }

    /// <summary>
    ///     Shows a completed round trip. Only while the panel is open — the probe is user-initiated, so
    ///     a reply with nothing on screen means the panel was closed mid-flight.
    /// </summary>
    private void HandleEchoReply(TimeSpan roundTrip)
    {
        if (ServerInfo.Visible)
            ServerInfo.SetLatency(roundTrip);
    }

    /// <summary>
    ///     Repopulates the connection panel from the live connection and the trust store.
    /// </summary>
    private void RefreshServerInfo()
        => ServerInfo.Describe(
            Game.Connection.ServerName,
            CertificateTrustStore.Accepted?.Server ?? GlobalSettings.LobbyHost,
            Game.Connection.Client.Negotiated,
            CertificateTrustStore.Accepted);

    /// <summary>
    ///     Sends an echo probe, or says why it cannot. The reply arrives on
    ///     <see cref="ConnectionManager.OnEchoReply" />, which the panel is subscribed to.
    /// </summary>
    private void SendEchoProbe()
    {
        if (Game.Connection.SendEchoProbe())
        {
            ServerInfo.SetLatencyUnavailable("measuring…", true);

            return;
        }

        //no dialect means no extension frame can be sent at all, so this is not a timeout to wait out.
        ServerInfo.SetLatencyUnavailable("unavailable on a retail connection", false);
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

        public static readonly Action<IWorldHud, bool> SetTransportSecure =
            static (h, secure) => h.SetTransportSecure(secure);

        public static readonly Action<IWorldHud, bool> SetGroupOpen =
            static (h, groupOpen) => h.SetGroupOpen(groupOpen);

        public static readonly Action<IWorldHud, string> ShowPersistentMessage =
            static (h, msg) => h.ShowPersistentMessage(msg);

        //the legacy status icons are the first of each three frames of emot000.epf; read only when there is no new art
        public static readonly Action<IWorldHud, SocialStatus> SetEmoteStatus = static (h, status) =>
        {
            if (h.EmoteButton is null || h.EmoteStatusIcon is null)
                return;

            h.EmoteStatusIcon.ShowOn(h.EmoteButton, status, () => UiRenderer.Instance?.GetEpfTexture("emot000.epf", (byte)status * 3));
        };

        public static readonly Action<IWorldHud, ushort, EffectColor> SetEffect =
            static (h, icon, color) => h.SetEffect(icon, color);
    }
    #endregion

    #region Options Dialog Wiring
    private void WireOptionsDialog()
    {
        PauseMenu.OnSettings += () => OptionsModal.Show(OptionsModalControl.OptionsTab.Settings);

        PauseMenu.OnExitToMenu += () => BeginExit(false);
        PauseMenu.OnExitGame += () => BeginExit(true);

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

        PauseMenu.OnAmbienceVolumeChanged += volume =>
        {
            Game.SoundSystem.SetAmbienceVolume(volume);
            ClientSettings.AmbienceVolume = volume;
            ClientSettings.Save();
        };

        //apply saved volume settings
        PauseMenu.SetSoundVolume(ClientSettings.SoundVolume);
        PauseMenu.SetMusicVolume(ClientSettings.MusicVolume);
        PauseMenu.SetAmbienceVolume(ClientSettings.AmbienceVolume);
        Game.SoundSystem.SetSoundVolume(ClientSettings.SoundVolume);
        Game.SoundSystem.SetMusicVolume(ClientSettings.MusicVolume);
        Game.SoundSystem.SetAmbienceVolume(ClientSettings.AmbienceVolume);
    }
    #endregion
}