#region
using Brigid.Collections;
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Controls.World.Hud;
using Brigid.Controls.World.Hud.Panel.Slots;
using Brigid.Controls.World.Popups;
using Brigid.Controls.World.Popups.Boards;
using Brigid.Controls.World.Popups.Dialog;
using Brigid.Controls.World.Popups.Exchange;
using Brigid.Controls.World.Popups.Options;
using Brigid.Controls.World.Popups.Profile;
using Brigid.Controls.World.Popups.WorldList;
using Brigid.Controls.World.ViewPort;
using Brigid.Data.Repositories;
using Brigid.Extensions;
using Brigid.Models;
using Brigid.Rendering.Models;
using Brigid.Systems;
using Brigid.Systems.Keybinds;
using Brigid.ViewModel;
using Chaos.DarkAges.Definitions;
using Chaos.Geometry.Abstractions;
using Chaos.Geometry.Abstractions.Definitions;
using DALib.Data;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Pathfinder = Chaos.Pathfinding.Pathfinder;
#endregion

namespace Brigid.Screens;

public sealed partial class WorldScreen : IScreen
{
    //walk queue: when walk animation is >= 75% complete, one walk can be queued
    private const float WALK_QUEUE_THRESHOLD = 0.75f;

    //minimum interval between spacebar assail fires when held (os key-repeat rate varies)
    private const long SPACEBAR_INTERVAL_MS = 100;

    //stripe-pass alpha for transparent (invisible) aislings. 1/3 is chosen so that for the silhouetted
    //local player, the stripe draw compounds with the silhouette overdraw to produce the target visibility:
    //    TRANSPARENT_ALPHA + TRANSPARENT_SILHOUETTE_ALPHA * SILHOUETTE_ALPHA * (1 - TRANSPARENT_ALPHA) = 0.5
    //i.e., ~50% in the open and ~25% behind foregrounds (occlusion × transparency = 50% × 50%).
    private const float TRANSPARENT_ALPHA = 1f / 3f;

    //silhouette-RT alpha for transparent entities. 0.5 makes the overlay's effective contribution
    //TRANSPARENT_SILHOUETTE_ALPHA * SILHOUETTE_ALPHA = 0.25, matching the behind-foreground target.
    private const float TRANSPARENT_SILHOUETTE_ALPHA = 0.5f;

    //set true while the silhouette pre-render callback is drawing entities into the silhouette RT.
    //used by DrawAisling to route transparent players through the silhouette pass instead of the stripe pass.
    private bool DrawingForSilhouette;

    //entity hitbox dimensions (screen pixels)
    private const int HITBOX_WIDTH = 28;
    private const int HITBOX_HEIGHT = 60;

    //doubleclick entity cache expiry — slightly larger than the dispatcher's 300ms double-click window so the cache
    //remains valid through the full doubleclick detection window
    private const int DOUBLE_CLICK_CACHE_WINDOW_MS = 550;

    private const string SPOUSE_PREFIX = "Spouse: ";
    private const string GROUP_MEMBERS_PREFIX = "Group members";

    private readonly CastingSystem CastingSystem = new();

    private readonly WorldDebugRenderer DebugRenderer = new();

    //draw-pass hitbox list: rebuilt every frame during entity rendering, in draw order (back-to-front)
    private readonly List<EntityHitBox> EntityHitBoxes = new(256);

    //set of entity ids currently highlighted as group members (auto-expires after 1000ms)
    private readonly HashSet<uint> GroupHighlightedIds = [];

