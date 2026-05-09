#region
using Chaos.Client.Collections;
using Chaos.Client.Controls.Components;
using Chaos.Client.Controls.World.Hud;
using Chaos.Client.Controls.World.Hud.Panel;
using Chaos.Client.Controls.World.Hud.Panel.Slots;
using Chaos.Client.Controls.World.Popups;
using Chaos.Client.Data;
using Chaos.Client.Data.Models;
using Chaos.Client.Extensions;
using Chaos.Client.Models;
using Chaos.Client.Systems;
using Chaos.Client.ViewModel;
using Chaos.DarkAges.Definitions;
using Chaos.Geometry.Abstractions.Definitions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Pathfinder = Chaos.Client.Systems.Pathfinder;
#endregion

namespace Chaos.Client.Screens;

public sealed partial class WorldScreen
{
    #region UI Event Handlers
    //--- inventory ---

    private void HandleInventorySlotClicked(byte slot)
    {
        Game.Connection.UseItem(slot);
        HoveredInventorySlot = null;
        ItemTooltip.Hide();
    }

    private void HandleInventoryHoverEnter(PanelSlot slot)
    {
        HoveredInventorySlot = slot;

        ItemTooltip.Show(
            slot.SlotName ?? string.Empty,
            slot.CurrentDurability,
            slot.MaxDurability,
            InputBuffer.MouseX,
            InputBuffer.MouseY);
    }

    private void HandleInventoryHoverExit()
    {
        HoveredInventorySlot = null;
        ItemTooltip.Hide();
    }

    /// <summary>
    ///     Returns true if the point is over any visible popup window, preventing drops from passing through.
    /// </summary>
    private bool IsOverVisiblePopup(int screenX, int screenY)
    {
        if (Root is null)
            return false;

        foreach (var child in Root.Children)
        {
            if (child is not UIPanel { Visible: true, IsPassThrough: false } panel)
                continue;

            if ((panel == SmallHud) || (panel == LargeHud))
                continue;

            if (panel.ContainsPoint(screenX, screenY))
                return true;
        }

        return false;
    }

    private void HandleInventoryDropInViewport(byte slot, int mouseX, int mouseY)
    {
        //dropped onto the exchange window — add item to exchange
        if ((slot != 0) && Exchange.Visible && Exchange.ContainsPoint(mouseX, mouseY))
        {
            Game.Connection.SendExchangeInteraction(ExchangeRequestType.AddItem, Exchange.OtherUserId, slot);

            return;
        }

        //dropped onto an equipment slot — equip the item
        if ((slot != 0) && StatusBook.Visible && StatusBook.ContainsEquipmentSlotPoint(mouseX, mouseY))
        {
            Game.Connection.UseItem(slot);

            return;
        }

        //block drops that land on any visible popup window
        if (IsOverVisiblePopup(mouseX, mouseY))
            return;

        var viewport = WorldHud.ViewportBounds;

        //only drop if released within the world viewport
        if ((mouseX < viewport.X)
            || (mouseX >= (viewport.X + viewport.Width))
            || (mouseY < viewport.Y)
            || (mouseY >= (viewport.Y + viewport.Height)))
            return;

        if (MapFile is null)
            return;

        //check if dropped on an entity (give item/gold to npc/player) — skip self (drop on ground instead)
        (var tileX, var tileY) = ScreenToTile(mouseX, mouseY);
        var entity = GetEntityAtScreen(mouseX, mouseY);

        var droppedOnEntity = entity?.Type is ClientEntityType.Aisling or ClientEntityType.Creature
                              && (entity.Id != Game.Connection.AislingId);

        //gold bag (slot 0) — show the gold amount popup
        if (slot == 0)
        {
            GoldDrop.CenterVerticallyIn(viewport);
            GoldDrop.ShowForTarget(droppedOnEntity ? entity!.Id : null, tileX, tileY);

            return;
        }

        if (droppedOnEntity)
        {
            var itemSlot = WorldState.Inventory.GetSlot(slot);
            Game.Connection.DropItemOnCreature(slot, entity!.Id, itemSlot.Stackable ? (byte)0 : (byte)1);

            return;
        }

        //stackable items with more than one — prompt for count before dropping
        var invSlot = WorldState.Inventory.GetSlot(slot);

        if (invSlot.Stackable && (invSlot.Count > 1))
        {
            var capturedSlot = slot;
            var capturedX = tileX;
            var capturedY = tileY;

            WorldHud.ChatInput.ShowPrompt(
                $"Number of items to drop [ 0 - {(int)invSlot.Count} ]: ",
                12,
                text =>
                {
                    if (int.TryParse(text, out var count) && (count > 0))
                        Game.Connection.DropItem(capturedSlot, capturedX, capturedY, count);
                });

            return;
        }

        Game.Connection.DropItem(slot, tileX, tileY);
    }

    //--- skills / spells ---

    private void HandleSkillSlotClicked(byte slot)
    {
        var skillSlot = WorldHud.SkillBook.GetSkillSlot(slot)
                        ?? WorldHud.SkillBookAlt.GetSkillSlot(slot)
                        ?? WorldHud.Tools.WorldSkills.GetSkillSlot(slot);

        if (skillSlot is not null && (skillSlot.CooldownPercent > 0))
            return;

        //send chant line if one is set for this skill
        if (skillSlot is not null && !string.IsNullOrEmpty(skillSlot.Chant))
            Game.Connection.SendChant(skillSlot.Chant);

        Game.Connection.UseSkill(slot);
    }

    private void HandleSpellSlotClicked(byte slot)
    {
        //determine which panel the slot came from
        var spellSlot = WorldHud.ActiveTab switch
        {
            HudTab.Spells    => WorldHud.SpellBook.GetSpellSlot(slot),
            HudTab.SpellsAlt => WorldHud.SpellBookAlt.GetSpellSlot(slot),
            HudTab.Tools     => WorldHud.Tools.WorldSpells.GetSpellSlot(slot),
            _                => WorldHud.SpellBook.GetSpellSlot(slot)
                                ?? WorldHud.SpellBookAlt.GetSpellSlot(slot)
                                ?? WorldHud.Tools.WorldSpells.GetSpellSlot(slot)
        };

        if (spellSlot is null || string.IsNullOrEmpty(spellSlot.AbilityName))
            return;

        if (spellSlot.CooldownPercent > 0)
            return;

        //notarget spells cast immediately (no cast mode)
        if (spellSlot.SpellType == SpellType.NoTarget)
        {
            if (spellSlot.CastLines == 0)
                Game.Connection.UseSpell(slot);
            else
            {
                //notarget with lines: begin chant sequence targeting self
                CastingSystem.BeginTargeting(spellSlot);

                var player = WorldState.GetPlayerEntity();

                CastingSystem.SelectTarget(
                    Game.Connection.AislingId,
                    player?.TileX ?? 0,
                    player?.TileY ?? 0,
                    Game.Connection);
            }

            return;
        }

        //enter cast mode — wait for target selection
        CastingSystem.BeginTargeting(spellSlot);
    }

