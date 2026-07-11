#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Controls.Scrolling;
using Brigid.Networking;
using Brigid.Rendering.Models;
using Chaos.DarkAges.Definitions;
using DALib.Networking.Packets.Server;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Controls.World.Popups.Dialog;

/// <summary>
///     NPC dialog/menu session container using the lnpcd prefab. This is the Layer 1 container that is always visible when
///     any dialog or menu is open. It provides the bottom bar (nd_talk.spf), NPC portrait (nd_npcbg.spf), NPC name, dialog
///     text, and navigation buttons (Next/Prev/Close/Top). Content sub-panels (Layer 2) float on top for specific
///     interaction types.
/// </summary>
public sealed class NpcSessionControl : PrefabPanel
{
    //scroll arrow buttons for dialog text overflow (nd_arw.spf)
    private const float ARROW_ANIM_INTERVAL = 0.5f;

    //container controls from lnpcd prefab
    private readonly UIButton? CloseButton;
    private readonly UILabel DialogTextLabel;
    private readonly BankShopPanel MenuShop;
    private readonly UIButton? NextButton;

    private readonly UILabel? NpcNameLabel;
    private readonly UIImage? NpcTileImage;
    private readonly UIImage? MessageBarImage;
    private readonly DialogAlphaGradient AlphaGradient;

    /// <summary>
    ///     When true, the legacy session chrome (bottom dialog bar, NPC portrait, name, dialog text, nav buttons) is
    ///     hidden so a from-scratch content panel — currently the <see cref="BankShopPanel" /> for ShowItems — owns the
    ///     whole screen region. <see cref="WorldScreen" /> also skips portrait rendering while this is set.
    /// </summary>
    public bool ChromeSuppressed { get; private set; }
    private readonly Rectangle PortraitRect;
    private readonly UIButton? PreviousButton;

    //sub-panels (layer 2 content) — exposed for focus tracking by inputdispatcher
    public DialogTextEntryPanel DialogTextEntry { get; }
    public MenuListPanel MenuList { get; }
    public MenuTextEntryPanel MenuTextEntry { get; }
    public DialogOptionPanel DialogOption { get; }
    public DialogProtectedTextEntryPanel DialogProtectedTextEntry { get; }
    private readonly UIButton? ScrollDownButton;
    private readonly Texture2D?[] ScrollDownFrames = new Texture2D?[2];
    private readonly UIButton? ScrollUpButton;
    private readonly Texture2D?[] ScrollUpFrames = new Texture2D?[2];
    private readonly UIButton? TopButton;
    private bool ArrowAnimFrame;
    private float ArrowAnimTimer;
    private bool OwnsPortraitTexture;
    private SpriteFrame? PortraitSpriteFrame;

    //portrait texture (owned illustration or cached sprite frame)
    private Texture2D? PortraitTexture;

    //arrows-only dialog-text scroll: a bare model (no bar) drives the label offset; the nd_arw.spf buttons stay
    private readonly ScrollModel DialogScroll = new();
    private readonly LabelScrollSource DialogSource;
    public DialogType? CurrentDialogType { get; private set; }
    public MenuType? CurrentMenuType { get; private set; }
    public ushort DialogId { get; private set; }
    public bool IsDialogOpcode { get; private set; }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────────
    //  RETAIL-COMPAT DIALOG PACING LATCH  —  DIVERGENCE POINT
    //
    //  The retail world server validates that each 0x3A dialog response's pursuitIndex is within ±1 of the
    //  server's own tracked position, and DROPS THE CONNECTION on a violation (see 0x3A-dialog-use.md). Our
    //  DialogId only advances when the server's *next* dialog packet arrives, so a second click before that
    //  packet resends a stale index (DialogId + 1 twice) → the index desyncs past ±1 → retail disconnects and
    //  the user is kicked to login. Retail's own client sidesteps this by making the dialog inert until the
    //  next frame arrives — you physically cannot click ahead.
    //
    //  So we hard-gate to ONE outstanding dialog action: after any advancing response is sent, further
    //  advancing input is dropped and the nav buttons are disabled until the next dialog/menu packet
    //  (ShowDialog/ShowMenu) or the session closes (HideAll). A safety timeout releases the latch if the
    //  expected packet never arrives (dropped packet) so the user is never permanently stranded.
    //
    //  ⚠ DIVERGE WHEN ON HYBRASYL: this lock exists ONLY to satisfy retail's strict ±1 validation. Once the
    //  client can detect it is talking to a Hybrasyl server (capability handshake), Hybrasyl can accept
    //  optimistic / queued dialog input and this whole latch should be relaxed or removed for that server —
    //  it needlessly throttles a modern server that does not punish fast input. Gate the divergence on that
    //  handshake, do not delete this for retail.
    // ─────────────────────────────────────────────────────────────────────────────────────────────────────
    private const float ResponsePendingTimeoutMs = 5000f;
    private bool ResponsePending;
    private float ResponsePendingElapsedMs;
    private bool LastHasNext;
    private bool LastHasPrevious;