    //authoritative closed/open state for each door we've heard from the server about on the current map.
    //populated by HandleDoor from 0x32 Door packets and cleared on map change. The sprite-pair table in
    //Brigid.Definitions.DoorTable is an imperfect oracle — some entries have their (closed, open)
    //columns reversed relative to reality — so the server's explicit flag is the only reliable source for
    //the Alt+right-click context menu's Open/Close label.
    private readonly Dictionary<(int X, int Y), bool> KnownDoorClosedState = [];
    private readonly EntityOverlayManager Overlays = new();
    private readonly PathfindingState Pathfinding = new();
    //count of Walk packets we've sent that the server has not yet acknowledged via ClientWalkResponse.
    //the server emits one ack per Walk in FIFO order over the same TCP stream, and we never send a walk
    //the server would reject (the local walkability check in PredictAndWalk gates outbound traffic).
    //so each ack we receive while this counter is positive corresponds to a walk we predicted and is
    //treated as a no-op confirmation; only when the counter is zero does an unmatched ack mean a
    //genuine server-initiated walk (force-walk, push tile, knockback) that the rubberband path applies.
    //Location packets do not touch this counter — they snap position authoritatively and let the
    //pending acks drain naturally as no-ops on arrival.
    private int InFlightWalkAcks;

    private AbilityMetadataDetailsControl AbilityMetadataDetails = null!;
    private AislingContextMenu AislingContext = null!;
    private DoorContextMenu DoorContext = null!;

    private int AnimationTick;
    private bool AwaitingMapData;

    //one modal for the whole board/mail flow (index -> list -> read/compose, Boards and Mail tabs)
    private BoardsModalControl BoardsModal = null!;
    private OkPopupMessageControl BoardResponsePopup = null!;
    private Camera Camera = null!;
    private CastablePopupControl CastablePopup = null!;
    private ushort CurrentMapCheckSum;
    private MapFlags CurrentMapFlags;
    private short CurrentMapId;

    private DarknessRenderer DarknessRenderer = null!;
    private WeatherRenderer WeatherRenderer = null!;
    private OkPopupMessageControl DeleteConfirm = null!;
    private GraphicsDevice Device = null!;
    private OkPopupMessageControl DisconnectPopup = null!;
    private OkPopupMessageControl ExitConfirmPopup = null!;
    private float ExitConfirmSecondsRemaining;
    //grace window after ConfirmExit fires — if a disconnect arrives within this window we treat it as
    //the expected logout (suppress the "Connection Lost" popup and transition to login). Defensive against
    //servers that drop the connection without flushing the Redirect packet.
    private float ExitInProgressSecondsRemaining;

    //event detail popup (from events tab)
    private EventMetadataDetailsControl EventMetadataDetails = null!;
    private ExchangeControl Exchange = null!;
    private OkPopupMessageControl ExchangeResultPopup = null!;
    private ItemAmountControl ItemAmount = null!;

    private ChaosGame Game = null!;
    private GoldAmountControl GoldDrop = null!;
    private GroupRecruitPanel GroupBoxViewer = null!;

    //true when j was pressed — the next selfprofile response triggers group highlighting instead of opening the panel
    private bool GroupHighlightRequested;
    private float GroupHighlightTimer;
    private GroupTabControl GroupPanel = null!;
    private HotkeyHelpControl HotkeyHelp = null!;
    private PanelSlot? HoveredInventorySlot;
    private bool IsGameMaster => WorldState.Attributes.IsGameMaster;
    private ItemTooltipControl ItemTooltip = null!;
    private LargeWorldHudControl LargeHud = null!;
    private TileClickTracker LeftClickTracker;
    private readonly LightingSystem Lighting = new();
    private PauseMenuControl PauseMenu = null!;
    private MapFile? MapFile;
    private MapLoadingBar MapLoading = null!;
    private Pathfinder? MapPathfinder;
    private bool MapPreloaded;
    private List<IPoint> MapWaterTiles = [];
    private MapRenderer MapRenderer = null!;

    //overlay panels (rendered on top of hud)
    private NotepadControl Notepad = null!;
    private NpcSessionControl NpcSession = null!;
    private OtherProfileTabControl OtherProfile = null!;
    private Action? PendingBoardSuccessAction;
    private Action? PendingDeleteAction;