    private void HandleSpellSlotDropped(byte slot, int mouseX, int mouseY)
    {
        if (IsOverVisiblePopup(mouseX, mouseY))
            return;

        var entity = GetEntityAtScreen(mouseX, mouseY);

        if (entity?.Type is not (ClientEntityType.Aisling or ClientEntityType.Creature))
            return;

        HandleSpellSlotClicked(slot);

        if (CastingSystem.IsTargeting)
            CastingSystem.SelectTarget(
                entity.Id,
                entity.TileX,
                entity.TileY,
                Game.Connection);
    }

    //--- hotkeys ---

    private static readonly Keys[] EmoteKeys =
    [
        Keys.D1,
        Keys.D2,
        Keys.D3,
        Keys.D4,
        Keys.D5,
        Keys.D6,
        Keys.D7,
        Keys.D8,
        Keys.D9,
        Keys.D0,
        Keys.OemMinus
    ];

    //ctrl+key emotes: 9-17 then 21-22 (skips 18-20 which don't exist in bodyanimation)
    private static readonly BodyAnimation[] CtrlEmotes =
    [
        BodyAnimation.Smile,
        BodyAnimation.Cry,
        BodyAnimation.Frown,
        BodyAnimation.Wink,
        BodyAnimation.Surprise,
        BodyAnimation.Tongue,
        BodyAnimation.Pleasant,
        BodyAnimation.Snore,
        BodyAnimation.Mouth,
        BodyAnimation.BlowKiss,
        BodyAnimation.Wave
    ];

    //base BodyAnimation value for Ctrl+Alt+<key> emotes (e.g. key 0 -> bodyanim 23)
    private const int CTRL_ALT_EMOTE_BASE = 23;

    //base BodyAnimation value for Alt+<key> emotes (e.g. key 0 -> bodyanim 34)
    private const int ALT_EMOTE_BASE = 34;

    /// <summary>
    ///     Returns true when no mutually-exclusive options panel is currently visible. Used by the F3/F4/F10 shortcuts so
    ///     pressing one hotkey cannot overlap another options popup.
    /// </summary>
    private static bool CanShowOptionsPanel(params UIElement[] others)
    {
        foreach (var other in others)
            if (other.Visible)
                return false;

        return true;
    }

    private bool HandleEmoteHotkey(KeyDownEvent e)
    {
        if (e is { Ctrl: false, Alt: false })
            return false;

        var keyIndex = Array.IndexOf(EmoteKeys, e.Key);

        if (keyIndex < 0)
            return false;

        //past this point the keystroke is unambiguously an emote attempt — consume it
        //regardless of whether the emote actually fires, so it doesn't fall through to
        //HandleSlotHotkey and trigger an item/skill slot use as an unintended fallback.
        e.Handled = true;

        var player = WorldState.GetPlayerEntity();

        //gate emote initiation on the same condition as movement: face emotes lock
        //the body while playing, so it shouldn't be possible to start one mid-walk
        //or while another emote/body anim is already running.
        if (player is null || !player.IsAtRest)
            return true;

        BodyAnimation bodyAnimation;

        if (e is { Ctrl: true, Alt: false })
            bodyAnimation = CtrlEmotes[keyIndex];
        else if (e is { Ctrl: true, Alt: true })
            bodyAnimation = (BodyAnimation)(CTRL_ALT_EMOTE_BASE + keyIndex);
        else
            bodyAnimation = (BodyAnimation)(ALT_EMOTE_BASE + keyIndex);

        Game.Connection.SendEmote(bodyAnimation);

        return true;
    }

    private bool HandleSlotHotkey(KeyDownEvent e)
    {
        var slot = e.Key switch
        {
            Keys.D1       => 1,
            Keys.D2       => 2,
            Keys.D3       => 3,
            Keys.D4       => 4,
            Keys.D5       => 5,
            Keys.D6       => 6,
            Keys.D7       => 7,
            Keys.D8       => 8,
            Keys.D9       => 9,
            Keys.D0       => 10,
            Keys.OemMinus => 11,
            Keys.OemPlus  => 12,
            _             => -1
        };

        if (slot < 0)
            return false;

        var byteSlot = (byte)slot;

        switch (WorldHud.ActiveTab)
        {
            case HudTab.Inventory:
                Game.Connection.UseItem(byteSlot);

                break;

            case HudTab.Skills:
                HandleSkillSlotClicked(byteSlot);

                break;

            case HudTab.SkillsAlt:
                HandleSkillSlotClicked((byte)(byteSlot + 36));

                break;

            case HudTab.Spells:
                HandleSpellSlotClicked(byteSlot);

                break;

            case HudTab.SpellsAlt:
                HandleSpellSlotClicked((byte)(byteSlot + 36));

                break;

            case HudTab.Tools:
                if (slot is >= 1 and <= 6)
                    HandleSkillSlotClicked((byte)(72 + slot));
                else
                    HandleSpellSlotClicked((byte)(66 + slot));

                break;

            case HudTab.Chat:
            case HudTab.MessageHistory:
            {
                var macroText = MacrosList.GetMacroValue(slot - 1);

                if (macroText.Length > 0)
                {
                    WorldHud.ChatInput.Focus($"{WorldHud.PlayerName}: ", Color.White);
                    WorldHud.ChatInput.SetText(macroText, macroText.Length);
                }

                break;
            }

            default:
                return false;
        }

        e.Handled = true;

        return true;
    }

    //--- chant editing ---

    private void WireAbilityRightClicks(PanelBase panel)
    {
        foreach (var slotControl in panel.Slots)
            if (slotControl is AbilitySlotControl ability)
                ability.OnRightClick += s => OpenChantEdit(panel, s);
    }

    private void OpenChantEdit(PanelBase source, byte slot)
    {
        var control = source.GetSlotControl(slot) as AbilitySlotControl;

        if (control is null || string.IsNullOrEmpty(control.AbilityName))
            return;

        var isSpell = control is SpellSlot;

        string[] currentChants;
        int lineCount;

        if (control is SpellSlot spell)
        {
            currentChants = spell.Chants;
            lineCount = spell.CastLines;
        } else if (control is SkillSlot skill)
        {
            currentChants = [skill.Chant];
            lineCount = 1;
        } else
            return;

        ChantEdit.Show(
            slot,
            control.AbilityName,
            control.AbilityLevel ?? string.Empty,
            control.NormalTexture,
            currentChants,
            lineCount,
            isSpell);
    }