    //menu args echoed back for menuwithargs
    public string? MenuArgs { get; private set; }

    //portrait metadata for rendering by worldscreen
    public string? NpcName { get; private set; }
    public DisplayColor PortraitColor { get; private set; }
    public ushort PortraitSpriteId { get; private set; }
    public ushort PursuitId { get; private set; }

    /// <summary>
    ///     Server-sent index into the NPC's variant list. Picks which SPF filename to load when the current NPC
    ///     name has multiple variants defined in <c>npci.tbl</c> or the server's <c>NPCIllust</c> metafile. 0 is the
    ///     near-universal default and means "first filename".
    /// </summary>
    public byte IllustrationIndex { get; private set; }

    //session state
    public EntityType SourceEntityType { get; private set; }

    /// <summary>
    ///     The raw wire object-type byte (creature 1, item 2, reactor 4, castable 5, async 0xFE). Echoed verbatim
    ///     in 0x39/0x3A responses — the server branches its lookup on it, so it must not be collapsed to
    ///     <see cref="SourceEntityType" /> (which only covers rendering).
    /// </summary>
    public byte SourceObjectType { get; private set; }

    public uint? SourceId { get; private set; }

    //speak dialog: prompt prefix and epilog suffix for the say broadcast
    public string? SpeakEpilog { get; private set; }
    public string? SpeakPrompt { get; private set; }

