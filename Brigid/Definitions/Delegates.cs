#region
using Brigid.Controls.Components;
using Brigid.Controls.World.Hud.Panel.Slots;
using Brigid.Data.Models;
using Brigid.ViewModel;
using Chaos.DarkAges.Definitions;
#endregion

namespace Brigid.Definitions;

#region Shared Delegates
/// <summary>
///     A slot at the given 1-based index changed (added, updated, or cleared).
/// </summary>
public delegate void SlotChangedHandler(byte slot);

/// <summary>
///     Fired when all entries in the owning collection are cleared (e.g. on logout or full server re-entry).
/// </summary>
public delegate void ClearedHandler();

/// <summary>
///     Fired when the owning state object is updated by the server.
/// </summary>
public delegate void ChangedHandler();
#endregion

#region ChaosGame Delegates
/// <summary>
///     Fired when all metadata files are up to date with the server.
/// </summary>
public delegate void MetaDataSyncCompleteHandler();
#endregion

#region ViewModel Delegates
/// <summary>
///     An equipment slot was equipped or updated.
/// </summary>
public delegate void EquipmentSlotChangedHandler(EquipmentSlot equipmentSlot);

/// <summary>
///     An equipment slot was unequipped.
/// </summary>
public delegate void EquipmentSlotClearedHandler(EquipmentSlot equipmentSlot);

/// <summary>
///     The player's gold amount changed.
/// </summary>
public delegate void GoldChangedHandler();

/// <summary>
///     A chat message was added (public, whisper, group, guild).
/// </summary>
public delegate void ChatMessageAddedHandler(Chat.ChatMessage message);

/// <summary>
///     An orange bar message was added (system messages, whisper/group/guild echoes).
/// </summary>
public delegate void OrangeBarMessageAddedHandler(Chat.OrangeBarMessage message);

/// <summary>
///     The current NPC dialog changed (shown, updated, or closed).
/// </summary>
public delegate void DialogChangedHandler();

/// <summary>
///     A new NPC menu was displayed.
/// </summary>
public delegate void MenuChangedHandler();

/// <summary>
///     Fired when the board session is closed (all panels should hide).
/// </summary>
public delegate void SessionClosedHandler();

/// <summary>
///     The board post list was shown or updated (new page appended).
/// </summary>
public delegate void PostListChangedHandler();

/// <summary>
///     A single post was displayed for reading.
/// </summary>
public delegate void PostViewedHandler();

/// <summary>
///     A board list was received (multiple boards available).
/// </summary>
public delegate void BoardListReceivedHandler();

/// <summary>
///     A server response message was received (submit/delete/highlight result).
/// </summary>
public delegate void BoardResponseReceivedHandler(string message, bool success);

/// <summary>
///     A new exchange was started.
/// </summary>
public delegate void ExchangeStartedHandler();

/// <summary>
///     An item was added to one side of the exchange.
/// </summary>
public delegate void ExchangeItemAddedHandler(bool isOtherSide, byte index);

/// <summary>
///     Gold was set on one side of the exchange.
/// </summary>
public delegate void ExchangeGoldSetHandler(bool isOtherSide);

/// <summary>
///     The other player accepted the exchange.
/// </summary>
public delegate void ExchangeOtherAcceptedHandler();

/// <summary>
///     The exchange was closed (cancelled or completed). Message is the server-provided result text,
///     or null for local-initiated closes that have no associated message.
/// </summary>
public delegate void ExchangeClosedHandler(string? message);

/// <summary>
///     The server requests a stackable item count from the given inventory slot.
/// </summary>
public delegate void ExchangeAmountRequestedHandler(byte fromSlot);

/// <summary>
///     A group-related interaction was received from the server.
/// </summary>
public delegate void GroupInviteReceivedHandler();

/// <summary>
///     Fires on any user option value change (server response or client toggle).
/// </summary>
public delegate void UserOptionChangedHandler(int setting, bool enabled);
#endregion

#region Controls/Components Delegates
/// <summary>
///     A UI button was clicked.
/// </summary>
public delegate void ClickedHandler();

/// <summary>
///     A UI button was clicked, with the keyboard modifiers active at click time.
/// </summary>
public delegate void ClickedWithModifiersHandler(KeyModifiers modifiers);

/// <summary>
///     A UI button was hovered (mouse enter).
/// </summary>
public delegate void HoveredHandler(UIButton button);

/// <summary>
///     A UI button was unhovered (mouse leave).
/// </summary>
public delegate void UnhoveredHandler(UIButton button);

/// <summary>
///     A UIElement's visibility changed.
/// </summary>
public delegate void VisibilityChangedHandler(bool visible);