    private void HandleChantSet(byte slot, string[] chantLines, bool isSpell)
    {
        if (isSpell)
        {
            foreach (var panel in new[]
                     {
                         WorldHud.SpellBook,
                         WorldHud.SpellBookAlt,
                         WorldHud.Tools.WorldSpells
                     })
            {
                var spellSlot = panel.GetSpellSlot(slot);

                if (spellSlot is null)
                    continue;

                for (var i = 0; i < Math.Min(chantLines.Length, spellSlot.Chants.Length); i++)
                    spellSlot.Chants[i] = chantLines[i];
            }

            SaveSpellChants();
            WorldState.ReloadChants();
        } else
        {
            foreach (var panel in new[]
                     {
                         WorldHud.SkillBook,
                         WorldHud.SkillBookAlt,
                         WorldHud.Tools.WorldSkills
                     })
            {
                var skillSlot = panel.GetSkillSlot(slot);

                skillSlot?.Chant = chantLines.Length > 0 ? chantLines[0] : string.Empty;
            }

            SaveSkillChants();
            WorldState.ReloadChants();
        }
    }

    //--- cache / persistence helpers ---

    private void LoadPlayerFamilyList()
    {
        var family = DataContext.LocalPlayerSettings.LoadFamilyList();
        StatusBook.SetFamilyMembers(family);
        WorldList.SetFamilyNames(family);
    }

    private void SavePlayerFamilyList()
    {
        var family = StatusBook.GetFamilyMembers();

        if (family is not null)
        {
            DataContext.LocalPlayerSettings.SaveFamilyList(family);
            WorldList.SetFamilyNames(family);
        }
    }

    private void LoadPlayerFriendList()
    {
        var names = DataContext.LocalPlayerSettings.LoadFriendList();

        FriendsList.SetFriends(names);
        WorldList.SetFriendNames(names);
    }

    private void SavePlayerFriendList()
    {
        var names = FriendsList.GetFriendNames();
        DataContext.LocalPlayerSettings.SaveFriendList(names);
        WorldList.SetFriendNames(names);
    }

    private void LoadPlayerMacros()
    {
        var macros = DataContext.LocalPlayerSettings.LoadMacros();
        MacrosList.SetMacros(macros);
    }

    private void SaveSkillChants()
    {
        var entries = new List<SkillChantEntry>();

        for (byte i = 1; i <= SkillBook.MAX_SLOTS; i++)
        {
            var slot = WorldHud.SkillBook.GetSkillSlot(i)
                       ?? WorldHud.SkillBookAlt.GetSkillSlot(i)
                       ?? WorldHud.Tools.WorldSkills.GetSkillSlot(i);

            if (slot is null || string.IsNullOrEmpty(slot.AbilityName))
                continue;

            entries.Add(
                new SkillChantEntry
                {
                    Name = slot.AbilityName,
                    Chant = slot.Chant
                });
        }

        DataContext.LocalPlayerSettings.SaveSkillChants(entries);
    }

    private void SaveSpellChants()
    {
        var entries = new List<SpellChantEntry>();

        for (byte i = 1; i <= SpellBook.MAX_SLOTS; i++)
        {
            var slot = WorldHud.SpellBook.GetSpellSlot(i)
                       ?? WorldHud.SpellBookAlt.GetSpellSlot(i)
                       ?? WorldHud.Tools.WorldSpells.GetSpellSlot(i);

            if (slot is null || string.IsNullOrEmpty(slot.AbilityName))
                continue;

            var entry = new SpellChantEntry
            {
                Name = slot.AbilityName
            };
            Array.Copy(slot.Chants, entry.Chants, 10);
            entries.Add(entry);
        }

        DataContext.LocalPlayerSettings.SaveSpellChants(entries);
    }
    #endregion

    #region Root Event Handlers