    public NpcSessionControl()
        : base("lnpcd", false)
    {
        Name = "NpcSession";
        Visible = false;
        UsesControlStack = true;
        X = 0;
        Y = 0;

        //darkness gradient (behind everything else in the dialog)
        AlphaGradient = new DialogAlphaGradient();
        AddChild(AlphaGradient);

        //background images (drawn after gradient so they render on top of it)
        MessageBarImage = CreateImage("MessageDialog"); //nd_talk.spf — bottom dialog bar
        NpcTileImage = CreateImage("NPCTile"); //nd_npcbg.spf — portrait background

        //container buttons (added after images so they draw on top)
        CloseButton = CreateButton("CloseBtn");
        NextButton = CreateButton("NextBtn");
        PreviousButton = CreateButton("PrevBtn");
        TopButton = CreateButton("TopBtn");

        //scroll arrow buttons for dialog text overflow (nd_arw.spf: 0-1 = up, 2-3 = down)
        var uiCache = UiRenderer.Instance!;
        ScrollUpFrames[0] = uiCache.GetSpfTexture("nd_arw.spf");
        ScrollUpFrames[1] = uiCache.GetSpfTexture("nd_arw.spf", 1);
        ScrollDownFrames[0] = uiCache.GetSpfTexture("nd_arw.spf", 2);
        ScrollDownFrames[1] = uiCache.GetSpfTexture("nd_arw.spf", 3);
        var upArrowTexture = ScrollUpFrames[0]!;
        var downArrowTexture = ScrollDownFrames[0]!;

        if (CloseButton is not null)
        {
            ScrollDownButton = new UIButton
            {
                Name = "ScrollDown",
                NormalTexture = downArrowTexture,
                X = CloseButton.X + CloseButton.Width - downArrowTexture.Width - 3,
                Y = CloseButton.Y - downArrowTexture.Height - 1,
                Width = downArrowTexture.Width,
                Height = downArrowTexture.Height,
                Visible = false
            };

            ScrollUpButton = new UIButton
            {
                Name = "ScrollUp",
                NormalTexture = upArrowTexture,
                X = ScrollDownButton.X - 3,
                Y = ScrollDownButton.Y - upArrowTexture.Height - 27,
                Width = upArrowTexture.Width,
                Height = upArrowTexture.Height,
                Visible = false
            };

            AddChild(ScrollDownButton);
            AddChild(ScrollUpButton);

            ScrollDownButton.Clicked += () => ScrollText(1);
            ScrollUpButton.Clicked += () => ScrollText(-1);
        }

        //layout rects
        NpcNameLabel = CreateLabel("Name");
        PortraitRect = GetRect("NPCTile");

        //dialog text label — word-wrapped, shifted up 10px from prefab rect
        var textRect = GetRect("Text");

        DialogTextLabel = new UILabel
        {
            X = textRect.X,
            Y = textRect.Y + 1,
            Width = textRect.Width,
            Height = 3 * TextRenderer.CHAR_HEIGHT + 2,
            WordWrap = true,
            ForegroundColor = TextColors.Default
        };

        AddChild(DialogTextLabel);

        DialogSource = new LabelScrollSource(DialogTextLabel);
        DialogScroll.Changed += DialogSource.ApplyOffset;

        //wire container button events
        if (CloseButton is not null)
            CloseButton.Clicked += () =>
            {
                HideAll();
                OnClose?.Invoke();
            };

        if (NextButton is not null)
            NextButton.Clicked += () =>
            {
                if (BeginResponse())
                    OnNext?.Invoke();
            };

        if (PreviousButton is not null)
            PreviousButton.Clicked += () =>
            {
                if (BeginResponse())
                    OnPrevious?.Invoke();
            };

        if (TopButton is not null)
            TopButton.Clicked += () =>
            {
                if (!BeginResponse())
                    return;

                HideAll();
                OnTop?.Invoke();
            };

        //create sub-panels as children
        DialogOption = new DialogOptionPanel();
        DialogTextEntry = new DialogTextEntryPanel();
        MenuTextEntry = new MenuTextEntryPanel();
        MenuShop = new BankShopPanel();
        MenuList = new MenuListPanel();
        DialogProtectedTextEntry = new DialogProtectedTextEntryPanel();

        AddChild(DialogOption);
        AddChild(DialogTextEntry);
        AddChild(MenuTextEntry);
        AddChild(MenuShop);
        AddChild(MenuList);
        AddChild(DialogProtectedTextEntry);

        //wire sub-panel events — forward to container events. advancing responses pass through the pacing latch.
        DialogOption.OnOptionSelected += index =>
        {
            if (BeginResponse())
                OnOptionSelected?.Invoke(index);
        };

        DialogOption.OnClose += () =>
        {
            HideAll();
            OnClose?.Invoke();
        };

        DialogTextEntry.OnTextSubmit += text =>
        {
            if (BeginResponse())
                OnTextSubmit?.Invoke(text);
        };

        DialogTextEntry.OnClose += () =>
        {
            HideAll();
            OnClose?.Invoke();
        };

        MenuTextEntry.OnTextSubmit += text =>
        {
            if (BeginResponse())
                OnTextSubmit?.Invoke(text);
        };

        MenuTextEntry.OnClose += () =>
        {
            HideAll();
            OnClose?.Invoke();
        };

        MenuShop.OnItemSelected += index =>
        {
            if (BeginResponse())
                OnMerchantItemSelected?.Invoke(index);
        };

        MenuShop.OnClose += () =>
        {
            HideAll();
            OnClose?.Invoke();
        };

        MenuList.OnItemSelected += index =>
        {
            if (BeginResponse())
                OnListItemSelected?.Invoke(index);
        };

        MenuList.OnClose += () =>
        {
            HideAll();
            OnClose?.Invoke();
        };

        DialogProtectedTextEntry.OnProtectedSubmit += (id, pw) =>
        {
            if (BeginResponse())
                OnProtectedSubmit?.Invoke(id, pw);
        };

        DialogProtectedTextEntry.OnClose += () =>
        {
            HideAll();
            OnClose?.Invoke();
        };
    }