    //entity captured on first right-click so a follow-up double-click can still target it even if pathfinding has shifted the camera between clicks
    private uint? PendingDoubleClickEntityId;
    private int PendingDoubleClickTick;
    private bool PendingLoginSwitch;
    private byte[] PlayerPortrait = [];
    private SelfProfileTextEditorControl SelfProfileTextEditor = null!;
    private Direction? QueuedWalkDirection;
    private bool RedirectInProgress;
    private TileClickTracker RightClickTracker;
    private RasterizerState ScissorRasterizerState = null!;

    //true when the client explicitly requested its own profile — prevents unsolicited selfprofile packets from opening the panel
    private OptionsModalControl OptionsModal = null!;
    private KeybindCaptureControl KeybindCapture = null!;
    private bool SelfProfileRequested;
    private StatusBookTab SelfProfileRequestedTab = StatusBookTab.Equipment;
    private SilhouetteRenderer SilhouetteRenderer = null!;
    private WorldHudControl SmallHud = null!;
    private SystemMessagePaneControl SystemMessagePane = null!;
    private SocialStatusControl SocialStatusPicker = null!;
    private long LastSpacebarMs;
    private SelfProfileTabControl StatusBook = null!;
    private TabMapEntity[] TabMapEntities = [];
    private TabMapRenderer TabMapRenderer = null!;
    private bool TabMapVisible;
    private TextPopupControl TextPopup = null!;
    private MarkdownView MarkdownNotice = null!;
    private Texture2D? TileCursorDragTexture;

    //tile cursor: dashed ellipse drawn on the hovered tile
    private Texture2D? TileCursorTexture;
    private IWorldHud WorldHud = null!;
    private WorldListControl WorldList = null!;
    private TownMapControl TownMapControl = null!;
    private WorldMap WorldMap = null!;

    /// <inheritdoc />
    public UIPanel? Root { get; private set; }

    /// <inheritdoc />
    public void Dispose() { }

    /// <inheritdoc />
    public void Initialize(ChaosGame game)
    {
        Game = game;
        WireServerEvents();
    }