    /// <summary>
    ///     Handles keyboard input that bubbles up to the root panel (no focused element consumed it).
    ///     Contains all game hotkeys, chat focus, movement, emotes, and slot actions.
    /// </summary>
    private void OnRootKeyDown(KeyDownEvent e)
    {
        //alt+enter — cycle window size
        if ((e.Key == Keys.Enter) && e.Modifiers.HasFlag(KeyModifiers.Alt))
        {
            Game.CycleWindowSize();
            e.Handled = true;

            return;
        }

        //casting mode: escape cancels targeting
        if (CastingSystem.IsTargeting && (e.Key == Keys.Escape))
        {
            CastingSystem.Reset();
            e.Handled = true;

            return;
        }

        //escape with nothing else open — toggle pause menu. popups on the control stack
        //receive the escape key via phase-2 stack dispatch before this handler runs, so
        //we only reach here when the stack is empty (pause menu itself closes via its own
        //OnKeyDown while it's the top of the stack).
        if ((e.Key == Keys.Escape) && (InputDispatcher.Instance?.ControlStackCount == 0))
        {
            PauseMenu.Show();
            e.Handled = true;

            return;
        }

        //enter — toggle chat focus
        if (e.Key == Keys.Enter)
        {
            if (!WorldHud.ChatInput.IsFocused)
                WorldHud.ChatInput.Focus($"{WorldHud.PlayerName}: ", Color.White);

            e.Handled = true;

            return;
        }

        //q/w/e/r toggle group — must be above the stack guard because these panels
        //use the control stack themselves and need to toggle while open
        if (e.Key == Keys.Q)
        {
            ForceCloseOtherTogglePanels(Keys.Q);

            if (PauseMenu.Visible)
            {
                SettingsDialog.Hide();
                MacrosList.Hide();
                FriendsList.Hide();
                PauseMenu.Hide();
            } else
                PauseMenu.Show();

            e.Handled = true;

            return;
        }

        if (e.Key == Keys.W)
        {
            ForceCloseOtherTogglePanels(Keys.W);

            if (IsAnyBoardPanelVisible())
            {
                if (BoardList.Visible)
                    BoardList.SlideClose();
                else
                    WorldState.Board.CloseSession();
            } else
            {
                WorldHud.BulletinButton?.IsSelected = true;

                Game.Connection.SendBoardInteraction(BoardRequestType.BoardList);
            }

            e.Handled = true;

            return;
        }

        if (e.Key == Keys.E)
        {
            ForceCloseOtherTogglePanels(Keys.E);

            if (WorldList.Visible)
                WorldList.SlideClose();
            else
            {
                WorldHud.UsersButton?.IsSelected = true;

                Game.Connection.RequestWorldList();
            }

            e.Handled = true;

            return;
        }

        if (e.Key == Keys.R)
        {
            ForceCloseOtherTogglePanels(Keys.R);
            ToggleSocialStatusPicker();

            e.Handled = true;

            return;
        }

        //stack guard: suppress all game hotkeys when a popup is active
        if (Game.Dispatcher.ControlStackCount > 0)
            return;

        //escape — collapse any expanded HUD panel back to normal size. only consume
        //the key when something was actually collapsed so it stays a no-op otherwise.
        if (e.Key == Keys.Escape)
        {
            if (WorldHud.CollapseExpanded())
                e.Handled = true;

            return;
        }

        //spacebar assail — fires on both initial press and os key-repeat keydowns
        //while held. sits after the stack guard so dialogs/menus block it; sits inside
        //the root handler so any ui element above can mark e.handled first and suppress it.
        //rate-limited to SPACEBAR_INTERVAL_MS since os key-repeat rates vary wildly.
        if (e.Key == Keys.Space)
        {
            var now = Environment.TickCount64;

            if ((now - LastSpacebarMs) >= SPACEBAR_INTERVAL_MS)
            {
                Game.Connection.Spacebar();
                Pathfinding.Clear();
                LastSpacebarMs = now;
            }

            e.Handled = true;

            return;
        }

        if ((e.Key == Keys.T) && TownMapControl.Visible)
        {
            TownMapControl.Hide();
            e.Handled = true;

            return;
        }

        //shout hotkey (shift+1)
        if (e is { Key: Keys.D1, Shift: true })
        {
            WorldHud.ChatInput.Focus($"{WorldHud.PlayerName}! ", Color.Yellow);
            e.Handled = true;

            return;
        }

        //whisper hotkey (shift+")
        if (e is { Key: Keys.OemQuotes, Shift: true })
        {
            WorldHud.ChatInput.FocusWhisper();
            e.Handled = true;

            return;
        }

        //tab panel switching — blocked while dragging the orange bar
        if (!WorldHud.IsOrangeBarDragging)
        {
            HudTab? tab = e.Key switch
            {
                Keys.A => HudTab.Inventory,
                Keys.S => HudTab.Skills,
                Keys.D => HudTab.Spells,
                Keys.F => HudTab.Chat,
                Keys.G => HudTab.Stats,
                Keys.H => HudTab.Tools,
                _      => null
            };

            if (tab is not null)
            {
                WorldHud.HandleTabActivation(tab.Value, e.Shift);
                e.Handled = true;

                return;
            }
        }

        //tab — toggle tab map overlay (suppressed by NoTabMap map flag)
        if (e.Key == Keys.Tab)
        {
            if (!CurrentMapFlags.HasFlag(MapFlags.NoTabMap))
                TabMapVisible = !TabMapVisible;

            e.Handled = true;

            return;
        }

        //pageup/pagedown — tab map zoom
        if (TabMapVisible)
        {
            if (e.Key == Keys.PageUp)
            {
                TabMapRenderer.ZoomIn();
                e.Handled = true;

                return;
            }

            if (e.Key == Keys.PageDown)
            {
                TabMapRenderer.ZoomOut();
                e.Handled = true;

                return;
            }
        }

        //f1 — help merchant (server-side)
        if (e.Key == Keys.F1)
        {
            Game.Connection.ClickEntity(uint.MaxValue);
            e.Handled = true;

            return;
        }

        //f3 — macro menu
        if (e.Key == Keys.F3)
        {
            if (CanShowOptionsPanel(SettingsDialog, FriendsList))
                MacrosList.Show();

            e.Handled = true;

            return;
        }

        //f4 — settings
        if (e.Key == Keys.F4)
        {
            if (CanShowOptionsPanel(MacrosList, FriendsList))
                SettingsDialog.Show();

            e.Handled = true;

            return;
        }

        //f5 — refresh
        if (e.Key == Keys.F5)
        {
            Game.Connection.RequestRefresh();
            e.Handled = true;

            return;
        }

        //f7 — board list
        if (e.Key == Keys.F7)
        {
            Game.Connection.SendBoardInteraction(BoardRequestType.BoardList);
            e.Handled = true;

            return;
        }

        //f8 — unused (group panel moved to y key)

        //f9 — ignore list management (toggle)
        if (e.Key == Keys.F9)
        {
            if (WorldHud.ChatInput.Mode != ChatMode.None)
                WorldHud.ChatInput.Unfocus();
            else
                WorldHud.ChatInput.FocusIgnore();

            e.Handled = true;

            return;
        }

        //f10 — friends list
        if (e.Key == Keys.F10)
        {
            if (CanShowOptionsPanel(MacrosList, SettingsDialog))
                FriendsList.Show();

            e.Handled = true;

            return;
        }

        // — swap hud layout (small <-> large)
        if (e is { Key: Keys.OemQuestion, Shift: false })
        {
            SwapHudLayout();
            e.Handled = true;

            return;
        }

        //` — unequip weapon and shield
        if (e.Key == Keys.OemTilde)
        {
            if (WorldState.Equipment.GetSlot(EquipmentSlot.Weapon) is not null)
                Game.Connection.Unequip(EquipmentSlot.Weapon);

            if (WorldState.Equipment.GetSlot(EquipmentSlot.Shield) is not null)
                Game.Connection.Unequip(EquipmentSlot.Shield);

            e.Handled = true;

            return;
        }

        //j — flash group member highlighting (1000ms, gated while pending or active)
        if ((e.Key == Keys.J) && !GroupHighlightRequested && (GroupHighlightedIds.Count == 0))
        {
            GroupHighlightRequested = true;
            Game.Connection.RequestSelfProfile();
            e.Handled = true;

            return;
        }

        //b — pick up item from under player, or from the tile in front
        if (e.Key == Keys.B)
        {
            TryPickupItem();
            e.Handled = true;

            return;
        }

        //t — town map toggle
        if (e.Key == Keys.T)
        {
            if (TownMapControl.Visible)
                TownMapControl.Hide();
            else
            {
                var player = WorldState.GetPlayerEntity();

                if (player is not null)
                    TownMapControl.Show(CurrentMapId, player.TileX, player.TileY);
            }

            e.Handled = true;

            return;
        }

        //y — group panel (members tab)
        if (e.Key == Keys.Y)
        {
            Game.Connection.RequestSelfProfile();
            GroupPanel.ShowMembers();
            e.Handled = true;

            return;
        }

        //emote hotkeys: ctrl/alt/ctrl+alt + number row
        if (HandleEmoteHotkey(e))
            return;

        //slot hotkeys: 1-9, 0, -, =
        if (HandleSlotHotkey(e))
            return;

        //player movement — arrow keys and zxcv
        Direction? direction = e.Key switch
        {
            Keys.Up    => Direction.Up,
            Keys.Right => Direction.Right,
            Keys.Down  => Direction.Down,
            Keys.Left  => Direction.Left,
            Keys.C     => Direction.Up,
            Keys.V     => Direction.Right,
            Keys.X     => Direction.Down,
            Keys.Z     => Direction.Left,
            _          => null
        };

        if (direction.HasValue)
        {
            Pathfinding.Clear();
            var player = WorldState.GetPlayerEntity();

            if (player is not null)
            {
                if (player.IsAtRest)
                {
                    if (player.Direction != direction.Value)
                    {
                        Game.Connection.Turn(direction.Value);
                        player.Direction = direction.Value;
                    } else
                    {
                        PredictAndWalk(player, direction.Value);
                        QueuedWalkDirection = null;
                    }
                } else if (player.AnimState == EntityAnimState.Walking)
                {
                    var totalDuration = Math.Max(1f, player.AnimFrameCount * player.AnimFrameIntervalMs);
                    var progress = player.AnimElapsedMs / totalDuration;

                    if (progress >= WALK_QUEUE_THRESHOLD)
                        QueuedWalkDirection = direction.Value;
                }
            }

            e.Handled = true;
        }
    }

