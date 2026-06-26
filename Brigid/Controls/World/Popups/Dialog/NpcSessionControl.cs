#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Rendering.Models;
using Chaos.DarkAges.Definitions;
using Chaos.Networking.Entities.Server;
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
    private readonly MenuShopPanel MenuShop;
    private readonly UIButton? NextButton;

    private readonly UILabel? NpcNameLabel;
    private readonly UIImage? NpcTileImage;
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
    private int ScrollLine;
    public DialogType? CurrentDialogType { get; private set; }
    public MenuType? CurrentMenuType { get; private set; }
    public ushort DialogId { get; private set; }
    public bool IsDialogOpcode { get; private set; }

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
        AddChild(new DialogAlphaGradient());

        //background images (drawn after gradient so they render on top of it)
        CreateImage("MessageDialog"); //nd_talk.spf — bottom dialog bar
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

        //wire container button events
        if (CloseButton is not null)
            CloseButton.Clicked += () =>
            {
                HideAll();
                OnClose?.Invoke();
            };

        if (NextButton is not null)
            NextButton.Clicked += () => OnNext?.Invoke();

        if (PreviousButton is not null)
            PreviousButton.Clicked += () => OnPrevious?.Invoke();

        if (TopButton is not null)
            TopButton.Clicked += () =>
            {
                HideAll();
                OnTop?.Invoke();
            };

        //create sub-panels as children
        DialogOption = new DialogOptionPanel();
        DialogTextEntry = new DialogTextEntryPanel();
        MenuTextEntry = new MenuTextEntryPanel();
        MenuShop = new MenuShopPanel();
        MenuList = new MenuListPanel();
        DialogProtectedTextEntry = new DialogProtectedTextEntryPanel();

        AddChild(DialogOption);
        AddChild(DialogTextEntry);
        AddChild(MenuTextEntry);
        AddChild(MenuShop);
        AddChild(MenuList);
        AddChild(DialogProtectedTextEntry);

        //wire sub-panel events — forward to container events
        DialogOption.OnOptionSelected += index => OnOptionSelected?.Invoke(index);

        DialogOption.OnClose += () =>
        {
            HideAll();
            OnClose?.Invoke();
        };

        DialogTextEntry.OnTextSubmit += text => OnTextSubmit?.Invoke(text);

        DialogTextEntry.OnClose += () =>
        {
            HideAll();
            OnClose?.Invoke();
        };

        MenuTextEntry.OnTextSubmit += text => OnTextSubmit?.Invoke(text);

        MenuTextEntry.OnClose += () =>
        {
            HideAll();
            OnClose?.Invoke();
        };

        MenuShop.OnItemSelected += index => OnMerchantItemSelected?.Invoke(index);
        MenuShop.OnItemHoverEnter += name => OnItemHoverEnter?.Invoke(name);
        MenuShop.OnItemHoverExit += () => OnItemHoverExit?.Invoke();

        MenuShop.OnClose += () =>
        {
            HideAll();
            OnClose?.Invoke();
        };

        MenuList.OnItemSelected += index => OnListItemSelected?.Invoke(index);

        MenuList.OnClose += () =>
        {
            HideAll();
            OnClose?.Invoke();
        };

        DialogProtectedTextEntry.OnProtectedSubmit += (id, pw) => OnProtectedSubmit?.Invoke(id, pw);

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
        if (PortraitTexture is null)
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
    ///     Returns the slot byte for the merchant entry at the given index.
    /// </summary>
    public byte? GetMerchantEntrySlot(int index) => MenuShop.GetEntrySlot(index);

    /// <summary>
    ///     Returns the pursuit ID for the option at the given index in the OptionMenu sub-panel.
    /// </summary>
    public ushort GetOptionPursuitId(int index) => DialogOption.GetOptionPursuitId(index);

    /// <summary>
    ///     Hides the container and all sub-panels.
    /// </summary>
    public void HideAll()
    {
        DisposePortrait();
        HideAllSubPanels();
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
        ScrollLine = 0;
        DialogTextLabel.ScrollOffset = 0;
        DialogTextLabel.Text = string.Empty;
        UpdateScrollButtons();
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
    public event ItemHoverEnterHandler? OnItemHoverEnter;
    public event ItemHoverExitHandler? OnItemHoverExit;
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
        var totalLines = DialogTextLabel.ContentHeight / TextRenderer.CHAR_HEIGHT;
        var innerH = DialogTextLabel.Height - DialogTextLabel.PaddingTop + DialogTextLabel.PaddingBottom;
        var visibleLines = innerH / TextRenderer.CHAR_HEIGHT;
        var maxScroll = Math.Max(0, totalLines - visibleLines);

        ScrollLine = Math.Clamp(ScrollLine + direction, 0, maxScroll);
        DialogTextLabel.ScrollOffset = ScrollLine * TextRenderer.CHAR_HEIGHT;

        UpdateScrollButtons();
    }

    private void SetDialogText(string? text)
    {
        ScrollLine = 0;
        DialogTextLabel.ScrollOffset = 0;
        DialogTextLabel.Text = text ?? string.Empty;
        UpdateScrollButtons();
    }

    private void SetNavigationButtons(bool hasNext, bool hasPrevious)
    {
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
    public void ShowDialog(DisplayDialogArgs args)
    {
        if (args.DialogType is DialogType.CloseDialog)
        {
            HideAll();

            return;
        }

        IsDialogOpcode = true;
        CurrentDialogType = args.DialogType;
        CurrentMenuType = null;
        SourceEntityType = args.EntityType;
        SourceId = args.SourceId;
        PursuitId = args.PursuitId ?? 0;
        DialogId = args.DialogId;
        NpcName = args.Name;
        PortraitSpriteId = args.Sprite;
        PortraitColor = args.Color;
        IllustrationIndex = args.IllustrationIndex;

        HideAllSubPanels();
        SetDialogText(args.Text);
        SetNavigationButtons(args.HasNextButton, args.HasPreviousButton);
        MenuArgs = null;
        SpeakPrompt = null;
        SpeakEpilog = null;

        switch (args.DialogType)
        {
            case DialogType.Normal:
                break;

            case DialogType.DialogMenu:
            case DialogType.CreatureMenu:
                if (args.Options is not null && (args.Options.Count > 0))
                {
                    var options = args.Options
                                      .Select(o => (o, (ushort)0))
                                      .ToList();
                    DialogOption.ShowOptions(options);
                }

                break;

            case DialogType.TextEntry:
            case DialogType.Speak:
                HideNavigationButtons();

                var prompt = args.TextBoxPrompt ?? string.Empty;
                var epilog = string.Empty;

                if (args.DialogType is DialogType.Speak)
                {
                    SpeakPrompt = prompt;
                    SpeakEpilog = epilog;
                }

                DialogTextEntry.ShowTextEntry(prompt, (byte)(args.TextBoxLength ?? 255), epilog);

                break;

            case DialogType.Protected:
                HideNavigationButtons();
                DialogProtectedTextEntry.ShowProtected(args.Text);

                break;

            default:
                return;
        }

        if (NpcNameLabel is not null)
        {
            NpcNameLabel.Text = args.Name;
            NpcNameLabel.ForegroundColor = new Color(0, 255, 0);
        }

        Show();
    }

    /// <summary>
    ///     Shows the container for a DisplayMenu packet (opcode 0x2F).
    /// </summary>
    public void ShowMenu(DisplayMenuArgs args)
    {
        IsDialogOpcode = false;
        CurrentDialogType = null;
        CurrentMenuType = args.MenuType;
        SourceEntityType = args.EntityType;
        SourceId = args.SourceId;
        PursuitId = args.PursuitId;
        DialogId = 0;
        NpcName = args.Name;
        PortraitSpriteId = args.Sprite;
        PortraitColor = args.Color;
        IllustrationIndex = args.IllustrationIndex;

        MenuArgs = args.Args;
        HideAllSubPanels();
        HideNavigationButtons();
        SetDialogText(args.Text);

        switch (args.MenuType)
        {
            case MenuType.Menu:
            case MenuType.MenuWithArgs:
                if (args.Options is not null && (args.Options.Count > 0))
                {
                    var options = args.Options
                                      .Select(o => (o.Text, o.Pursuit))
                                      .ToList();
                    DialogOption.ShowOptions(options);
                }

                break;

            case MenuType.TextEntry:
                MenuTextEntry.ShowTextEntry(null);

                break;

            case MenuType.TextEntryWithArgs:
                MenuTextEntry.ShowTextEntry(args.Args);

                break;

            case MenuType.ShowItems:
                MenuShop.ShowMerchant(args);

                break;

            case MenuType.ShowPlayerItems:
            case MenuType.ShowSkills:
            case MenuType.ShowSpells:
            case MenuType.ShowPlayerSkills:
            case MenuType.ShowPlayerSpells:
                MenuList.ShowList(args);

                break;

            default:
                return;
        }

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
            NpcNameLabel.Text = args.Name;
            NpcNameLabel.ForegroundColor = LegendColors.Lime;
        }

        Show();
    }

    public override void Update(GameTime gameTime)
    {
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
            if (DialogOption is { Visible: true, OptionCount: > 0 })
            {
                OnOptionSelected?.Invoke(0);
                e.Handled = true;
            } else if (NextButton is { Visible: true, Enabled: true })
            {
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
        var totalLines = DialogTextLabel.ContentHeight / TextRenderer.CHAR_HEIGHT;
        var innerH = DialogTextLabel.Height - DialogTextLabel.PaddingTop + DialogTextLabel.PaddingBottom;
        var visibleLines = innerH / TextRenderer.CHAR_HEIGHT;

        if (totalLines <= visibleLines)
        {
            ScrollUpButton?.Visible = false;

            ScrollDownButton?.Visible = false;

            return;
        }

        var maxScroll = totalLines - visibleLines;

        if (ScrollUpButton is not null)
        {
            ScrollUpButton.Visible = ScrollLine > 0;
            ScrollUpButton.Enabled = ScrollLine > 0;
        }

        if (ScrollDownButton is not null)
        {
            ScrollDownButton.Visible = ScrollLine < maxScroll;
            ScrollDownButton.Enabled = ScrollLine < maxScroll;
        }
    }
}