#region
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using Brigid.Collections;
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Controls.LobbyLogin;
using Brigid.Data;
using Brigid.Extensions;
using Brigid.Networking;
using Brigid.Networking.Definitions;
using Brigid.Systems;
using Chaos.DarkAges.Definitions;
using DALib.Cryptography;
using DALib.Networking.Packets.Server;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
//disambiguates from Brigid.Systems.ServerEntry (the launcher's server-list model)
using ServerEntry = DALib.Networking.Packets.Server.ServerEntry;
#endregion

namespace Brigid.Screens;

public sealed class LobbyLoginScreen : IScreen
{
    private readonly bool ReturningFromWorld;
    private bool AwaitingCharFinalize;

    private uint? CachedNoticeCheckSum;
    private bool ChangingPassword;
    private CharacterCreationControl CharCreateControl = null!;

    //flow state
    private bool Connecting;
    private bool CreatingCharacter;
    private LoginNoticeControl LoginNoticeControl = null!;

    private ChaosGame Game = null!;
    private string? HomepageUrl;
    private LoginControl LoginControl = null!;
    private PasswordChangeControl PasswordChangeControl = null!;
    private bool PendingWorldSwitch;
    private OkPopupMessageControl LobbyLoginPopupMessage = null!;
    private IList<ServerEntry> ServerList = [];
    private ServerSelectControl ServerSelectControl = null!;

    private UIButton? LastClickedButton;

    //ui panels
    private LobbyLoginControl StartPanel = null!;

    /// <inheritdoc />
    public UIPanel? Root { get; private set; }

    public LobbyLoginScreen(bool returningFromWorld = false) => ReturningFromWorld = returningFromWorld;

    /// <inheritdoc />
    public void Dispose() => Root?.Dispose();

    /// <inheritdoc />
    public void Draw(SpriteBatch spriteBatch, GameTime gameTime)
    {
        //the lobby/login screen has no world layer — its background and controls live in Root and render in the
        //native-resolution UI pass (DrawUi). Nothing is drawn into the world render target here.
    }

    /// <inheritdoc />
    public void DrawNative(SpriteBatch spriteBatch, float scaleX, float scaleY)
    {
        spriteBatch.Begin(samplerState: GlobalSettings.Sampler, transformMatrix: Matrix.CreateScale(scaleX, scaleY, 1f));
        Root!.Draw(spriteBatch);
        DebugOverlay.SnapshotDrawCount();
        spriteBatch.End();
    }

    /// <inheritdoc />
    public void Initialize(ChaosGame game)
    {
        Game = game;

        Game.Connection.StateChanged += OnConnectionStateChanged;
        Game.Connection.OnError += OnConnectionError;
        Game.Connection.OnServerTableReceived += OnServerTableReceived;
        Game.Connection.OnRedirectReceived += OnRedirectReceived;
        Game.Connection.OnLoginMessage += OnLoginMessage;
        Game.Connection.OnLoginNotice += OnLoginNotice;
        Game.Connection.OnLoginControl += OnLoginControlReceived;
    }