    /// <summary>
    ///     Handles mouse scroll that bubbles up to the root panel (no child consumed it). Forwards to whichever chat-style
    ///     HUD panel is currently visible so the player can scroll chat/system messages from anywhere on screen.
    /// </summary>
    private void OnRootMouseScroll(MouseScrollEvent e)
    {
        if (WorldHud.ChatDisplay.Visible)
        {
            WorldHud.ChatDisplay.OnMouseScroll(e);

            return;
        }

        if (WorldHud.MessageHistory.Visible)
            WorldHud.MessageHistory.OnMouseScroll(e);
    }

    /// <summary>
    ///     Handles right-mouse-button presses that bubble up to the root panel. Right-click pathfinding
    ///     fires on press (not release) for snappier response — a held right-click begins moving the
    ///     player immediately instead of waiting for the button to come back up.
    /// </summary>
    private void OnRootMouseDown(MouseDownEvent e)
    {
        if (e.Button != MouseButton.Right)
            return;

        //cast mode consumes right-clicks silently so pathfinding doesn't fire under a targeting cursor
        if (CastingSystem.IsTargeting)
            return;

        if (e.Alt)
        {
            HandleAltRightClick(e.ScreenX, e.ScreenY);
            e.Handled = true;

            return;
        }

        if (e.Shift)
        {
            HandleShiftRightClick(e.ScreenX, e.ScreenY);
            e.Handled = true;

            return;
        }

        //cache the hovered entity for the upcoming doubleclick — pathfinding triggered by this press will start
        //moving the player on the next update, which shifts the camera and makes the second click's ScreenToTile
        //resolve to a different world tile than the entity actually occupies
        var currentTick = Environment.TickCount;

        if ((currentTick - PendingDoubleClickTick) > DOUBLE_CLICK_CACHE_WINDOW_MS)
            PendingDoubleClickEntityId = null;

        var hoverEntity = GetEntityAtScreen(e.ScreenX, e.ScreenY);

        //exclude self — the player's own sprite has a hitbox, and a rapid right-click on the tile the
        //player is walking off of overlaps that hitbox, which would cache the player as a double-click
        //target and kick off a self-follow loop in OnRootDoubleClick
        if (hoverEntity?.Type is ClientEntityType.Aisling or ClientEntityType.Creature
            && (hoverEntity.Id != Game.Connection.AislingId))
        {
            PendingDoubleClickEntityId = hoverEntity.Id;
            PendingDoubleClickTick = currentTick;
        }

        //ctrl is a UI modifier (ctrl+left-click opens the aisling context menu) — suppress single-press
        //pathfinding, but prime the same-tile tracker so a right-doubleclick still resolves to follow.
        if (e.Ctrl)
        {
            if (MapFile is not null)
            {
                (var tileX, var tileY) = ScreenToTile(e.ScreenX, e.ScreenY);
                tileX = Math.Clamp(tileX, 0, MapFile.Width - 1);
                tileY = Math.Clamp(tileY, 0, MapFile.Height - 1);
                RightClickTracker.Click(tileX, tileY);
            }
        } else
            HandleWorldRightClick(e.ScreenX, e.ScreenY);

        e.Handled = true;
    }

    /// <summary>
    ///     Handles mouse clicks that bubble up to the root panel (no child element consumed them).
    ///     Contains cast-mode target selection, Ctrl/Alt-click, and left-click world interaction.
    ///     Right-click pathfinding is handled in OnRootMouseDown for faster response.
    /// </summary>
    private void OnRootClick(ClickEvent e)
    {
        if (e.Button != MouseButton.Left)
            return;

        //exchange gold-click coordination — clicking the money label opens the gold amount popup
        if (Exchange.Visible && Exchange.IsMyMoneyClicked(e.ScreenX, e.ScreenY))
        {
            GoldDrop.ShowForTarget(Exchange.OtherUserId, 0, 0);
            e.Handled = true;

            return;
        }

        //cast mode — target selection or cancel
        if (CastingSystem.IsTargeting)
        {
            var hoverEntity = GetEntityAtScreen(e.ScreenX, e.ScreenY);

            if (hoverEntity?.Type is ClientEntityType.Aisling or ClientEntityType.Creature)
                CastingSystem.SelectTarget(
                    hoverEntity.Id,
                    hoverEntity.TileX,
                    hoverEntity.TileY,
                    Game.Connection);
            else
                CastingSystem.CancelTargeting();

            e.Handled = true;

            return;
        }

        //ctrl+click — context menu on aisling entities
        if (e.Ctrl)
        {
            HandleCtrlClick(e.ScreenX, e.ScreenY);
            e.Handled = true;

            return;
        }

        //alt+click on self — open self profile
        if (e.Alt)
        {
            var hoverEntity = GetEntityAtScreen(e.ScreenX, e.ScreenY);

            if (hoverEntity is not null && (hoverEntity.Id == Game.Connection.AislingId))
            {
                SelfProfileRequested = true;
                Game.Connection.RequestSelfProfile();
            } else
                HandleWorldClick(e.ScreenX, e.ScreenY);

            e.Handled = true;

            return;
        }

        HandleWorldClick(e.ScreenX, e.ScreenY);
        e.Handled = true;
    }