    public override void Dispose()
    {
        DisposePortrait();
        base.Dispose();
    }

    private void DisposePortrait()
    {
        if (OwnsPortraitTexture)
            PortraitTexture?.Dispose();

        PortraitTexture = null;
        OwnsPortraitTexture = false;
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        UpdateClipRect();

        if ((ClipRect.Width <= 0) || (ClipRect.Height <= 0))
            return;

        //1. background
        if (Background is not null)
            DrawTexture(
                spriteBatch,
                Background,
                new Vector2(ScreenX, ScreenY),
                Color.White);

        //2. base-layer children (alpha pane, bottom bar, portrait bg, buttons, labels)
        foreach (var child in Children)
            if (child.Visible && !IsSubPanel(child))
            {
                child.Draw(spriteBatch);
                DebugOverlay.DrawElement(spriteBatch, child);
            }

        //3. portrait — on top of base layer, behind sub-panels
        DrawPortrait(spriteBatch);

        //4. sub-panels — always in front of portrait
        foreach (var child in Children)
            if (child.Visible && IsSubPanel(child))
            {
                child.Draw(spriteBatch);
                DebugOverlay.DrawElement(spriteBatch, child);
            }
    }

    private void DrawPortrait(SpriteBatch spriteBatch)
    {
        if (ChromeSuppressed || (PortraitTexture is null))
            return;

        if (OwnsPortraitTexture)
        {
            //npcillustration: left-aligned, bottom edge sits on top of the bottom bar (y=372)
            var illustY = 372 - PortraitTexture.Height;
            DrawTexture(spriteBatch, PortraitTexture, new Vector2(0, illustY), Color.White);
        } else if (PortraitRect != Rectangle.Empty)
        {
            //creature/item sprite: center the sprite's visual anchor in the npctile rect
            var sx = ScreenX;
            var sy = ScreenY;
            var rectCenterX = sx + PortraitRect.X + PortraitRect.Width / 2;
            var rectCenterY = sy + PortraitRect.Y + PortraitRect.Height / 2;

            if (PortraitSpriteFrame is { } frame)
            {
                var drawX = rectCenterX - (PortraitTexture.Width + frame.Left) / 2;
                var drawY = rectCenterY - (PortraitTexture.Height + frame.Top) / 2;

                DrawTexture(spriteBatch, PortraitTexture, new Vector2(drawX, drawY), Color.White);
            } else
                DrawTexture(
                    spriteBatch,
                    PortraitTexture,
                    new Vector2((int)(rectCenterX - PortraitTexture.Width / 2f), (int)(rectCenterY - PortraitTexture.Height / 2f)),
                    Color.White);
        }
    }

    /// <summary>
    ///     Returns the name of the list menu entry at the given index.
    /// </summary>
    public string? GetListEntryName(int index) => MenuList.GetEntryName(index);

    /// <summary>
    ///     Returns the slot byte for the list menu entry at the given index.
    /// </summary>
    public byte? GetListEntrySlot(int index) => MenuList.GetEntrySlot(index);

    /// <summary>
    ///     Returns the previous args string for menu text entry (TextEntryWithArgs).
    /// </summary>
    public string? GetMenuTextPreviousArgs() => MenuTextEntry.PreviousArgs;

    /// <summary>
    ///     Returns the name of the merchant entry at the given index.
    /// </summary>
    public string? GetMerchantEntryName(int index) => MenuShop.GetEntryName(index);

    /// <summary>
    ///     Returns the pursuit ID for the option at the given index in the OptionMenu sub-panel.
    /// </summary>
    public ushort GetOptionPursuitId(int index) => DialogOption.GetOptionPursuitId(index);

    /// <summary>
    ///     Hides the container and all sub-panels.
    /// </summary>
    public void HideAll()
    {
        ResetDialogLock();
        DisposePortrait();
        HideAllSubPanels();

        //restore chrome so the next dialog/menu opens in the default (non-suppressed) state
        ApplyChromeSuppression(false);

        Hide();
    }