    /// <inheritdoc />
    public void LoadContent(GraphicsDevice graphicsDevice)
    {
        Device = graphicsDevice;

        //create both hud layouts — '/' key swaps between them
        //zindex=-1 so hud frames render behind all popup panels
        SmallHud = new WorldHudControl
        {
            ZIndex = -1
        };

        LargeHud = new LargeWorldHudControl
        {
            Visible = false,
            ZIndex = -1
        };
        WorldHud = SmallHud;

        var viewport = WorldHud.ViewportBounds;

        //shared floating system-message pane — lives at Root so its fade timer keeps ticking
        //across HUD swaps. Repositioned in SwapHudLayout when the active HUD changes.
        SystemMessagePane = new SystemMessagePaneControl(viewport)
        {
            ZIndex = -1
        };

        Camera = new Camera(viewport.Width, viewport.Height)
        {
            Offset = new Vector2(-28, 24)
        };
        MapRenderer = new MapRenderer();
        TabMapRenderer = new TabMapRenderer();
        SilhouetteRenderer = new SilhouetteRenderer(graphicsDevice);
        DarknessRenderer = new DarknessRenderer(graphicsDevice);
        WeatherRenderer = new WeatherRenderer();

        ScissorRasterizerState = new RasterizerState
        {
            ScissorTestEnable = true
        };

        TileCursorTexture = CreateTileCursorTexture(graphicsDevice, new Color(247, 142, 24));
        TileCursorDragTexture = CreateTileCursorTexture(graphicsDevice, new Color(100, 149, 237));

        //overlay panels — zindex: -2 sub-panels, -1 slide panels, 0 standard (default), 1 popups, 2 context menu
        NpcSession = new NpcSessionControl();
        WireNpcSession();

        PauseMenu = new PauseMenuControl
        {
            ZIndex = -2
        };
        PauseMenu.SetViewportBounds(WorldHud.ViewportBounds);
        WireOptionsDialog();

        //initialize client-local settings into useroptions from persisted config
        var userOptions = WorldState.UserOptions;
        userOptions.SetValue(6, ClientSettings.UseGroupWindow);
        userOptions.SetValue(8, ClientSettings.ScrollLevel > 0);
        userOptions.SetValue(9, ClientSettings.UseShiftKeyForAltPanels);
        userOptions.SetValue(10, ClientSettings.EnableProfileClick);
        userOptions.SetValue(11, ClientSettings.RecordNpcChat);
        userOptions.SetValue(12, ClientSettings.GroupOpen);

        //route user-initiated toggles to server or local persistence (named so UnloadContent can detach it
        //from the static WorldState.UserOptions — otherwise a handler leaks per world re-entry)
        userOptions.SettingToggled += HandleSettingToggled;

        //ZIndex 10 = the top modal tier (matches the exit/disconnect popups), so it renders above the HUD
        //(-1). No SetViewportBounds call: it keeps the default full-window viewport and centers on the window
        //rather than the play-area viewport.
        OptionsModal = new OptionsModalControl
        {
            ZIndex = 10
        };
        OptionsModal.SettingsRequested += () => Game.Connection.SendOptionToggle(UserOption.Request);
        OptionsModal.FriendsCommitted += SavePlayerFriendList;

        //the keybind capture modal rides above the Options modal (ZIndex 11 > 10). The tab requests a rebind
        //for a command/slot; on commit we persist the override and repaint the tab underneath. Reset routes
        //through here too, so mutate-then-persist-then-repaint lives in one keybind seam.
        KeybindCapture = new KeybindCaptureControl
        {
            ZIndex = 11
        };
        OptionsModal.KeybindRebindRequested += KeybindCapture.Show;
        KeybindCapture.Committed += (id, slot, chord) => ApplyKeybind(() => Keybinds.SetChord(id, slot, chord));
        OptionsModal.KeybindResetRequested += id => ApplyKeybind(() => Keybinds.ResetToDefault(id));
        OptionsModal.KeybindResetAllRequested += () => ApplyKeybind(Keybinds.ResetAll);

        HotkeyHelp = new HotkeyHelpControl();

        GroupPanel = new GroupTabControl();

        GroupPanel.MembersPanel.OnKick += name =>
        {
            Game.Connection.SendGroupInvite(ClientGroupSwitch.TryInvite, name);
            // Retail sends a SelfProfileRequest (0x2D) after a kick to refresh group state.
            // Ref: docs/research/group-ui-original-re.md §7.2.7.
            Game.Connection.RequestSelfProfile();
        };

        GroupPanel.RecruitPanel.OnCreateGroupBox += (
            name,
            note,
            minLvl,
            maxLvl,
            maxW,
            maxWiz,
            maxR,
            maxP,
            maxM) =>
        {
            Game.Connection.SendCreateGroupBox(
                WorldState.PlayerName,
                name,
                note,
                minLvl,
                maxLvl,
                maxW,
                maxWiz,
                maxR,
                maxP,
                maxM);
            WorldState.Group.MarkGroupBoxActive();
        };

        GroupPanel.RecruitPanel.OnRemoveGroupBox += () =>
        {
            //RemoveGroupBox (0x2E/6) writes the owner's own name in the TargetName
            //field on the wire per the retail client (ref: group-ui-original-re.md
            //§6.3). The server doesn't validate the value but protocol parity matters.
            Game.Connection.SendGroupInvite(ClientGroupSwitch.RemoveGroupBox, WorldState.PlayerName);
            //Retail sends a SelfProfileRequest (0x2D) after RemoveGroupBox so the
            //server's profile response confirms the state transition. Queue both
            //packets on the wire before flipping the local flag.
            //Ref: docs/research/group-ui-original-re.md §7.2.7.
            Game.Connection.RequestSelfProfile();
            WorldState.Group.MarkGroupBoxInactive();

            //Server's RemoveGroupBox handler sets Aisling.GroupBox = null but does
            //NOT broadcast Display(), so no fresh DisplayAisling (0x33) packet
            //arrives and WorldEntity.GroupBoxText stays stale. Clear our own
            //overhead banner manually.
            //Ref: docs/research/group-protocol-spec.md §Gap 2.
            if (WorldState.GetPlayerEntity() is { } player)
                player.GroupBoxText = null;
        };

        GroupPanel.RecruitPanel.OnRequestJoin += name => Game.Connection.SendGroupInvite(ClientGroupSwitch.RequestToJoin, name);

        // When the user clicks TAB1, query the server for our own box if we have one active.
        // The server's ShowGroupBox(self) response routes to GroupPanel.ShowRecruitOwnerEdit
        // via HandleGroupInviteReceived, populating OwnerEdit mode. Otherwise RecruitPanel
        // stays in its default OwnerNew (blank) state.
        GroupPanel.OnRecruitTabOpened += () =>
        {
            if (WorldState.Group.HasActiveGroupBox)
                Game.Connection.SendGroupInvite(ClientGroupSwitch.ViewGroupBox, WorldState.PlayerName);
            //else: no action. GroupTabControl.ShowMembers already primed RecruitPanel to
            //OwnerNew mode with defaults once per panel-open, so tab toggles preserve any
            //in-progress typing in the recruit fields.
        };

        GroupBoxViewer = new GroupRecruitPanel(true);

        GroupBoxViewer.OnRequestJoin += name => Game.Connection.SendGroupInvite(ClientGroupSwitch.RequestToJoin, name);

        WorldList = new WorldListControl(Game.Connection.ServerName)
        {
            ZIndex = -2
        };
        WorldList.SetViewportBounds(WorldHud.ViewportBounds);

        Exchange = new ExchangeControl(WorldHud.ViewportBounds);

        GoldDrop = new GoldAmountControl
        {
            ZIndex = 2
        };

        GoldDrop.OnConfirm += amount =>
        {
            if (Exchange.Visible && (GoldDrop.TargetEntityId == Exchange.OtherUserId))
                Game.Connection.SendExchangeInteraction(ExchangeRequestType.SetGold, Exchange.OtherUserId, goldAmount: (int)amount);
            else if (GoldDrop.TargetEntityId.HasValue)
                Game.Connection.DropGoldOnCreature((int)amount, GoldDrop.TargetEntityId.Value);
            else
                Game.Connection.DropGold((int)amount, GoldDrop.TargetTileX, GoldDrop.TargetTileY);
        };

        //match retail: while the gold amount popup is open, the HUD description bar shows what's
        //being operated on even though nothing is hovered. clear it when the popup closes.
        GoldDrop.Closed += () => WorldHud.SetDescription(null);

        ItemAmount = new ItemAmountControl
        {
            ZIndex = 2
        };

        ItemAmount.OnConfirm += amount =>
        {
            Game.Connection.SendExchangeInteraction(
                ExchangeRequestType.AddStackableItem,
                Exchange.OtherUserId,
                ItemAmount.ItemSlot,
                (byte)Math.Min(amount, byte.MaxValue));
        };

        ItemAmount.Closed += () => WorldHud.SetDescription(null);

        //ZIndex 10 = the top modal tier, and no SetViewportBounds — like the Options modal it centers on the
        //window rather than the play area, which is smaller than the modal.
        BoardsModal = new BoardsModalControl
        {
            ZIndex = 10
        };
        DeleteConfirm = new OkPopupMessageControl(true)
        {
            Name = "DeleteConfirm"
        };
        BoardResponsePopup = new OkPopupMessageControl
        {
            Name = "BoardResponsePopup"
        };

        BoardResponsePopup.OnOk += () => BoardResponsePopup.Hide();

        ExchangeResultPopup = new OkPopupMessageControl
        {
            ZIndex = 3,
            Name = "ExchangeResultPopup"
        };
        ExchangeResultPopup.OnOk += () => ExchangeResultPopup.Hide();

        DisconnectPopup = new OkPopupMessageControl(true)
        {
            ZIndex = 10,
            Name = "DisconnectPopup"
        };

        DisconnectPopup.OnOk += () =>
        {
            DisconnectPopup.Hide();
            Game.Screens.Switch(new LobbyLoginScreen());
        };
        DisconnectPopup.OnCancel += () => Game.Exit();

        ExitConfirmPopup = new OkPopupMessageControl
        {
            ZIndex = 10,
            Name = "ExitConfirmPopup"
        };
        ExitConfirmPopup.OnOk += ConfirmExit;

        WireExchange();
        WireBoardControls();

        StatusBook = new SelfProfileTabControl
        {
            ZIndex = 2
        };

        StatusBook.OnUnequip += slot => Game.Connection.Unequip(slot);
        StatusBook.OnClose += SavePlayerFamilyList;

        //route through UserOptions.Toggle so the F4 settings panel and HUD indicator stay in sync
        StatusBook.OnGroupToggled += () => WorldState.UserOptions.Toggle(12);

        StatusBook.OnProfileTextClicked += () =>
        {
            SelfProfileTextEditor.Show(StatusBook.GetProfileText());
        };

        StatusBook.OnAbilityDetailRequested += entry =>
        {
            AbilityMetadataDetails.ShowEntry(entry, WorldHud.ViewportBounds);
        };
        StatusBook.OnEventDetailRequested += (entry, state) => EventMetadataDetails.ShowEntry(entry, state, WorldHud.ViewportBounds);

        SelfProfileTextEditor = new SelfProfileTextEditorControl
        {
            ZIndex = 3
        };

        SelfProfileTextEditor.OnSave += text =>
        {
            StatusBook.SetProfileText(text);
            SaveProfileText(text);
        };

        AbilityMetadataDetails = new AbilityMetadataDetailsControl
        {
            ZIndex = 3
        };

        EventMetadataDetails = new EventMetadataDetailsControl
        {
            ZIndex = 3
        };

        SocialStatusPicker = new SocialStatusControl();

        SocialStatusPicker.OnStatusSelected += status =>
        {
            Game.Connection.SendSocialStatus(status);
            StatusBook.SetEmoticonState((byte)status, UiComponentRepository.GetSocialStatusName(status));

            var emoteIcon = UiRenderer.Instance?.GetEpfTexture("emot000.epf", (int)status * 3);

            if (emoteIcon is not null)
                UpdateHuds(HudOps.SetEmoteIcon, emoteIcon);
        };

        TextPopup = new TextPopupControl
        {
            ZIndex = 2
        };

        MarkdownNotice = new MarkdownView(Device, viewport)
        {
            ZIndex = 4
        };

        Notepad = new NotepadControl
        {
            ZIndex = 2
        };
        Notepad.OnSave += (slot, text) => Game.Connection.SendSetNotepad(slot, text);

        OtherProfile = new OtherProfileTabControl
        {
            ZIndex = 2
        };
        OtherProfile.OnGroupInviteRequested += name => Game.Connection.SendGroupInvite(ClientGroupSwitch.TryInvite, name);

        //no SetViewportBounds: like the Options modal it centers on the window, matching where the legacy chant
        //popup put itself (CenterOnScreen).
        CastablePopup = new CastablePopupControl
        {
            ZIndex = 2
        };
        CastablePopup.OnChantSet += HandleChantSet;

        WorldMap = new WorldMap(Game.Connection)
        {
            ZIndex = 2
        };

        TownMapControl = new TownMapControl();

        MapLoading = new MapLoadingBar
        {
            ZIndex = 5
        };
        MapLoading.CenterIn(viewport);

        AislingContext = new AislingContextMenu
        {
            ZIndex = 3
        };

        DoorContext = new DoorContextMenu
        {
            ZIndex = 3
        };

        ItemTooltip = new ItemTooltipControl
        {
            ZIndex = 3
        };

        Root = new WorldRootPanel(this)
        {
            Name = "WorldRoot",
            Width = ChaosGame.VIRTUAL_WIDTH,
            Height = ChaosGame.VIRTUAL_HEIGHT
        };
        Root.AddChild(SmallHud);
        Root.AddChild(LargeHud);
        Root.AddChild(SystemMessagePane);
        Root.AddChild(NpcSession);
        Root.AddChild(ItemTooltip);
        Root.AddChild(PauseMenu);
        Root.AddChild(OptionsModal);
        Root.AddChild(KeybindCapture);
        Root.AddChild(HotkeyHelp);
        Root.AddChild(GroupPanel);
        Root.AddChild(GroupBoxViewer);
        Root.AddChild(WorldList);
        Root.AddChild(Exchange);
        Root.AddChild(GoldDrop);
        Root.AddChild(ItemAmount);
        Root.AddChild(BoardsModal);
        Root.AddChild(DeleteConfirm);
        Root.AddChild(BoardResponsePopup);
        Root.AddChild(ExchangeResultPopup);
        Root.AddChild(StatusBook);
        Root.AddChild(SelfProfileTextEditor);
        Root.AddChild(AbilityMetadataDetails);
        Root.AddChild(EventMetadataDetails);
        Root.AddChild(OtherProfile);
        Root.AddChild(TextPopup);
        Root.AddChild(MarkdownNotice);
        Root.AddChild(Notepad);
        Root.AddChild(CastablePopup);
        Root.AddChild(WorldMap);
        Root.AddChild(SocialStatusPicker);
        Root.AddChild(AislingContext);
        Root.AddChild(DoorContext);

        Root.AddChild(TownMapControl);
        Root.AddChild(MapLoading);
        Root.AddChild(DisconnectPopup);
        Root.AddChild(ExitConfirmPopup);

        WireHudPanels(SmallHud);
        WireHudPanels(LargeHud);

        //build ui atlas after all hud controls are constructed
        UiRenderer.Instance?.BuildAtlas();

        //load local portrait and profile text from character folder
        var playerName = Game.Connection.AislingName;
        PlayerPortrait = LoadPortraitFile(playerName);
        StatusBook.SetProfileText(LoadProfileText());
    }