    /// <summary>
    ///     Handles double-click events that bubble up to the root panel.
    ///     Left double-click: interact with entities (pickup ground items, click NPC/aisling).
    ///     Right double-click: follow and assail entity.
    ///     Uses TileClickTracker same-tile guard since the dispatcher only checks same-element (Root),
    ///     not same-tile.
    /// </summary>
    private void OnRootDoubleClick(DoubleClickEvent e)
    {
        if (MapFile is null)
            return;

        var viewport = WorldHud.ViewportBounds;

        if ((e.ScreenX < viewport.X)
            || (e.ScreenX >= (viewport.X + viewport.Width))
            || (e.ScreenY < viewport.Y)
            || (e.ScreenY >= (viewport.Y + viewport.Height)))
            return;

        (var tileX, var tileY) = ScreenToTile(e.ScreenX, e.ScreenY);

        if (e.Button == MouseButton.Left)
        {
            var sameTile = LeftClickTracker.Click(tileX, tileY);

            if (!sameTile)
                return;

            //shift+doubleclick bypasses hitboxes and only picks up ground items
            if (e.Shift)
            {
                var groundItem = WorldState.GetGroundItemAt(tileX, tileY);

                if (groundItem is not null)
                {
                    var firstEmptySlot = WorldState.Inventory.GetFirstEmptySlot();
                    Game.Connection.PickupItem(groundItem.TileX, groundItem.TileY, firstEmptySlot);
                }
            } else
            {
                var entity = GetEntityAtScreen(e.ScreenX, e.ScreenY);

                if (entity is not null && !entity.IsHidden)
                {
                    if (entity.Type == ClientEntityType.GroundItem)
                    {
                        var firstEmptySlot = WorldState.Inventory.GetFirstEmptySlot();
                        Game.Connection.PickupItem(entity.TileX, entity.TileY, firstEmptySlot);
                    } else if ((entity.Type == ClientEntityType.Aisling) && (entity.Id == Game.Connection.AislingId))
                    {
                        //double-clicking self opens the local self-profile (paperdoll / equipment tab), same as alt+click
                        SelfProfileRequested = true;
                        SelfProfileRequestedTab = StatusBookTab.Equipment;
                        Game.Connection.RequestSelfProfile();
                    } else if ((entity.Type != ClientEntityType.Aisling) || ClientSettings.EnableProfileClick)
                        Game.Connection.ClickEntity(entity.Id);
                }
            }

            e.Handled = true;
        } else if (e.Button == MouseButton.Right)
        {
            if (MapPathfinder is null)
                return;

            var player = WorldState.GetPlayerEntity();

            if (player is null)
                return;

            tileX = Math.Clamp(tileX, 0, MapFile.Width - 1);
            tileY = Math.Clamp(tileY, 0, MapFile.Height - 1);

            //tracker still updates so any consumers relying on the last-clicked tile stay accurate
            var sameTile = RightClickTracker.Click(tileX, tileY);

            //prefer the entity captured on the first single right-click — pathfinding started by that click will have
            //moved the player by now, shifting the camera and making ScreenToTile resolve to a different world tile
            WorldEntity? entity = null;

            if (PendingDoubleClickEntityId.HasValue
                && ((Environment.TickCount - PendingDoubleClickTick) <= DOUBLE_CLICK_CACHE_WINDOW_MS))
                entity = WorldState.GetEntity(PendingDoubleClickEntityId.Value);

            //fallback to the legacy tile-based lookup only when the cache miss AND the tiles line up
            if (entity is null)
            {
                if (!sameTile)
                {
                    PendingDoubleClickEntityId = null;

                    return;
                }

                entity = WorldState.GetEntityAt(tileX, tileY);
            }

            //reject self — following yourself produces a re-pathfinding loop that walks into walls or oscillates
            //reject hidden aislings — they have a hitbox for spell targeting but should not be followable
            if (entity?.Type is ClientEntityType.Aisling or ClientEntityType.Creature
                && (entity.Id != Game.Connection.AislingId)
                && !entity.IsHidden)
            {
                Pathfinding.SetEntityTarget(entity.Id);
                PathfindToEntity(player, entity);
            }

            PendingDoubleClickEntityId = null;
            e.Handled = true;
        }
    }

    /// <summary>
    ///     Tracks the cursor position during a drag so the ghost icon follows the mouse.
    ///     Fires on every DragMove that bubbles to root (i.e. any DragMove not consumed by a child).
    /// </summary>
    private void OnRootDragMove(DragMoveEvent e)
    {
        var dragging = GetDraggingPanel();

        dragging?.UpdateDragPosition(e.ScreenX, e.ScreenY);
    }

    /// <summary>
    ///     Handles drag-drop events that bubble up to the root panel.
    ///     If the drop was not consumed by a PanelSlot (slot swap), it means the user dropped
    ///     into the world viewport or onto a non-slot UI element — fire OnSlotDroppedOutside.
    /// </summary>
    private void OnRootDragDrop(DragDropEvent e)
    {
        if (e.Payload is not SlotDragPayload payload)
            return;

        if (payload.Source.Parent is not PanelBase { IsDragging: true } panel)
            return;

        panel.CompleteDragOutside(e.ScreenX, e.ScreenY);
        e.Handled = true;
    }

    #endregion

    #region Click Handling
    /// <summary>
    ///     Converts screen mouse coordinates to tile coordinates, accounting for the HUD viewport offset. The world is
    ///     rendered with a translation matrix for the viewport origin, so mouse coords must be adjusted to match.
    /// </summary>
    private WorldEntity? GetEntityAtScreen(int mouseX, int mouseY)
    {
        if (MapFile is null)
            return null;

        //hitbox rects are stored in viewport-relative coords (the world spriteBatch applies a
        //viewport-origin translation at draw time), so mouse coords must be rebased to match.
        var viewport = WorldHud.ViewportBounds;
        var viewportMouseX = mouseX - viewport.X;
        var viewportMouseY = mouseY - viewport.Y;

        //iterate hitboxes back-to-front (last drawn = closest to camera = highest priority)
        for (var i = EntityHitBoxes.Count - 1; i >= 0; i--)
        {
            var hitbox = EntityHitBoxes[i];

            if (hitbox.ScreenRect.Contains(viewportMouseX, viewportMouseY))
                return WorldState.GetEntity(hitbox.EntityId);
        }

        //fallback: tile-based lookup for ground items
        (var tileX, var tileY) = ScreenToTile(mouseX, mouseY);

        return WorldState.GetGroundItemAt(tileX, tileY);
    }