    private void HideAllSubPanels()
    {
        DialogOption.Hide();
        DialogTextEntry.Hide();
        MenuTextEntry.Hide();
        MenuShop.Hide();
        MenuList.Hide();
        DialogProtectedTextEntry.Hide();
        DialogTextLabel.Text = string.Empty;
        DialogScroll.Configure(DialogSource);
        UpdateScrollButtons();
    }

    //single owner of the suppress-flag ⇄ chrome-visibility relationship so the two can never drift apart
    private void ApplyChromeSuppression(bool suppressed)
    {
        ChromeSuppressed = suppressed;
        SetSessionChrome(!suppressed);
    }

    /// <summary>
    ///     Toggles the legacy session chrome (bottom dialog bar, NPC name, dialog text). The portrait is gated separately
    ///     in <see cref="DrawPortrait" /> via <see cref="ChromeSuppressed" />; nav buttons are handled by the callers.
    /// </summary>
    private void SetSessionChrome(bool visible)
    {
        //the alpha gradient is shaped for the bottom-anchored dialog; over the centered bank it reads as a stray
        //diagonal shadow, so it's part of the chrome we suppress
        AlphaGradient.Visible = visible;
        MessageBarImage?.Visible = visible;
        NpcNameLabel?.Visible = visible;
        DialogTextLabel.Visible = visible;
    }

    private void HideNavigationButtons()
    {
        if (NextButton is not null)
        {
            NextButton.Visible = false;
            NextButton.Enabled = false;
        }

        if (PreviousButton is not null)
        {
            PreviousButton.Visible = false;
            PreviousButton.Enabled = false;
        }

        if (CloseButton is not null)
        {
            CloseButton.Visible = false;
            CloseButton.Enabled = false;
        }

        if (TopButton is not null)
        {
            TopButton.Visible = false;
            TopButton.Enabled = false;
        }
    }

    private bool IsSubPanel(UIElement child)
        => (child == DialogOption)
           || (child == DialogTextEntry)
           || (child == MenuTextEntry)
           || (child == MenuShop)
           || (child == MenuList)
           || (child == DialogProtectedTextEntry);

    //events — worldscreen.wiring subscribes to these
    public event CloseHandler? OnClose;
    public event ItemSelectedHandler? OnListItemSelected;
    public event ItemSelectedHandler? OnMerchantItemSelected;
    public event NextHandler? OnNext;
    public event OptionSelectedHandler? OnOptionSelected;
    public event PreviousHandler? OnPrevious;
    public event ProtectedSubmitHandler? OnProtectedSubmit;
    public event TextSubmitHandler? OnTextSubmit;
    public event TopHandler? OnTop;

    private void ScrollText(int direction)
    {
        DialogScroll.ScrollBy(direction);
        UpdateScrollButtons();
    }

    private void SetDialogText(string? text)
    {
        DialogTextLabel.Text = text ?? string.Empty;
        DialogScroll.Configure(DialogSource);
        DialogScroll.ScrollToStart();
        UpdateScrollButtons();
    }

    private void SetNavigationButtons(bool hasNext, bool hasPrevious)
    {
        //remembered so the pacing-latch timeout can restore the correct enabled state (see the latch block)
        LastHasNext = hasNext;
        LastHasPrevious = hasPrevious;

        if (NextButton is not null)
        {
            NextButton.Visible = true;
            NextButton.Enabled = hasNext;
        }

        if (PreviousButton is not null)
        {
            PreviousButton.Visible = true;
            PreviousButton.Enabled = hasPrevious;
        }

        if (CloseButton is not null)
        {
            CloseButton.Visible = true;
            CloseButton.Enabled = true;
        }

        if (TopButton is not null)
        {
            TopButton.Visible = false;
            TopButton.Enabled = false;
        }
    }

    /// <summary>
    ///     Gates an advancing dialog response (next/prev/top/option/text/list/merchant). Returns false — dropping
    ///     the input — while a prior response is still awaiting the server's reply. See the pacing-latch block.
    /// </summary>
    private bool BeginResponse()
    {
        if (ResponsePending)
            return false;

        ResponsePending = true;
        ResponsePendingElapsedMs = 0f;

        //visually reflect the lock — the buttons come back when the next dialog packet renders (or on timeout)
        if (NextButton is not null)
            NextButton.Enabled = false;

        if (PreviousButton is not null)
            PreviousButton.Enabled = false;

        if (TopButton is not null)
            TopButton.Enabled = false;

        return true;
    }