    //single keybind-persistence seam: mutate the registry, persist, then repaint the Options tab. Both the
    //capture-commit and the per-command reset route through here so the mutate→save→refresh order lives once.
    private void ApplyKeybind(Action mutate)
    {
        mutate();
        Keybinds.Save();
        OptionsModal.RefreshKeybinds();
    }

    /// <inheritdoc />
    //user-initiated settings toggle: server settings route to the server, client settings persist locally.
    private void HandleSettingToggled(int index, bool value)
    {
        if (UserOptions.IsServerSetting(index))
        {
            var option = (UserOption)(index + 1);
            Game.Connection.SendOptionToggle(option);

            return;
        }

        switch (index)
        {
            case 6:
                ClientSettings.UseGroupWindow = value;

                break;
            case 8:
                ClientSettings.ScrollLevel = value ? 1 : 0;

                break;
            case 9:
                ClientSettings.UseShiftKeyForAltPanels = value;

                break;
            case 10:
                ClientSettings.EnableProfileClick = value;

                break;
            case 11:
                ClientSettings.RecordNpcChat = value;

                break;
            case 12:
                //optimistic local repaint — Hybrasyl's 0x2F handler doesn't push a profile back unless the
                //toggle actually leaves a group, and retail's response shape is unverified. Legacy clients flip
                //the indicator on click; we match that. Subsequent SelfProfile updates (e.g., on next /stats)
                //will reconcile if server state diverges.
                WorldHud.SetGroupOpen(value);
                StatusBook.SetGroupOpen(value);
                Game.Connection.ToggleGroup();

                return;
        }

        ClientSettings.Save();
    }