    private (int TileX, int TileY) ScreenToTile(int mouseX, int mouseY)
    {
        var viewport = WorldHud.ViewportBounds;
        var worldPos = Camera.ScreenToWorld(new Vector2(mouseX - viewport.X, mouseY - viewport.Y));
        var tile = Camera.WorldToTile(worldPos.X, worldPos.Y, MapFile!.Height);

        return (tile.X, tile.Y);
    }

    private void TryPickupItem()
    {
        var player = WorldState.GetPlayerEntity();

        if (player is null)
            return;

        var slot = WorldState.Inventory.GetFirstEmptySlot();

        if (slot == 0)
            return;

        //first try the player's own tile
        if (WorldState.HasGroundItemAt(player.TileX, player.TileY))
        {
            Game.Connection.PickupItem(player.TileX, player.TileY, slot);

            return;
        }

        //then try the tile in front (direction the player is facing)
        (var dx, var dy) = player.Direction.ToTileOffset();
        var frontX = player.TileX + dx;
        var frontY = player.TileY + dy;

        if (WorldState.HasGroundItemAt(frontX, frontY))
            Game.Connection.PickupItem(frontX, frontY, slot);
    }

    private void HandleWorldClick(int mouseX, int mouseY)
    {
        if (MapFile is null)
            return;

        var viewport = WorldHud.ViewportBounds;

        if ((mouseX < viewport.X)
            || (mouseX >= (viewport.X + viewport.Width))
            || (mouseY < viewport.Y)
            || (mouseY >= (viewport.Y + viewport.Height)))
            return;

        (var tileX, var tileY) = ScreenToTile(mouseX, mouseY);

        //track tile for same-tile guard used by onrootdoubleclick
        LeftClickTracker.Click(tileX, tileY);

        //check group box text overlays first — they sit above entity hitboxes.
        //rects are viewport-relative, rebase mouse coords to match.
        var groupBoxViewport = WorldHud.ViewportBounds;
        var groupBoxHit = Overlays.GetGroupBoxAtScreen(mouseX - groupBoxViewport.X, mouseY - groupBoxViewport.Y);

        if (groupBoxHit.HasValue)
        {
            (_, var entityName) = groupBoxHit.Value;

            Game.Connection.SendGroupInvite(ClientGroupSwitch.ViewGroupBox, entityName);

            return;
        }

        //single click: check for entity at hitbox first, then tile interaction
        var entity = GetEntityAtScreen(mouseX, mouseY);

        if (entity?.Type is ClientEntityType.Creature)
            Game.Connection.ClickEntity(entity.Id);
        else if (TileHasForeground(tileX, tileY))
            Game.Connection.ClickTile(tileX, tileY);
    }

    private void HandleCtrlClick(int mouseX, int mouseY)
    {
        if (MapFile is null)
            return;

        var viewport = WorldHud.ViewportBounds;

        if ((mouseX < viewport.X)
            || (mouseX >= (viewport.X + viewport.Width))
            || (mouseY < viewport.Y)
            || (mouseY >= (viewport.Y + viewport.Height)))
            return;

        var entity = GetEntityAtScreen(mouseX, mouseY);

        if (entity is null)
            return;

        if ((entity.Type == ClientEntityType.Aisling) && (entity.Id != Game.Connection.AislingId))
        {
            var name = entity.Name;
            var id = entity.Id;

            AislingContext.Show(
                mouseX,
                mouseY,
                name,
                () => Game.Connection.ClickEntity(id),
                () => Game.Connection.SendGroupInvite(ClientGroupSwitch.TryInvite, name),
                () => WorldHud.ChatInput.Focus($"-> {name}: ", TextColors.Whisper));
        }
    }

    private void HandleWorldRightClick(int mouseX, int mouseY)
    {
        if (MapFile is null || MapPathfinder is null)
            return;

        var viewport = WorldHud.ViewportBounds;

        if ((mouseX < viewport.X)
            || (mouseX >= (viewport.X + viewport.Width))
            || (mouseY < viewport.Y)
            || (mouseY >= (viewport.Y + viewport.Height)))
            return;

        var player = WorldState.GetPlayerEntity();

        if (player is null)
            return;

        (var tileX, var tileY) = ScreenToTile(mouseX, mouseY);

        //clamp to map bounds
        tileX = Math.Clamp(tileX, 0, MapFile.Width - 1);
        tileY = Math.Clamp(tileY, 0, MapFile.Height - 1);

        //track tile for same-tile guard used by onrootdoubleclick
        RightClickTracker.Click(tileX, tileY);

        //don't pathfind to current position
        if ((tileX == player.TileX) && (tileY == player.TileY))
        {
            Pathfinding.Clear();

            return;
        }

        //reject right-clicks onto walls so we don't auto-walk into them. open doors pass this filter because
        //IsTileWallBlocked consults DoorTable. gms walk through walls so skip the filter for them.
        if (!IsGameMaster && IsTileWallBlocked(tileX, tileY))
            return;

        //single right-click — pathfind to ground tile
        Pathfinding.TargetEntityId = null;
        PathfindToTile(player, tileX, tileY);
    }

    //alt+right-click door context radius — tile-diamond center must be within this many screen pixels
    //of the cursor to appear in the menu. DoorContextMenu caps entries at MAX_ENTRIES (3) after sort.
    private const int DOOR_CONTEXT_RADIUS_PX = 40;
    private const int DOOR_CONTEXT_RADIUS_SQ = DOOR_CONTEXT_RADIUS_PX * DOOR_CONTEXT_RADIUS_PX;

    //5x5 tile scan (±2) covers a 40px radius at default zoom with margin; zoom 2x would still be safe
    //because 40px / (HALF_TILE_HEIGHT * 2) ≈ 1.4 tiles max.
    private const int DOOR_SCAN_RADIUS_TILES = 2;

    /// <summary>
    ///     Alt+right-click: scan a 5x5 tile region around the cursor for doors whose tile-diamond center is within
    ///     <see cref="DOOR_CONTEXT_RADIUS_PX" /> screen pixels, and if any are found show <see cref="DoorContext" />
    ///     listing them. Each entry's callback issues a normal ClickTile packet — the server toggles the door and
    ///     echoes a 0x32 Door update back, exactly as if the player had left-clicked the door tile directly.
    /// </summary>
    private void HandleAltRightClick(int mouseX, int mouseY)
    {
        if (MapFile is null)
            return;

        var viewport = WorldHud.ViewportBounds;

        if ((mouseX < viewport.X)
            || (mouseX >= (viewport.X + viewport.Width))
            || (mouseY < viewport.Y)
            || (mouseY >= (viewport.Y + viewport.Height)))
            return;

        var entries = FindNearbyDoors(mouseX, mouseY);

        if (entries.Count == 0)
            return;

        DoorContext.Show(mouseX, mouseY, entries);
    }