    //cleared whenever a new dialog/menu packet arrives or the session closes — the outstanding response resolved
    private void ResetDialogLock() => ResponsePending = false;

    private void TickResponsePending(float elapsedMs)
    {
        if (!ResponsePending)
            return;

        ResponsePendingElapsedMs += elapsedMs;

        if (ResponsePendingElapsedMs < ResponsePendingTimeoutMs)
            return;

        //server never answered (dropped packet) — release the latch so the user can retry instead of being stuck
        ResponsePending = false;

        //don't resurrect the legacy nav buttons over a from-scratch panel that suppresses the chrome (e.g. the bank) —
        //re-showing them would float stale Next/Prev/Close over it, and a stale enabled Next could fire a bogus response
        if (!ChromeSuppressed)
            SetNavigationButtons(LastHasNext, LastHasPrevious);
    }

    /// <summary>
    ///     Sets the NPC portrait texture. If ownsTexture is true, the container will dispose the texture when replaced or
    ///     hidden.
    /// </summary>
    public void SetPortrait(Texture2D? texture, bool ownsTexture)
    {
        DisposePortrait();
        PortraitTexture = texture;
        PortraitSpriteFrame = null;
        OwnsPortraitTexture = ownsTexture;

        //show the npctile background only for sprite portraits (not illustrations, not when hidden)
        NpcTileImage?.Visible = texture is not null && !ownsTexture;
    }

    public void SetPortrait(SpriteFrame spriteFrame)
    {
        DisposePortrait();
        PortraitTexture = spriteFrame.Texture;
        PortraitSpriteFrame = spriteFrame;
        OwnsPortraitTexture = false;

        NpcTileImage?.Visible = true;
    }

    /// <summary>
    ///     Shows the container for a DisplayDialog packet (opcode 0x30).
    /// </summary>
    public void ShowDialog(NpcDialogPacket pkt)
    {
        //the awaited server reply has arrived — release the pacing latch (HideAll also clears it on close)
        ResetDialogLock();

        if (pkt.DialogType is NpcDialogType.Close)
        {
            HideAll();

            return;
        }

        //Chaos DialogType and DALib NpcDialogType share wire byte values; convert and reuse the existing switch.
        var dialogType = (DialogType)(byte)pkt.DialogType;

        //dialogs always use the default chrome (no from-scratch content panel suppresses it)
        ApplyChromeSuppression(false);

        IsDialogOpcode = true;
        CurrentDialogType = dialogType;
        CurrentMenuType = null;
        SourceEntityType = pkt.ObjectType switch
        {
            NpcDialogPacket.ObjectTypeItem => EntityType.Item,
            _                              => EntityType.Creature,
        };
        SourceObjectType = pkt.ObjectType;
        SourceId = pkt.SourceId;
        PursuitId = pkt.PursuitId;
        DialogId = pkt.DialogId;
        NpcName = pkt.Name;
        PortraitSpriteId = pkt.Sprite;
        PortraitColor = (DisplayColor)pkt.Color;
        //the 0x30 wire carries no illustration-index byte (unlike 0x2F); 0 = "first variant"
        IllustrationIndex = 0;

        HideAllSubPanels();
        SetDialogText(pkt.Text);
        SetNavigationButtons(pkt.HasNextButton, pkt.HasPreviousButton);
        MenuArgs = null;
        SpeakPrompt = null;
        SpeakEpilog = null;

        switch (dialogType)
        {
            case DialogType.Normal:
                break;

            case DialogType.DialogMenu:
            case DialogType.CreatureMenu:
                if (pkt.Body is OptionsDialog optionsBody && (optionsBody.Options.Count > 0))
                {
                    var options = optionsBody.Options
                                             .Select(o => (o, (ushort)0))
                                             .ToList();
                    DialogOption.ShowOptions(options);
                }

                break;

            case DialogType.TextEntry:
            case DialogType.Speak:
                HideNavigationButtons();

                var input = pkt.Body as TextInputDialog;
                var prompt = input?.TopCaption ?? string.Empty;
                var epilog = input?.BottomCaption ?? string.Empty;

                if (dialogType is DialogType.Speak)
                {
                    SpeakPrompt = prompt;
                    SpeakEpilog = epilog;
                }

                DialogTextEntry.ShowTextEntry(prompt, (byte)(input?.InputLength ?? 255), epilog);

                break;

            case DialogType.Protected:
                HideNavigationButtons();
                DialogProtectedTextEntry.ShowProtected(pkt.Text);

                break;

            default:
                NoticeDebugLog.Write(
                    $"[NpcDialog] unhandled dialog type 0x{(byte)dialogType:X2} (body {pkt.Body.GetType().Name}) -- not displayed");

                return;
        }

        if (NpcNameLabel is not null)
        {
            NpcNameLabel.Text = pkt.Name;
            NpcNameLabel.ForegroundColor = new Color(0, 255, 0);
        }

        Show();
    }