    /// <inheritdoc />
    public void LoadContent(GraphicsDevice graphicsDevice)
    {
        StartPanel = new LobbyLoginControl();
        LoginControl = new LoginControl();
        ServerSelectControl = new ServerSelectControl();
        LoginNoticeControl = new LoginNoticeControl();
        CharCreateControl = new CharacterCreationControl(Game.AislingRenderer);
        PasswordChangeControl = new PasswordChangeControl();

        //wire button events
        StartPanel.ContinueButton?.Clicked += OnContinueClicked;
        StartPanel.ExitButton?.Clicked += OnExitClicked;
        StartPanel.SubmitCreateButton?.Clicked += OnCreateClicked;
        StartPanel.PasswordButton?.Clicked += OnPasswordClicked;
        StartPanel.CreditButton?.Clicked += OnCreditClicked;
        StartPanel.HomepageButton?.Clicked += OnHomepageClicked;

        //track last-clicked start panel button so enter can repeat it
        foreach (var btn in (UIButton?[]) [
                     StartPanel.ContinueButton,
                     StartPanel.ExitButton,
                     StartPanel.SubmitCreateButton,
                     StartPanel.PasswordButton,
                     StartPanel.CreditButton,
                     StartPanel.HomepageButton
                 ])
            if (btn is not null)
                btn.Clicked += () => LastClickedButton = btn;

        LoginControl.OkButton?.Clicked += OnLoginOkClicked;
        LoginControl.CancelButton?.Clicked += OnLoginCancelClicked;

        ServerSelectControl.OnServerSelected += OnServerSelected;

        LoginNoticeControl.OnOk += OnLoginAccepted;
        LoginNoticeControl.OnCancel += OnLoginCancelled;

        CharCreateControl.OnOk += OnCharCreateOkClicked;
        CharCreateControl.OnCancel += OnCharCreateCancelClicked;

        PasswordChangeControl.OnOk += OnPasswordChangeOkClicked;
        PasswordChangeControl.OnCancel += OnPasswordChangeCancelClicked;

        LobbyLoginPopupMessage = new OkPopupMessageControl
        {
            ZIndex = 1,
            Name = "LobbyLoginPopupMessage"
        };
        LobbyLoginPopupMessage.OnOk += OnLobbyLoginPopupMessageOk;

        Root = new LobbyRootPanel
        {
            Name = "LobbyRoot",
            Width = ChaosGame.VIRTUAL_WIDTH,
            Height = ChaosGame.VIRTUAL_HEIGHT
        };
        Root.AddChild(StartPanel);
        Root.AddChild(LoginControl);
        Root.AddChild(ServerSelectControl);
        Root.AddChild(LoginNoticeControl);
        Root.AddChild(CharCreateControl);
        Root.AddChild(PasswordChangeControl);
        Root.AddChild(LobbyLoginPopupMessage);

        //build ui atlas after all login controls are constructed
        UiRenderer.Instance?.BuildAtlas();

        WireRootInputHandlers();

        if (ReturningFromWorld)
        {
            //already connected to login server via redirect — skip lobby handshake, show login directly
            StartPanel.SetButtonsEnabled(false);
            LoginControl.Show();
        } else

            //fresh start — connect to lobby
            BeginLobbyConnect();
    }

    /// <inheritdoc />
    public void UnloadContent()
    {
        Game.Connection.StateChanged -= OnConnectionStateChanged;
        Game.Connection.OnError -= OnConnectionError;
        Game.Connection.OnServerTableReceived -= OnServerTableReceived;
        Game.Connection.OnRedirectReceived -= OnRedirectReceived;
        Game.Connection.OnLoginMessage -= OnLoginMessage;
        Game.Connection.OnLoginNotice -= OnLoginNotice;
        Game.Connection.OnLoginControl -= OnLoginControlReceived;
    }

    /// <inheritdoc />
    public void Update(GameTime gameTime)
    {
        if (PendingWorldSwitch)
        {
            PendingWorldSwitch = false;

            if (Game.Connection.State != ConnectionState.World)
            {
                //connection died before world screen could be created — restart login flow
                Game.Screens.Switch(new LobbyLoginScreen());

                return;
            }

            Game.Screens.Switch(new WorldScreen());

            return;
        }

        Game.Dispatcher.ProcessInput(Root!, gameTime);
        Root!.Update(gameTime);
    }

    private void WireRootInputHandlers() => ((LobbyRootPanel)Root!).Screen = this;

    #region Button Handlers
    private void OnContinueClicked()
    {
        if (Connecting || LoginControl.Visible || PasswordChangeControl.Visible)
            return;

        LoginControl.Show();
        StartPanel.SetButtonsEnabled(false);
    }

    private void OnExitClicked() => Game.Exit();

    private void OnCreateClicked()
    {
        if (Connecting || LoginControl.Visible || PasswordChangeControl.Visible)
            return;

        CharCreateControl.Show();
    }

    private void OnCharCreateOkClicked()
    {
        var name = CharCreateControl.NameField?.Text;
        var password = CharCreateControl.PasswordField?.Text;
        var passwordConfirm = CharCreateControl.PasswordConfirmField?.Text;

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(password))
        {
            LobbyLoginPopupMessage.Show("Name and password are required.");

            return;
        }

        if (password != passwordConfirm)
        {
            LobbyLoginPopupMessage.Show("Passwords do not match.");
            CharCreateControl.PasswordField?.Text = string.Empty;
            CharCreateControl.PasswordConfirmField?.Text = string.Empty;

            return;
        }