    private List<(string Label, Action Callback)> FindNearbyDoors(int mouseX, int mouseY)
    {
        var results = new List<(int DistanceSq, string Label, Action Callback)>();

        if (MapFile is null)
            return [];

        (var cursorTileX, var cursorTileY) = ScreenToTile(mouseX, mouseY);
        var viewport = WorldHud.ViewportBounds;

        for (var dy = -DOOR_SCAN_RADIUS_TILES; dy <= DOOR_SCAN_RADIUS_TILES; dy++)
            for (var dx = -DOOR_SCAN_RADIUS_TILES; dx <= DOOR_SCAN_RADIUS_TILES; dx++)
            {
                var tx = cursorTileX + dx;
                var ty = cursorTileY + dy;

                if ((tx < 0) || (ty < 0) || (tx >= MapFile.Width) || (ty >= MapFile.Height))
                    continue;

                var tile = MapFile.Tiles[tx, ty];
                var lfg = tile.LeftForeground;
                var rfg = tile.RightForeground;

                //DoorTable flags any tile whose foreground is a known door sprite — either side of a pair.
                //Used only to decide whether THIS tile is a door; the Open/Close label comes from the
                //server-authoritative KnownDoorClosedState cache below, because DoorTable has some pairs
                //with their (closed, open) columns reversed relative to reality.
                var isDoorTile = DoorTable.GetOpenTileId(lfg).HasValue
                                 || DoorTable.GetOpenTileId(rfg).HasValue
                                 || DoorTable.GetClosedTileId(lfg).HasValue
                                 || DoorTable.GetClosedTileId(rfg).HasValue;

                if (!isDoorTile)
                    continue;

                //tile image is 56x27 with diamond centered at (HALF_TILE_WIDTH, HALF_TILE_HEIGHT) relative to top-left
                var tileWorld = Camera.TileToWorld(tx, ty, MapFile.Height)
                                + new Vector2(DaLibConstants.HALF_TILE_WIDTH, DaLibConstants.HALF_TILE_HEIGHT);
                var tileScreen = Camera.WorldToScreen(tileWorld);
                var screenX = tileScreen.X + viewport.X;
                var screenY = tileScreen.Y + viewport.Y;

                var dpx = mouseX - screenX;
                var dpy = mouseY - screenY;
                var distSq = (int)((dpx * dpx) + (dpy * dpy));

                if (distSq > DOOR_CONTEXT_RADIUS_SQ)
                    continue;

                //prefer the server-authoritative state; fall back to the table's best guess only if the
                //server hasn't told us about this door yet (cache gets populated on map-load door blast).
                string label;

                if (KnownDoorClosedState.TryGetValue((tx, ty), out var knownClosed))
                    label = knownClosed ? "Open Door" : "Close Door";
                else
                {
                    var isClosedByTable = DoorTable.GetOpenTileId(lfg).HasValue || DoorTable.GetOpenTileId(rfg).HasValue;
                    label = isClosedByTable ? "Open Door" : "Close Door";
                }

                var doorX = tx;
                var doorY = ty;
                Action callback = () => Game.Connection.ClickTile(doorX, doorY);

                results.Add((distSq, label, callback));
            }

        results.Sort(static (a, b) => a.DistanceSq.CompareTo(b.DistanceSq));

        var limit = Math.Min(results.Count, DoorContextMenu.MAX_ENTRIES);
        var final = new List<(string Label, Action Callback)>(limit);

        for (var i = 0; i < limit; i++)
            final.Add((results[i].Label, results[i].Callback));

        return final;
    }

    /// <summary>
    ///     Shift+right-click: cancel pathfinding/auto-assailing, and if idle, turn toward the clicked tile.
    /// </summary>
    private void HandleShiftRightClick(int mouseX, int mouseY)
    {
        Pathfinding.Clear();

        if (MapFile is null)
            return;

        var player = WorldState.GetPlayerEntity();

        if (player is null || !player.IsAtRest)
            return;

        var viewport = WorldHud.ViewportBounds;

        if ((mouseX < viewport.X)
            || (mouseX >= (viewport.X + viewport.Width))
            || (mouseY < viewport.Y)
            || (mouseY >= (viewport.Y + viewport.Height)))
            return;

        (var tileX, var tileY) = ScreenToTile(mouseX, mouseY);

        tileX = Math.Clamp(tileX, 0, MapFile.Width - 1);
        tileY = Math.Clamp(tileY, 0, MapFile.Height - 1);

        if ((tileX == player.TileX) && (tileY == player.TileY))
            return;

        var dx = tileX - player.TileX;
        var dy = tileY - player.TileY;

        var direction = Math.Abs(dx) >= Math.Abs(dy)
            ? dx > 0 ? Direction.Right : Direction.Left
            : dy > 0 ? Direction.Down : Direction.Up;

        if (player.Direction != direction)
        {
            Game.Connection.Turn(direction);
            player.Direction = direction;
        }
    }

    private void PathfindToTile(WorldEntity player, int tileX, int tileY)
    {
        if (MapPathfinder is null || MapFile is null)
            return;

        Pathfinding.Path = Pathfinder.FindPathToTile(
            MapPathfinder,
            player.TileX,
            player.TileY,
            tileX,
            tileY,
            MapFile.Width,
            MapFile.Height,
            GetPathfindingBlockedPoints(),
            IsGameMaster);
    }

    private void PathfindToEntity(WorldEntity player, WorldEntity target)
    {
        if (MapPathfinder is null || MapFile is null)
            return;

        var path = Pathfinder.FindPathToEntity(
            MapPathfinder,
            player.TileX,
            player.TileY,
            target.TileX,
            target.TileY,
            MapFile.Width,
            MapFile.Height,
            GetPathfindingBlockedPoints(),
            IsGameMaster,
            IsGameMaster ? null : IsTilePassable,
            out var alreadyAdjacent);

        //already adjacent: no path to walk, but keep TargetEntityId so the Update loop's auto-follow
        //branch turns and assails next tick. Pathfinding.Clear() here would wipe the target entity
        //that OnRootDoubleClick just set, breaking double-right-click follow on neighbors.
        if (alreadyAdjacent)
            Pathfinding.Path = null;
        else
            Pathfinding.Path = path;
    }
    #endregion
}