/// <summary>
///     A UITextBox gained focus.
/// </summary>
public delegate void TextBoxFocusHandler(UITextBox textBox);

/// <summary>
///     A focus state changed.
/// </summary>
public delegate void FocusChangedHandler(bool focused);
#endregion

#region Controls/LobbyLogin Delegates
/// <summary>
///     A server was selected in the server list.
/// </summary>
public delegate void ServerSelectedHandler(byte serverId);

/// <summary>
///     An OK action was confirmed on a dialog.
/// </summary>
public delegate void OkHandler();

/// <summary>
///     A cancel action was triggered on a dialog.
/// </summary>
public delegate void CancelHandler();
#endregion

#region Controls/Generic Delegates
/// <summary>
///     A popup or panel was closed.
/// </summary>
public delegate void CloseHandler();

/// <summary>
///     A scrollbar value changed.
/// </summary>
public delegate void ScrollValueChangedHandler(int value);

/// <summary>
///     A scroll model's offset changed.
/// </summary>
public delegate void ScrollOffsetChangedHandler(int offset);
#endregion

#region Controls/World/Hud Delegates
/// <summary>
///     A chat message was sent.
/// </summary>
public delegate void MessageSentHandler(string message);

/// <summary>
///     A shout message was sent.
/// </summary>
public delegate void ShoutSentHandler(string message);

/// <summary>
///     A whisper was sent.
/// </summary>
public delegate void WhisperSentHandler(string recipient, string message);

/// <summary>
///     A player name was added to the ignore list.
/// </summary>
public delegate void IgnoreAddedHandler(string name);

/// <summary>
///     A player name was removed from the ignore list.
/// </summary>
public delegate void IgnoreRemovedHandler(string name);

/// <summary>
///     The ignore list was requested.
/// </summary>
public delegate void IgnoreListRequestedHandler();
#endregion

#region Controls/World/Hud/Panel Delegates
/// <summary>
///     A panel slot was clicked.
/// </summary>
public delegate void PanelSlotClickedHandler(byte slot);

/// <summary>
///     A panel slot was dragged outside the panel.
/// </summary>
public delegate void PanelSlotDroppedOutsideHandler(byte slot, int screenX, int screenY);

/// <summary>
///     A panel slot hover started.
/// </summary>
public delegate void PanelSlotHoverEnterHandler(PanelSlot panelSlot);

/// <summary>
///     A panel slot hover ended.
/// </summary>
public delegate void PanelSlotHoverExitHandler();

/// <summary>
///     Two panel slots were swapped.
/// </summary>
public delegate void PanelSlotSwappedHandler(byte fromSlot, byte toSlot);

/// <summary>
///     A stat raise button was clicked.
/// </summary>
public delegate void RaiseStatHandler(Stat stat);
#endregion

#region Controls/World/Hud/Panel/Slots Delegates
/// <summary>
///     An ability slot was right-clicked.
/// </summary>
public delegate void AbilitySlotRightClickHandler(byte slot);

/// <summary>
///     A panel slot was double-clicked.
/// </summary>
public delegate void PanelSlotDoubleClickedHandler(byte slot);

/// <summary>
///     A panel slot drag was started.
/// </summary>
public delegate void PanelSlotDragStartedHandler(PanelSlot panelSlot);
#endregion

#region Controls/World/Popups Delegates
/// <summary>
///     A chant was set. Parameters: slot (1-based), chant lines array, isSpell.
/// </summary>
public delegate void ChantSetHandler(byte slot, string[] lines, bool isSpell);

/// <summary>
///     An amount was confirmed.
/// </summary>
public delegate void AmountConfirmedHandler(uint amount);

/// <summary>
///     A social status was selected.
/// </summary>
public delegate void SocialStatusSelectedHandler(SocialStatus status);

/// <summary>
///     A notepad was saved.
/// </summary>
public delegate void NotepadSavedHandler(byte index, string content);

/// <summary>
///     A group recruit box creation was requested.
/// </summary>
public delegate void CreateGroupBoxHandler(
    string name,
    string note,
    byte minLevel,
    byte maxLevel,
    byte classMax0,
    byte classMax1,
    byte classMax2,
    byte classMax3,
    byte classMax4);

/// <summary>
///     A group box removal was requested.
/// </summary>
public delegate void RemoveGroupBoxHandler();

/// <summary>
///     A request to join a group was sent.
/// </summary>
public delegate void RequestJoinHandler(string name);

/// <summary>
///     A whisper to a player was requested.
/// </summary>
public delegate void WhisperRequestedHandler(string name);

/// <summary>
///     A group kick was requested.
/// </summary>
public delegate void GroupKickHandler(string name);