    public void UnloadContent()
    {
        WorldState.UserOptions.SettingToggled -= HandleSettingToggled;
        Game.Connection.OnUserId -= HandleUserId;
        Game.Connection.OnMapInfo -= HandleMapInfo;
        Game.Connection.OnMapData -= HandleMapData;
        Game.Connection.OnMapLoadComplete -= HandleMapLoadComplete;
        Game.Connection.OnLocationChanged -= HandleLocationChanged;
        Game.Connection.OnDisplayAisling -= HandleDisplayAisling;
        Game.Connection.OnRemoveEntity -= HandleRemoveEntity;
        Game.Connection.OnClientWalkResponse -= HandleClientWalkResponse;
        Game.Connection.OnDisplayPublicMessage -= HandleDisplayPublicMessage;
        Game.Connection.OnServerMessage -= HandleServerMessage;
        WorldState.NpcInteraction.DialogChanged -= HandleDialogChanged;
        WorldState.NpcInteraction.MenuChanged -= HandleMenuChanged;
        Game.Connection.OnRefreshResponse -= HandleRefreshResponse;
        WorldState.Exchange.AmountRequested -= HandleExchangeAmountRequested;
        WorldState.Exchange.Closed -= HandleExchangeClosed;
        WorldState.Board.PostListChanged -= HandleBoardPostListChanged;
        WorldState.Board.PostViewed -= HandleBoardPostViewed;
        WorldState.Board.BoardListReceived -= HandleBoardListReceived;
        WorldState.Board.SessionClosed -= HideAllBoardControls;
        WorldState.Board.ResponseReceived -= HandleBoardResponse;
        WorldState.Board.SessionClosed -= ResetBulletinButtonSelection;
        WorldState.Board.SessionClosed -= ResetMailButtonSelection;
        WorldState.GroupInvite.Received -= HandleGroupInviteReceived;
        Game.Connection.OnEditableProfileRequest -= HandleEditableProfileRequest;
        Game.Connection.OnSelfProfile -= HandleSelfProfile;
        Game.Connection.OnOtherProfile -= HandleOtherProfile;
        Game.Connection.OnBodyAnimation -= HandleBodyAnimation;
        Game.Connection.OnAnimation -= HandleAnimation;
        Game.Connection.OnSound -= HandleSound;
        Game.Connection.OnCancelCasting -= CastingSystem.CancelChant;
        Game.Connection.OnMapChangePending -= HandleMapChangePending;
        Game.Connection.OnExitResponse -= HandleExitResponse;
        Game.Connection.OnRedirectReceived -= HandleRedirectReceived;
        Game.Connection.StateChanged -= HandleStateChanged;
        Game.Connection.OnHealthBar -= HandleHealthBar;
        Game.Connection.OnEffect -= HandleEffect;
        Game.Connection.OnLightLevel -= HandleLightLevel;
        Game.OnMetaDataSyncComplete -= HandleMetaDataSyncComplete;
        Game.Connection.OnDisplayReadonlyNotepad -= HandleDisplayReadonlyNotepad;
        Game.Connection.OnDisplayEditableNotepad -= HandleDisplayEditableNotepad;
        Game.Connection.OnWorldMap -= HandleWorldMap;
        Game.Connection.OnDoor -= HandleDoor;

        //unwire panel click-to-use events
        WorldHud.Inventory.OnSlotClicked -= HandleInventorySlotClicked;
        WorldHud.SkillBook.OnSlotClicked -= HandleSkillSlotClicked;
        WorldHud.SkillBookAlt.OnSlotClicked -= HandleSkillSlotClicked;
        WorldHud.SpellBook.OnSlotClicked -= HandleSpellSlotClicked;
        WorldHud.SpellBookAlt.OnSlotClicked -= HandleSpellSlotClicked;
        WorldHud.Tools.WorldSkills.OnSlotClicked -= HandleSkillSlotClicked;
        WorldHud.Tools.WorldSpells.OnSlotClicked -= HandleSpellSlotClicked;

        WorldState.ResetAll();

        MapRenderer.Dispose();
        TabMapRenderer.Dispose();
        ScissorRasterizerState.Dispose();
        DarknessRenderer.Dispose();
        WeatherRenderer.Dispose();
        SilhouetteRenderer.Dispose();
        Root?.Dispose();
        Game.AislingRenderer.ClearCompositeCache();
        Game.AislingRenderer.ClearGroupTintCache();
        Game.CreatureRenderer.ClearTintCaches();
        Game.ItemRenderer.Clear();
        Overlays.Clear();
        DebugRenderer.Clear();
    }
}