    /// <summary>
    ///     Shows the container for a DisplayMenu packet (opcode 0x2F).
    /// </summary>
    public void ShowMenu(NpcMenuPacket pkt)
    {
        //the awaited server reply has arrived — release the pacing latch
        ResetDialogLock();

        var menuType = (MenuType)(byte)pkt.MenuType;

        IsDialogOpcode = false;
        CurrentDialogType = null;
        CurrentMenuType = menuType;

        //the from-scratch BankShopPanel owns the whole screen for ShowItems — hide the legacy bar/portrait/name chrome
        ApplyChromeSuppression(menuType == MenuType.ShowItems);

        SourceEntityType = pkt.EntityType switch
        {
            2 => EntityType.Item,
            _ => EntityType.Creature,
        };
        SourceObjectType = pkt.EntityType;
        SourceId = pkt.SourceId;
        PursuitId = MenuPursuitId(pkt.Menu);
        DialogId = 0;
        NpcName = pkt.Name;
        PortraitSpriteId = pkt.Sprite;
        PortraitColor = (DisplayColor)pkt.Color;
        IllustrationIndex = pkt.IllustrationIndex;

        MenuArgs = pkt.Menu switch
        {
            OptionsWithArgumentMenu m   => m.Argument,
            TextEntryWithArgumentMenu m => m.Argument,
            _                           => null,
        };
        HideAllSubPanels();
        HideNavigationButtons();
        SetDialogText(pkt.Text);

        switch (menuType)
        {
            case MenuType.Menu:
            case MenuType.MenuWithArgs:
                var menuOptions = pkt.Menu switch
                {
                    OptionsMenu m             => m.Options,
                    OptionsWithArgumentMenu m => m.Options,
                    _                         => null,
                };

                if (menuOptions is { Count: > 0 })
                {
                    var options = menuOptions
                                  .Select(o => (o.Text, o.Pursuit))
                                  .ToList();
                    DialogOption.ShowOptions(options);
                }

                break;

            case MenuType.TextEntry:
                MenuTextEntry.ShowTextEntry(null);

                break;

            case MenuType.TextEntryWithArgs:
                MenuTextEntry.ShowTextEntry(MenuArgs);

                break;

            case MenuType.ShowItems:
                MenuShop.ShowMerchant(pkt);

                break;

            case MenuType.ShowPlayerItems:
            case MenuType.ShowSkills:
            case MenuType.ShowSpells:
            case MenuType.ShowPlayerSkills:
            case MenuType.ShowPlayerSpells:
                MenuList.ShowList(pkt);

                break;

            default:
                NoticeDebugLog.Write(
                    $"[NpcMenu] unhandled menu type 0x{(byte)pkt.MenuType:X2} (body {pkt.Menu?.GetType().Name ?? "null"}) -- not displayed");

                return;
        }

        //the BankShopPanel supplies its own OK/close; leave the legacy nav buttons hidden when chrome is suppressed
        if (!ChromeSuppressed)
        {
            if (CloseButton is not null)
            {
                CloseButton.Visible = true;
                CloseButton.Enabled = true;
            }

            if (TopButton is not null)
            {
                TopButton.Visible = true;
                TopButton.Enabled = true;
            }

            if (NpcNameLabel is not null)
            {
                NpcNameLabel.Text = pkt.Name;
                NpcNameLabel.ForegroundColor = LegendColors.Lime;
            }
        }

        Show();
    }

