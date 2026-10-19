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
using Brigid.Utilities;
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
    private const string CONNECTION_LOST = "The connection to the server was lost.";
    private const string NOT_CONNECTED = "The client is not connected to the server.";

    private readonly bool ReturningFromWorld;
    private bool AwaitingCharFinalize;

    private uint? CachedNoticeCheckSum;
    private bool ChangingPassword;
    private CharacterCreationControl CharCreateControl = null!;

    //flow state
    private bool Connecting;
    private bool CreatingCharacter;
    private bool FollowingRedirect;
    private LoginNoticeControl LoginNoticeControl = null!;

    private ChaosGame Game = null!;
    private string? HomepageUrl;
    private LoginControl LoginControl = null!;
    private PasswordChangeControl PasswordChangeControl = null!;
    private bool PendingWorldSwitch;
    private bool PendingConfigSwitch;
    private OkPopupMessageControl LobbyLoginPopupMessage = null!;
    private OkPopupMessageControl MigrationPrompt = null!;
    private CertificateTrustPromptControl TrustPrompt = null!;
    private InsecureConnectionPromptControl InsecurePrompt = null!;

    //last transport state written to the start screen's connection line, so the poll only writes on a
    //change. Drawn tracks the first write, which has to happen even though false is the initial state.
    private bool ConnectionLineSecure;
    private bool ConnectionLineDrawn;
    private IList<ServerEntry> ServerList = [];
    private ServerSelectControl ServerSelectControl = null!;

    private UIButton? LastClickedButton;

    //ui panels
    private LobbyLoginControl StartPanel = null!;

    //BRIG-107 preview: the new main menu over the legacy one, while BRIGID_MAIN_MENU_PREVIEW=1. Phase 4 replaces the
    //legacy menu with it and removes this.
    private MainMenuControl? PreviewMenu;
    private ServerGreetingControl? PreviewGreeting;

    //BRIG-116 preview: the primitive creator in place of the legacy one, while BRIGID_MAIN_MENU_PREVIEW=1. The Creator*
    //helpers pick it; phase 4 deletes the legacy creator and their legacy branch.
    private CharacterCreatorControl? PreviewCreator;

    //BRIG-119 preview: the primitive login and password forms in place of the legacy ones, while BRIGID_MAIN_MENU_PREVIEW=1.
    //The Login* and Password* helpers pick them; phase 4 deletes the legacy forms and their legacy branch.
    private LoginFormControl? PreviewLogin;
    private PasswordFormControl? PreviewPassword;

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
        StartPanel.ConfigButton?.Clicked += OnConfigClicked;

        if (Environment.GetEnvironmentVariable("BRIGID_MAIN_MENU_PREVIEW") == "1")
        {
            PreviewMenu = new MainMenuControl();
            PreviewMenu.ContinueButton.Clicked += OnContinueClicked;
            PreviewMenu.ExitButton.Clicked += OnExitClicked;
            PreviewMenu.CreateButton.Clicked += OnCreateClicked;
            PreviewMenu.PasswordButton.Clicked += OnPasswordClicked;
            PreviewMenu.HomepageButton.Clicked += OnHomepageClicked;
            PreviewMenu.ConfigButton.Clicked += OnConfigClicked;

            //sample alerts, to see the panel; the preview import alert records no decision
            PreviewMenu.Alerts.Add(MenuAlerts.NoSound(OperatingSystem.IsLinux()));
            PreviewMenu.Alerts.Add(MenuAlerts.LegacyImport(static () => { }, static () => { }));

            PreviewGreeting = new ServerGreetingControl();
            PreviewGreeting.Accepted += OnLoginAccepted;
            PreviewGreeting.Declined += OnLoginCancelled;

            //the greeting covers the menu buttons and the alerts until the player answers it. A fresh start waits for it with
            //them hidden; a return from the world gets no greeting
            var menu = PreviewMenu;
            PreviewGreeting.VisibilityChanged += visible => menu.SetCovered(visible);

            if (!ReturningFromWorld)
                PreviewMenu.CoverUntilGreeting();

            PreviewCreator = new CharacterCreatorControl(Game.AislingRenderer, CharacterCreatorControl.DyeSwatchColor);
            PreviewCreator.OnOk += OnCharCreateOkClicked;
            PreviewCreator.OnCancel += OnCharCreateCancelClicked;

            PreviewLogin = new LoginFormControl();
            PreviewLogin.OnOk += OnLoginOkClicked;
            PreviewLogin.OnCancel += OnLoginCancelClicked;

            PreviewPassword = new PasswordFormControl();
            PreviewPassword.OnOk += OnPasswordChangeOkClicked;
            PreviewPassword.OnCancel += OnPasswordChangeCancelClicked;
        }

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

        LobbyLoginPopupMessage = new OkPopupMessageControl("Notification")
        {
            ZIndex = 1,
            Name = "LobbyLoginPopupMessage"
        };
        LobbyLoginPopupMessage.OnOk += OnLobbyLoginPopupMessageOk;

        //one-time first-run offer to import legacy per-character config (OK = import, Cancel = start fresh)
        MigrationPrompt = new OkPopupMessageControl("Import Settings", showCancel: true)
        {
            ZIndex = 2,
            Name = "MigrationPrompt"
        };
        MigrationPrompt.OnOk += OnMigrationImport;
        MigrationPrompt.OnCancel += OnMigrationSkip;

        TrustPrompt = new CertificateTrustPromptControl { Name = "TrustPrompt" };
        TrustPrompt.OnTrusted += OnCertificateTrusted;
        TrustPrompt.OnDeclined += CertificateTrustStore.Decline;

        InsecurePrompt = new InsecureConnectionPromptControl { Name = "InsecurePrompt" };
        InsecurePrompt.OnContinued += OnInsecureConnectionAccepted;
        InsecurePrompt.OnRefused += OnInsecureConnectionRefused;

        Root = new LobbyRootPanel
        {
            Name = "LobbyRoot",
            Width = ChaosGame.VIRTUAL_WIDTH,
            Height = ChaosGame.VIRTUAL_HEIGHT
        };
        Root.AddChild(StartPanel);

        if (PreviewMenu is not null)
            Root.AddChild(PreviewMenu);

        if (PreviewGreeting is not null)
            Root.AddChild(PreviewGreeting);
        Root.AddChild(LoginControl);

        if (PreviewLogin is not null)
            Root.AddChild(PreviewLogin);
        Root.AddChild(ServerSelectControl);
        Root.AddChild(LoginNoticeControl);
        Root.AddChild(CharCreateControl);

        if (PreviewCreator is not null)
            Root.AddChild(PreviewCreator);
        Root.AddChild(PasswordChangeControl);

        if (PreviewPassword is not null)
            Root.AddChild(PreviewPassword);
        Root.AddChild(LobbyLoginPopupMessage);
        Root.AddChild(MigrationPrompt);
        Root.AddChild(TrustPrompt);
        Root.AddChild(InsecurePrompt);

        //build ui atlas after all login controls are constructed
        UiRenderer.Instance?.BuildAtlas();

        WireRootInputHandlers();

        MaybeShowMigrationPrompt();

        if (ReturningFromWorld)
        {
            //already connected to login server via redirect — skip lobby handshake, show login directly
            StartPanel.SetButtonsEnabled(false);
            ShowLogin();
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

        //deferred so the switch happens at the top of a frame, not inside ProcessInput below: OnConfigClicked fires
        //from within input dispatch, and switching there would dispose this screen's control tree mid-walk (the same
        //reason PendingWorldSwitch exists). Reuses the loaded assets — no FinishAssetInitialization.
        if (PendingConfigSwitch)
        {
            PendingConfigSwitch = false;
            Game.Screens.Switch(new LauncherScreen(openedFromMainMenu: true));

            return;
        }

        MaybeShowTrustPrompt();
        MaybeShowInsecurePrompt();
        RefreshConnectionLine();

        //the preview follows the legacy menu, which the screen enables and disables, and the server name, which the
        //lobby learns after the connection line is first drawn. SetConnection changes nothing when the text is the same.
        PreviewMenu?.SetButtonsEnabled(StartPanel.ContinueButton?.Enabled == true);
        PreviewMenu?.SetConnection(DataContext.LobbyHost, ConnectionLineSecure, Game.Connection.ServerName);

        Game.Dispatcher.ProcessInput(Root!, gameTime);
        Root!.Update(gameTime);
    }

    /// <summary>
    ///     Raises the certificate prompt from the game loop rather than from the connect failure that
    ///     produced it: that failure surfaces on the connect task's thread, and building a modal there
    ///     would mutate the control tree while this loop is drawing it.
    /// </summary>
    private void MaybeShowTrustPrompt()
    {
        if (TrustPrompt.Visible || CertificateTrustStore.Pending is not { } pending)
            return;

        Connecting = false;
        TrustPrompt.Show(pending);
    }

    /// <summary>
    ///     Keeps the start screen's connection line current. Polled rather than driven by the transport
    ///     event, which fires on the connect task — this loop is the thread allowed to touch the control
    ///     tree, and the line has to survive a refused certificate and a reconnect as well.
    /// </summary>
    private void RefreshConnectionLine()
    {
        var secure = Game.Connection.Client.Negotiated is not null;

        if ((secure == ConnectionLineSecure) && ConnectionLineDrawn)
            return;

        ConnectionLineSecure = secure;
        ConnectionLineDrawn = true;
        StartPanel.SetConnection(DataContext.LobbyHost, secure);
    }

    /// <summary>
    ///     Raises the downgrade warning, from the game loop for the same reason as the trust prompt. The
    ///     trust prompt wins a tie: it is about a connection that has already been refused, while this
    ///     one is about a connection that is live and about to be given a password.
    /// </summary>
    private void MaybeShowInsecurePrompt()
    {
        if (TrustPrompt.Visible || InsecurePrompt.Visible || CertificateTrustStore.PendingDowngrade is not { } server)
            return;

        Connecting = false;
        InsecurePrompt.Show(server);
    }

    private void OnCertificateTrusted(string server, string fingerprint, CertificateTrustPath path)
    {
        CertificateTrustStore.Trust(server, fingerprint, path);

        //the refusal that raised the prompt already dropped the connection, so accepting means
        //reconnecting. A hop reached by redirect is retried as that hop — restarting at the lobby would
        //throw away a login the user has already completed.
        if (Game.Connection.RetryLastRedirect())
            return;

        BeginLobbyConnect();
    }

    private void OnInsecureConnectionAccepted(string server)
    {
        CertificateTrustStore.ClearDowngrade(server, true);
        NoticeDebugLog.Write($"user accepted an unencrypted connection to {server}");
    }

    private void OnInsecureConnectionRefused(string server)
    {
        CertificateTrustStore.ClearDowngrade(server, false);
        NoticeDebugLog.Write($"user refused an unencrypted connection to {server}");

        Game.Connection.Disconnect();
        StartPanel.SetButtonsEnabled(true);
        LobbyLoginPopupMessage.Show("Disconnected. The server did not offer an encrypted connection.");
    }

    private void WireRootInputHandlers() => ((LobbyRootPanel)Root!).Screen = this;

    #region Button Handlers
    private void OnContinueClicked()
    {
        if (Connecting || LoginVisible || PasswordFormVisible)
            return;

        ShowLogin();
        StartPanel.SetButtonsEnabled(false);
    }

    private void OnExitClicked() => Game.Exit();

    //open the launcher/config screen over the loaded session. Deferred (PendingConfigSwitch) rather than switched here:
    //this runs inside input dispatch, and switching mid-walk would dispose this screen's tree while it's being handled.
    //The summoned launcher surfaces its Return-to-Main-Menu close and locks the data-folder selector.
    private void OnConfigClicked() => PendingConfigSwitch = true;

    private void OnCreateClicked()
    {
        if (Connecting || LoginVisible || PasswordFormVisible || CreatorVisible)
            return;

        ShowCreator();
    }

    private void OnCharCreateOkClicked()
    {
        //a second Create before the server answers the first would start a second creation
        if (CreatingCharacter)
            return;

        if (Game.Connection.State != ConnectionState.Login)
        {
            ShowCreatorMessage(NOT_CONNECTED);

            return;
        }

        var (name, password, passwordConfirm) = ReadCreatorFields();

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(password))
        {
            ShowCreatorMessage("Name and password are required.");

            return;
        }

        if (password != passwordConfirm)
        {
            ShowCreatorMessage("Passwords do not match.");
            ClearCreatorField(CharCreateFailureField.Password);

            return;
        }

        Connecting = true;
        CreatingCharacter = true;
        AwaitingCharFinalize = false;
        PreviewCreator?.SetBusy(true);
        Game.Connection.CreateCharInitial(name, password);
    }

    private void OnCharCreateCancelClicked()
    {
        //a cancel while the server has not answered leaves the menu usable; the answer, if it comes, is not waited for
        if (CreatingCharacter)
            Connecting = false;

        HideCreator();
        CreatingCharacter = false;
        AwaitingCharFinalize = false;
    }

    private void OnPasswordClicked()
    {
        if (Connecting || LoginVisible || PasswordFormVisible)
            return;

        ShowPasswordForm();
        StartPanel.SetButtonsEnabled(false);
    }

    private void OnPasswordChangeOkClicked()
    {
        var (name, currentPassword, newPassword, confirmPassword) = ReadPasswordFields();

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(currentPassword) || string.IsNullOrWhiteSpace(newPassword))
        {
            ShowPasswordMessage("All fields are required.");

            return;
        }

        if (newPassword != confirmPassword)
        {
            ShowPasswordMessage("New passwords do not match.");
            ClearNewPasswords();

            return;
        }

        //a change sent with no connection gets no answer, so the form would wait with no way out
        if (Game.Connection.State != ConnectionState.Login)
        {
            ShowPasswordMessage(NOT_CONNECTED);

            return;
        }

        Connecting = true;
        ChangingPassword = true;
        PreviewPassword?.SetBusy(true);
        Game.Connection.ChangePassword(name, currentPassword, newPassword);
    }

    private void OnPasswordChangeCancelClicked()
    {
        HidePasswordForm();
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

        Browser.Open(HomepageUrl);
    }

    private void OnLoginOkClicked()
    {
        var (username, password) = ReadLoginFields();

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            //the legacy form ignores the click; the primitive one says why
            if (PreviewLogin is not null)
                ShowLoginMessage("Name and password are required.");

            return;
        }

        //a login sent with no connection gets no answer, so the form would wait with no way out
        if (Game.Connection.State != ConnectionState.Login)
        {
            ShowLoginMessage(NOT_CONNECTED);

            return;
        }

        Connecting = true;
        SetLoginWaiting();

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
        HideLogin();
        StartPanel.SetButtonsEnabled(true);
    }

    private void OnLobbyLoginPopupMessageOk() => LobbyLoginPopupMessage.Hide();

    //── one-time legacy character-data import offer ──

    private void MaybeShowMigrationPrompt()
    {
        //ask only once, and only when there is actually legacy per-character config to import
        if (ClientSettings.LegacyCharacterImport != ClientSettings.MigrationUndecided)
            return;

        if (!DataContext.LocalPlayerSettings.HasLegacyCharacterData())
        {
            //nothing to import — record the decision so we never re-scan or re-ask
            SetMigrationDecision(ClientSettings.MigrationSkip);

            return;
        }

        MigrationPrompt.Show(
            "Import your existing Dark Ages character settings (macros, friends, family, spells)?\n\n"
            + "OK = Import      Cancel = Start Fresh");
    }

    private void OnMigrationImport()
    {
        SetMigrationDecision(ClientSettings.MigrationImport);
        MigrationPrompt.Hide();
    }

    private void OnMigrationSkip()
    {
        SetMigrationDecision(ClientSettings.MigrationSkip);
        MigrationPrompt.Hide();
    }

    private static void SetMigrationDecision(int decision)
    {
        ClientSettings.LegacyCharacterImport = decision;
        ClientSettings.Save();
    }

    private void OnServerSelected(byte serverId)
    {
        ServerSelectControl.Visible = false;

        var server = ServerList.FirstOrDefault(s => s.Id == serverId);

        if (server is not null)
            //assigning ServerName raises ServerNameChanged → the window title refreshes itself (see ChaosGame ctor)
            Game.Connection.ServerName = server.SplitNameDescription().Name;

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
                FollowingRedirect = false;

                break;

            case ConnectionState.Login:
                FollowingRedirect = false;
                Connecting = false;

                //buttons are enabled after eula acceptance (or checksum cache hit) in onloginnotice
                break;

            case ConnectionState.World:
                FollowingRedirect = false;
                PendingWorldSwitch = true;

                break;

            //a redirect closes the old connection (from Lobby or Login) before it connects to the next server: that is not a
            //lost connection, so a form that waits keeps waiting. A redirect that cannot connect goes from Connecting
            case ConnectionState.Disconnected when FollowingRedirect:
                Connecting = false;

                if (oldState != ConnectionState.Connecting)
                    break;

                FollowingRedirect = false;
                StopWaitsOnConnectionLoss();

                break;

            case ConnectionState.Disconnected when Connecting:
                Connecting = false;
                StopWaitsOnConnectionLoss();

                break;
        }
    }

    private void OnConnectionError(string error)
    {
        FollowingRedirect = false;
        Connecting = false;
        StopWaitsOnConnectionLoss();
    }

    //a form that waits for the server cannot get its answer without the connection
    private void StopWaitsOnConnectionLoss()
    {
        StopCreationOnConnectionLoss();

        if (PreviewLogin is { IsBusy: true })
        {
            PreviewLogin.SetBusy(false);
            PreviewLogin.ShowError(CONNECTION_LOST);
        }

        if (!ChangingPassword)
            return;

        ChangingPassword = false;

        if (PreviewPassword is null)
            return;

        PreviewPassword.SetBusy(false);
        PreviewPassword.ShowError(CONNECTION_LOST);
    }

    //a creation that waits for the server cannot finish without the connection
    private void StopCreationOnConnectionLoss()
    {
        if (!CreatingCharacter)
            return;

        CreatingCharacter = false;
        AwaitingCharFinalize = false;

        if (PreviewCreator is null)
            return;

        PreviewCreator.SetBusy(false);
        PreviewCreator.ShowError(CONNECTION_LOST);
    }

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
            //auto-select the first (or only) server — the ServerName assignment refreshes the window title via its event
            Game.Connection.ServerName = servers[0].SplitNameDescription().Name;
            Game.Connection.SelectServer(servers[0].Id);
        }
    }

    //raised before the old connection closes, so the Disconnected that the close causes sees the flag
    private void OnRedirectReceived(RedirectInfo _) => FollowingRedirect = true;

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
        ShowLoginFailure(pkt.Message ?? "Login failed.");
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

                var (hairStyle, gender, hairColor) = ReadCreatorAppearance();
                Game.Connection.CreateCharFinalize(hairStyle, gender, hairColor);
            } else
            {
                //finalize confirmed — character created, show popup
                Connecting = false;
                CreatingCharacter = false;
                AwaitingCharFinalize = false;
                HideCreator();
                LobbyLoginPopupMessage.Show("Character has been created. Choose \"CONTINUE\".");
            }

            return;
        }

        //creation failed — show error popup and clear the relevant field
        Connecting = false;
        AwaitingCharFinalize = false;

        if (!CharCreateFailure.IsDocumented(pkt.Type))
            NoticeDebugLog.Write($"char-create: undocumented 0x02 result code 0x{pkt.Type:X2} — \"{pkt.Message}\"");

        CreatingCharacter = false;
        PreviewCreator?.SetBusy(false);
        ClearCreatorField(CharCreateFailure.FieldFor(pkt.Type));
        ShowCreatorMessage(pkt.Message ?? "Character creation failed.");
    }

    //BRIG-116: the creator that the Create button opens is the primitive one under the preview switch, else the legacy one
    private bool CreatorVisible => PreviewCreator?.Visible ?? CharCreateControl.Visible;

    private void ShowCreator()
    {
        if (PreviewCreator is not null)
            PreviewCreator.Show();
        else
            CharCreateControl.Show();
    }

    private void HideCreator()
    {
        if (PreviewCreator is not null)
            PreviewCreator.Hide();
        else
            CharCreateControl.Hide();
    }

    private (string? Name, string? Password, string? Confirm) ReadCreatorFields()
        => PreviewCreator is not null
            ? (PreviewCreator.NameField.Text, PreviewCreator.PasswordField.Text, PreviewCreator.PasswordConfirmField.Text)
            : (CharCreateControl.NameField?.Text, CharCreateControl.PasswordField?.Text, CharCreateControl.PasswordConfirmField?.Text);

    private (byte HairStyle, Gender Gender, DisplayColor HairColor) ReadCreatorAppearance()
        => PreviewCreator is not null
            ? (PreviewCreator.SelectedHairStyle, PreviewCreator.SelectedGender, PreviewCreator.SelectedHairColor)
            : (CharCreateControl.SelectedHairStyle, CharCreateControl.SelectedGender, CharCreateControl.SelectedHairColor);

    //the primitive creator shows a message in its status line; the legacy one has none, so the popup shows it
    private void ShowCreatorMessage(string message)
    {
        if (PreviewCreator is not null)
            PreviewCreator.ShowError(message);
        else
            LobbyLoginPopupMessage.Show(message);
    }

    private void ClearCreatorField(CharCreateFailureField field)
    {
        switch (field)
        {
            case CharCreateFailureField.Name when PreviewCreator is not null:
                PreviewCreator.ClearName();

                break;
            case CharCreateFailureField.Name:
                CharCreateControl.NameField?.Text = string.Empty;

                break;
            case CharCreateFailureField.Password when PreviewCreator is not null:
                PreviewCreator.ClearPasswords();

                break;
            case CharCreateFailureField.Password:
                CharCreateControl.PasswordField?.Text = string.Empty;
                CharCreateControl.PasswordConfirmField?.Text = string.Empty;

                break;
            case CharCreateFailureField.None:
                break;
        }
    }

    private void HandlePasswordChangeMessage(LoginMessagePacket pkt)
    {
        Connecting = false;
        ChangingPassword = false;

        if ((LoginMessageType)pkt.Type == LoginMessageType.Confirm)
        {
            HidePasswordForm();
            LobbyLoginPopupMessage.Show("Password has been changed.");

            return;
        }

        PreviewPassword?.SetBusy(false);
        ShowPasswordMessage(pkt.Message ?? "Password change failed.");
    }

    //BRIG-119: the login form that Continue opens is the primitive one under the preview switch, else the legacy one
    private bool LoginVisible => PreviewLogin?.Visible ?? LoginControl.Visible;

    private void ShowLogin()
    {
        if (PreviewLogin is not null)
            PreviewLogin.Show();
        else
            LoginControl.Show();
    }

    private void HideLogin()
    {
        if (PreviewLogin is not null)
            PreviewLogin.Hide();
        else
            LoginControl.Hide();
    }

    private (string Name, string Password) ReadLoginFields()
        => PreviewLogin is not null
            ? (PreviewLogin.NameField.Text, PreviewLogin.PasswordField.Text)
            : (LoginControl.UsernameField?.Text ?? string.Empty, LoginControl.PasswordField?.Text ?? string.Empty);

    //the legacy form hides while the server answers; the primitive one stays shown and busy
    private void SetLoginWaiting()
    {
        if (PreviewLogin is not null)
            PreviewLogin.SetBusy(true);
        else
            LoginControl.Visible = false;
    }

    //the primitive form shows a message in its status line; the legacy one has none, so the popup shows it
    private void ShowLoginMessage(string message)
    {
        if (PreviewLogin is not null)
            PreviewLogin.ShowError(message);
        else
            LobbyLoginPopupMessage.Show(message);
    }

    //the primitive form shows a failure in its status line; the legacy one shows again with the popup over it
    private void ShowLoginFailure(string message)
    {
        if (PreviewLogin is not null)
        {
            PreviewLogin.SetBusy(false);
            PreviewLogin.ClearPassword();
            PreviewLogin.ShowError(message);

            return;
        }

        LoginControl.Visible = true;

        if (LoginControl.PasswordField is not null)
        {
            LoginControl.PasswordField.Text = string.Empty;
            LoginControl.PasswordField.IsFocused = true;
        }

        LobbyLoginPopupMessage.Show(message);
    }

    //BRIG-119: the password form that Password opens is the primitive one under the preview switch, else the legacy one
    private bool PasswordFormVisible => PreviewPassword?.Visible ?? PasswordChangeControl.Visible;

    private void ShowPasswordForm()
    {
        if (PreviewPassword is not null)
            PreviewPassword.Show();
        else
            PasswordChangeControl.Show();
    }

    private void HidePasswordForm()
    {
        if (PreviewPassword is not null)
            PreviewPassword.Hide();
        else
            PasswordChangeControl.Hide();
    }

    private (string Name, string Current, string New, string Confirm) ReadPasswordFields()
        => PreviewPassword is not null
            ? (PreviewPassword.NameField.Text, PreviewPassword.CurrentPasswordField.Text, PreviewPassword.NewPasswordField.Text,
                PreviewPassword.ConfirmPasswordField.Text)
            : (PasswordChangeControl.NameField?.Text ?? string.Empty, PasswordChangeControl.CurrentPasswordField?.Text ?? string.Empty,
                PasswordChangeControl.NewPasswordField?.Text ?? string.Empty, PasswordChangeControl.ConfirmPasswordField?.Text ?? string.Empty);

    //the primitive form shows a message in its status line; the legacy one has none, so the popup shows it
    private void ShowPasswordMessage(string message)
    {
        if (PreviewPassword is not null)
            PreviewPassword.ShowError(message);
        else
            LobbyLoginPopupMessage.Show(message);
    }

    private void ClearNewPasswords()
    {
        if (PreviewPassword is not null)
        {
            PreviewPassword.ClearNewPasswords();

            return;
        }

        PasswordChangeControl.NewPasswordField?.Text = string.Empty;
        PasswordChangeControl.ConfirmPasswordField?.Text = string.Empty;
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

        NoticeDebugLog.Write($"  calling Show, prior Visible={PreviewGreeting?.Visible ?? LoginNoticeControl.Visible}");
        if (PreviewGreeting is not null)
            PreviewGreeting.Show(noticeText);
        else
            LoginNoticeControl.Show(noticeText);
        NoticeDebugLog.Write($"  after Show, Visible={PreviewGreeting?.Visible ?? LoginNoticeControl.Visible}");
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
                && !Screen.LoginVisible
                && !Screen.ServerSelectControl.Visible
                && !Screen.CreatorVisible
                && !Screen.PasswordFormVisible)
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