/// <summary>
///     A group leave was requested.
/// </summary>

/// <summary>
///     A closed event was fired (for overlapping OnClose semantics).
/// </summary>
public delegate void ClosedHandler();
#endregion

#region Controls/World/Popups/Boards Delegates
/// <summary>
///     A post was deleted.
/// </summary>
public delegate void DeletePostHandler(short postId);

/// <summary>
///     A new post was requested.
/// </summary>
public delegate void NewPostHandler();

/// <summary>
///     Navigate to the next item.
/// </summary>
public delegate void NextHandler();

/// <summary>
///     Navigate to the previous item.
/// </summary>
public delegate void PrevHandler();

/// <summary>
///     Navigate up one level.
/// </summary>
public delegate void UpHandler();

/// <summary>
///     A post was highlighted (toggled).
/// </summary>
public delegate void HighlightPostHandler(short postId);

/// <summary>
///     More posts were requested to be loaded.
/// </summary>
public delegate void LoadMorePostsHandler(short startPostId);

/// <summary>
///     A post was selected for viewing.
/// </summary>
public delegate void ViewPostHandler(short postId);

/// <summary>
///     A board was selected for viewing.
/// </summary>
public delegate void ViewBoardHandler(ushort boardId);

/// <summary>
///     An article was sent (subject + body).
/// </summary>
public delegate void ArticleSendHandler(string subject, string body);

/// <summary>
///     A new mail was requested.
/// </summary>
public delegate void NewMailHandler();

/// <summary>
///     A reply to a post was requested.
/// </summary>
public delegate void ReplyPostHandler(short postId);

/// <summary>
///     A quit action was triggered.
/// </summary>
public delegate void QuitHandler();

/// <summary>
///     A mail was sent (recipient, subject, body).
/// </summary>
public delegate void MailSendHandler(string recipient, string subject, string body);
#endregion

#region Controls/World/Popups/Dialog Delegates
/// <summary>
///     A dialog option was selected.
/// </summary>
public delegate void OptionSelectedHandler(int optionIndex);

/// <summary>
///     A protected text entry was submitted (visible text + hidden text).
/// </summary>
public delegate void ProtectedSubmitHandler(string visibleText, string hiddenText);

/// <summary>
///     A text entry was submitted.
/// </summary>
public delegate void TextSubmitHandler(string text);

/// <summary>
///     A menu/merchant item was selected.
/// </summary>
public delegate void ItemSelectedHandler(int itemIndex);

/// <summary>
///     A menu item hover started.
/// </summary>
public delegate void ItemHoverEnterHandler(string itemName);

/// <summary>
///     A menu item hover ended.
/// </summary>
public delegate void ItemHoverExitHandler();

/// <summary>
///     Navigate to the top of a dialog sequence.
/// </summary>
public delegate void TopHandler();

/// <summary>
///     Navigate to the previous dialog step.
/// </summary>
public delegate void PreviousHandler();
#endregion

#region Controls/World/Popups/Options Delegates
/// <summary>
///     An exit request was made.
/// </summary>
public delegate void ExitHandler();

/// <summary>
///     The friends list was requested.
/// </summary>
public delegate void FriendsHandler();

/// <summary>
///     The macro list was requested.
/// </summary>
public delegate void MacroHandler();

/// <summary>
///     The settings panel was requested.
/// </summary>
public delegate void SettingsHandler();

/// <summary>
///     The music volume was changed.
/// </summary>
public delegate void MusicVolumeChangedHandler(int volume);

/// <summary>
///     The sound volume was changed.
/// </summary>
public delegate void SoundVolumeChangedHandler(int volume);
#endregion

#region Controls/World/Popups/Profile Delegates
/// <summary>
///     An ability metadata entry was clicked.
/// </summary>
public delegate void AbilityMetadataClickedHandler(AbilityMetadataEntry entry);

/// <summary>
///     An event metadata entry was clicked.
/// </summary>
public delegate void EventMetadataClickedHandler(EventMetadataEntry entry, EventState state);

/// <summary>
///     A group toggle was requested on the profile.
/// </summary>
public delegate void GroupToggledHandler();

/// <summary>
///     The profile text was clicked for editing.
/// </summary>
public delegate void ProfileTextClickedHandler();

/// <summary>
///     An equipment slot unequip was requested.
/// </summary>
public delegate void UnequipHandler(EquipmentSlot equipmentSlot);

/// <summary>
///     A group invite was requested for a player.
/// </summary>
public delegate void GroupInviteRequestedHandler(string playerName);

/// <summary>
///     A profile text was saved.
/// </summary>
public delegate void ProfileTextSavedHandler(string text);
#endregion