        Connecting = true;
        CreatingCharacter = true;
        AwaitingCharFinalize = false;
        Game.Connection.CreateCharInitial(name, password);
    }

    private void OnCharCreateCancelClicked()
    {
        CharCreateControl.Hide();
        CreatingCharacter = false;
        AwaitingCharFinalize = false;
    }

    private void OnPasswordClicked()
    {
        if (Connecting || LoginControl.Visible || PasswordChangeControl.Visible)
            return;

        PasswordChangeControl.Show();
        StartPanel.SetButtonsEnabled(false);
    }

    private void OnPasswordChangeOkClicked()
    {
        var name = PasswordChangeControl.NameField?.Text ?? string.Empty;
        var currentPassword = PasswordChangeControl.CurrentPasswordField?.Text ?? string.Empty;
        var newPassword = PasswordChangeControl.NewPasswordField?.Text ?? string.Empty;
        var confirmPassword = PasswordChangeControl.ConfirmPasswordField?.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(currentPassword) || string.IsNullOrWhiteSpace(newPassword))
        {
            LobbyLoginPopupMessage.Show("All fields are required.");

            return;
        }

        if (newPassword != confirmPassword)
        {
            LobbyLoginPopupMessage.Show("New passwords do not match.");
            PasswordChangeControl.NewPasswordField?.Text = string.Empty;
            PasswordChangeControl.ConfirmPasswordField?.Text = string.Empty;

            return;
        }

        Connecting = true;
        ChangingPassword = true;
        Game.Connection.ChangePassword(name, currentPassword, newPassword);
    }

    private void OnPasswordChangeCancelClicked()
    {
        PasswordChangeControl.Hide();
        ChangingPassword = false;
        StartPanel.SetButtonsEnabled(true);
    }

    private void OnCreditClicked()
    {
        //credits panel not yet implemented
    }

    private void OnHomepageClicked()
    {
        if (string.IsNullOrWhiteSpace(HomepageUrl))

            //homepage url not yet received
            return;

        try
        {
            Process.Start(
                new ProcessStartInfo(HomepageUrl)
                {
                    UseShellExecute = true
                });
        } catch
        {
            //could not open browser
        }
    }

    private void OnLoginOkClicked()
    {
        var username = LoginControl.UsernameField?.Text ?? string.Empty;
        var password = LoginControl.PasswordField?.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))

            //username and password are required
            return;

        Connecting = true;
        LoginControl.Visible = false;

        //logging in...
        WorldState.PlayerName = username;

        Game.Connection.Login(
            username,
            password,
            MachineIdentity.ClientId1,
            MachineIdentity.ClientId2);
    }

    private void OnLoginCancelClicked()
    {
        LoginControl.Hide();
        StartPanel.SetButtonsEnabled(true);
    }

    private void OnLobbyLoginPopupMessageOk() => LobbyLoginPopupMessage.Hide();

    private void OnServerSelected(byte serverId)
    {
        ServerSelectControl.Visible = false;

        var server = ServerList.FirstOrDefault(s => s.Id == serverId);

        if (server is not null)
        {
            Game.Connection.ServerName = server.SplitNameDescription().Name;
            Game.UpdateWindowTitle();
        }

        Game.Connection.SelectServer(serverId);
    }
    #endregion

    #region Connection Flow
    private async void BeginLobbyConnect()
    {
        Connecting = true;

        await Game.Connection.ConnectToLobbyAsync(DataContext.LobbyHost, DataContext.LobbyPort, DataContext.ClientVersion);
    }

    private void OnConnectionStateChanged(ConnectionState oldState, ConnectionState newState)
    {
        switch (newState)
        {
            case ConnectionState.Lobby:
                break;

            case ConnectionState.Login:
                Connecting = false;

                //buttons are enabled after eula acceptance (or checksum cache hit) in onloginnotice
                break;

            case ConnectionState.World:
                PendingWorldSwitch = true;

                break;

            case ConnectionState.Disconnected when Connecting:
                Connecting = false;

                break;
        }
    }

    private void OnConnectionError(string error) => Connecting = false;

    private void OnServerTableReceived(IList<ServerEntry> servers)
    {
        ServerList = servers;

        //the 0x56 wire is [count][entries] with no server-list flag (retail-verified); show the picker
        //whenever more than one server is advertised, else auto-select the only one
        if (servers.Count > 1)
        {
            ServerSelectControl.SetServers(servers);
            ServerSelectControl.Visible = true;
        } else if (servers.Count > 0)
        {
            //auto-select the first (or only) server
            Game.Connection.ServerName = servers[0].SplitNameDescription().Name;
            Game.UpdateWindowTitle();
            Game.Connection.SelectServer(servers[0].Id);
        }
    }

    private void OnRedirectReceived(RedirectInfo _)
    {
        //following redirect...
    }

    private void OnLoginMessage(LoginMessagePacket pkt)
    {
        var messageType = (LoginMessageType)pkt.Type;

        if (CreatingCharacter)
        {
            HandleCharCreateMessage(pkt);

            return;
        }

        if (ChangingPassword)
        {
            HandlePasswordChangeMessage(pkt);

            return;
        }

        if (messageType == LoginMessageType.Confirm)

            //login accepted. waiting for redirect...
            return;

        //login failed — show login again for retry, clear password
        Connecting = false;
        LoginControl.Visible = true;

        if (LoginControl.PasswordField is not null)
        {
            LoginControl.PasswordField.Text = string.Empty;
            LoginControl.PasswordField.IsFocused = true;
        }

        LobbyLoginPopupMessage.Show(pkt.Message ?? "Login failed.");
    }

    private void HandleCharCreateMessage(LoginMessagePacket pkt)
    {
        var messageType = (LoginMessageType)pkt.Type;

        if (messageType == LoginMessageType.Confirm)
        {
            if (!AwaitingCharFinalize)
            {
                //initial step confirmed — send finalize with appearance (setting appearance...)
                AwaitingCharFinalize = true;

                Game.Connection.CreateCharFinalize(
                    CharCreateControl.SelectedHairStyle,
                    CharCreateControl.SelectedGender,
                    CharCreateControl.SelectedHairColor);
            } else
            {
                //finalize confirmed — character created, show popup
                Connecting = false;
                CreatingCharacter = false;
                AwaitingCharFinalize = false;
                CharCreateControl.Hide();
                LobbyLoginPopupMessage.Show("Character has been created. Choose \"CONTINUE\".");
            }

            return;
        }

        //creation failed — show error popup and clear the relevant field
        Connecting = false;
        AwaitingCharFinalize = false;

        switch (messageType)
        {
            case LoginMessageType.ClearNameMessage:
                CharCreateControl.NameField?.Text = string.Empty;

                break;
            case LoginMessageType.ClearPswdMessage:
                CharCreateControl.PasswordField?.Text = string.Empty;
                CharCreateControl.PasswordConfirmField?.Text = string.Empty;

                break;
            case LoginMessageType.Confirm:
                break;
            case LoginMessageType.CharacterDoesntExist:
                break;
            case LoginMessageType.WrongPassword:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        LobbyLoginPopupMessage.Show(pkt.Message ?? "Character creation failed.");
    }

    private void HandlePasswordChangeMessage(LoginMessagePacket pkt)
    {
        Connecting = false;
        ChangingPassword = false;

        if ((LoginMessageType)pkt.Type == LoginMessageType.Confirm)
        {
            PasswordChangeControl.Hide();
            LobbyLoginPopupMessage.Show("Password has been changed.");

            return;
        }

        LobbyLoginPopupMessage.Show(pkt.Message ?? "Password change failed.");
    }

    private void OnLoginNotice(LoginNotificationPacket pkt)
    {
        var checkSum = (pkt.Form as NotificationChecksumForm)?.Checksum;
        var data = (pkt.Form as NotificationDataForm)?.Data;
        var isFullResponse = pkt.Form is NotificationDataForm;

        NoticeDebugLog.Write($"OnLoginNotice IsFull={isFullResponse} CRC={checkSum:X8} DataLen={data?.Length} Returning={ReturningFromWorld}");

        //returning from world — already accepted the eula this session, skip entirely
        if (ReturningFromWorld)
        {
            StartPanel.EnableButtons();

            return;
        }

        if (!isFullResponse)
        {
            //checksum-only probe — request full notice if we don't have a cached match
            if (CachedNoticeCheckSum.HasValue && (CachedNoticeCheckSum.Value == checkSum))
            {
                //already accepted this notice, skip display and enable buttons
                NoticeDebugLog.Write("  checksum cache hit, enabling buttons");
                StartPanel.EnableButtons();

                return;
            }

            NoticeDebugLog.Write("  probe received, calling RequestNotice()");
            Game.Connection.RequestNotice();

            return;
        }

        //full response — decompress and display
        if (data is null or { Length: 0 })
        {
            NoticeDebugLog.Write("  !!! full response with empty data — UI would be soft-locked");
            return;
        }

        string noticeText;
        try
        {
            noticeText = DecompressNotice(data);
            NoticeDebugLog.Write($"  decompressed ok, text length={noticeText.Length}");
        }
        catch (Exception ex)
        {
            NoticeDebugLog.Write($"  !!! DecompressNotice threw {ex.GetType().Name}: {ex.Message}");
            throw;
        }

        NoticeDebugLog.Write($"  calling Show, prior Visible={LoginNoticeControl.Visible}");
        LoginNoticeControl.Show(noticeText);
        NoticeDebugLog.Write($"  after Show, Visible={LoginNoticeControl.Visible}");
    }

    private void OnLoginAccepted()
    {
        LoginNoticeControl.Hide();
        StartPanel.EnableButtons();
    }

    private void OnLoginCancelled() => Game.Exit();

    private string DecompressNotice(byte[] compressedData)
    {
        // Hybrasyl-compatible path: skip 2-byte zlib header and 4-byte Adler32 trailer,
        // decompress the raw deflate body.
        var deflateLength = compressedData.Length - 6;
        using var compressed = new MemoryStream(compressedData, 2, deflateLength);
        using var decompressor = new DeflateStream(compressed, CompressionMode.Decompress);
        using var decompressed = new MemoryStream();
        decompressor.CopyTo(decompressed);

        var rawBytes = decompressed.ToArray();
        //retail + Hybrasyl both send standard (inverted) CRC-32 — verified against the retail checksum routine
        CachedNoticeCheckSum = CRC32.Calculate(rawBytes);

        var text = Encoding.GetEncoding(949)
                           .GetString(rawBytes);

        //only Kru's retail Dark Ages sends the legacy fixed-width notice; Hybrasyl and other modern
        //hosts send clean newline-delimited text that should be displayed verbatim.
        return GlobalSettings.IsCursed ? NormalizeNoticeText(text) : text;
    }

    // Kru's retail login notice uses the legacy fixed-width server.msg layout: logical lines are
    // separated by TAB and each is right-padded with spaces to a fixed column width. Blank lines are
    // encoded as literal "." lines, and a RUN of consecutive TABs is a single hard break (not one per
    // tab). Brigid word-wraps the agreement text, so without normalization the trailing space padding
    // overflows into blank lines and each tab in a run becomes its own blank line. Collapse tab runs
    // to a single break, convert to newlines, strip each line's trailing padding, and (per Kedian)
    // turn the "." spacer lines into real blank lines so wrapping reproduces a clean retail layout.
    private static string NormalizeNoticeText(string text)
    {
        text = text.Replace("\r\n", "\n");

        //a run of consecutive TABs is one line break, mirroring the legacy renderer
        while (text.Contains("\t\t"))
            text = text.Replace("\t\t", "\t");

        var lines = text.Replace('\t', '\n')
                        .Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd(' ');

            //a line whose only content is "." is a blank-line spacer in this format
            lines[i] = line == "." ? string.Empty : line;
        }

        return string.Join('\n', lines);
    }

    private void OnLoginControlReceived(UrlPacket pkt)
    {
        //subtype 3 (SetUrlForm) carries the homepage / account URL.
        if (pkt.Form is SetUrlForm setUrl)
            HomepageUrl = setUrl.Url;
    }
    #endregion

    /// <summary>
    ///     Root panel for LobbyLoginScreen. Handles Enter-to-repeat and ServerSelect Escape dismiss
    ///     at the root level when no focused sub-panel claims keyboard input.
    /// </summary>
    private sealed class LobbyRootPanel : UIPanel
    {
        public LobbyLoginScreen? Screen { get; set; }

        public override void OnKeyDown(KeyDownEvent e)
        {
            if (Screen is null)
                return;

            //alt+enter — cycle window size
            if ((e.Key == Keys.Enter) && e.Modifiers.HasFlag(KeyModifiers.Alt))
            {
                Screen.Game.CycleWindowSize();
                e.Handled = true;

                return;
            }

            //enter — repeat last-clicked button when no sub-control is open
            if ((e.Key == Keys.Enter)
                && Screen.LastClickedButton is { Enabled: true }
                && !Screen.LoginControl.Visible
                && !Screen.ServerSelectControl.Visible
                && !Screen.CharCreateControl.Visible
                && !Screen.PasswordChangeControl.Visible)
            {
                Screen.LastClickedButton.PerformClick();
                e.Handled = true;

                return;
            }

            //escape — dismiss serverselectcontrol when it is visible and nothing else claims focus
            if ((e.Key == Keys.Escape) && Screen.ServerSelectControl.Visible)
            {
                Screen.ServerSelectControl.Visible = false;
                e.Handled = true;
            }
        }
    }
}