    //The menu's pursuit id lives on the body (the prefix carries none); options menus use per-option pursuits.
    private static ushort MenuPursuitId(NpcMenu menu) => menu switch
    {
        TextEntryMenu m             => m.PursuitId,
        TextEntryWithArgumentMenu m => m.PursuitId,
        ItemListMenu m              => m.PursuitId,
        ServerItemMenu              => ServerItemMenu.ServerItemPursuit,
        PlayerItemListMenu m        => m.PursuitId,
        PlayerItemHandleMenu        => PlayerItemHandleMenu.HandlePursuit,
        SpellListMenu m             => m.PursuitId,
        SkillListMenu m             => m.PursuitId,
        PlayerSpellListMenu m       => m.PursuitId,
        PlayerSkillListMenu m       => m.PursuitId,
        _                           => (ushort)0,
    };

    public override void Update(GameTime gameTime)
    {
        //release the pacing latch if the server never answered. runs before the visibility guard below; the
        //latch is only ever pending while the dialog is visible and waiting, so the timeout always gets to fire.
        TickResponsePending((float)gameTime.ElapsedGameTime.TotalMilliseconds);

        if (!Visible || !Enabled)
            return;

        //animate scroll arrow buttons (flip frames every 500ms)
        var anyArrowVisible = ScrollUpButton is { Visible: true } || ScrollDownButton is { Visible: true };

        if (anyArrowVisible)
        {
            ArrowAnimTimer += (float)gameTime.ElapsedGameTime.TotalSeconds;

            if (ArrowAnimTimer >= ARROW_ANIM_INTERVAL)
            {
                ArrowAnimTimer -= ARROW_ANIM_INTERVAL;
                ArrowAnimFrame = !ArrowAnimFrame;
                var frameIndex = ArrowAnimFrame ? 1 : 0;

                if (ScrollUpButton is { Visible: true })
                    ScrollUpButton.NormalTexture = ScrollUpFrames[frameIndex];

                if (ScrollDownButton is { Visible: true })
                    ScrollDownButton.NormalTexture = ScrollDownFrames[frameIndex];
            }
        }

        base.Update(gameTime);
    }

    public override void OnKeyDown(KeyDownEvent e)
    {
        if (e.Key == Keys.Escape)
        {
            HideAll();
            OnClose?.Invoke();
            e.Handled = true;

            return;
        }

        //space/enter — advance normal dialogs via next button, or select first option in menus
        if (e.Key is Keys.Space or Keys.Enter)
        {
            //while awaiting the server's reply, swallow advance keys — don't fall through to the close branch
            //below (Next is temporarily disabled by the pacing latch, which would otherwise read as "no next").
            if (ResponsePending)
            {
                e.Handled = true;

                return;
            }

            if (DialogOption is { Visible: true, OptionCount: > 0 })
            {
                if (BeginResponse())
                    OnOptionSelected?.Invoke(0);

                e.Handled = true;
            } else if (NextButton is { Visible: true, Enabled: true })
            {
                if (BeginResponse())
                    OnNext?.Invoke();

                e.Handled = true;
            } else if (!DialogOption.Visible || (DialogOption.OptionCount == 0))
            {
                HideAll();
                OnClose?.Invoke();
                e.Handled = true;
            }
        }
    }

    private void UpdateScrollButtons()
    {
        if (!DialogScroll.CanScroll)
        {
            ScrollUpButton?.Visible = false;

            ScrollDownButton?.Visible = false;

            return;
        }

        if (ScrollUpButton is not null)
        {
            ScrollUpButton.Visible = DialogScroll.Offset > 0;
            ScrollUpButton.Enabled = DialogScroll.Offset > 0;
        }

        if (ScrollDownButton is not null)
        {
            ScrollDownButton.Visible = DialogScroll.Offset < DialogScroll.Max;
            ScrollDownButton.Enabled = DialogScroll.Offset < DialogScroll.Max;
        }
    }
}