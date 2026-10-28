#region
using System.IO.Compression;
using System.Text;
using Brigid.Collections;
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Controls.LobbyLogin;
using Brigid.Data;
using Brigid.Data.AssetPacks;
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
    private const string NO_ANSWER = "The server did not answer.";
    private const string CHOOSE_SERVER = "Close this form, then choose a server.";

    //the longest time that a form waits for the server
    private const double WAIT_TIMEOUT_MS = 20_000;

    //the screen and its menu are made again on every return from the world and after Config; the alert shows once per
    //process
    private static bool NoSoundAlertAdded;
    private static bool AssetsAlertAdded;

    private readonly LobbyStart Start;
    private bool AwaitingCharFinalize;

    //the checksum of the greeting that the player accepted, and of the greeting that shows now. A reconnect skips only a
    //greeting that the player accepted
    private uint? CachedNoticeCheckSum;
    private uint? ShownNoticeCheckSum;

    //flow state. Connecting is true while the lobby connection starts; a form that waits for the server is busy
    //(LobbyFormPanel.IsBusy), and the busy form is the one that a server answer is for
    private bool Connecting;
    private bool FollowingRedirect;

    //set on the thread that lost the connection; Update stops the waits of the forms on the game loop
    private volatile bool ConnectionLost;

    //true after the server greeting (or a greeting that the player accepted before). The connection buttons of the menu
    //are enabled while it is true and no form is open. A new lobby connection sets it to false
    private bool LobbyReady;

    private ChaosGame Game = null!;
    private string? HomepageUrl;
    private bool PendingWorldSwitch;
    private bool PendingConfigSwitch;
    private OkPopupMessageControl LobbyLoginPopupMessage = null!;
    private CertificateTrustPromptControl TrustPrompt = null!;
    private InsecureConnectionPromptControl InsecurePrompt = null!;

    private IList<ServerEntry> ServerList = [];
    private ServerSelectControl ServerSelectControl = null!;

    //a server table that arrived while a form was open; the server list shows when the form closes
    private bool ServerListPending;

    //the menu button that Enter clicks again
    private TextButton? LastClickedButton;

    //ui panels
    private MainMenuControl Menu = null!;
    private ServerGreetingControl Greeting = null!;
    private CharacterCreatorControl Creator = null!;
    private LoginFormControl LoginForm = null!;
    private PasswordFormControl PasswordForm = null!;

    /// <inheritdoc />
    public UIPanel? Root { get; private set; }

    public LobbyLoginScreen(LobbyStart start = LobbyStart.Fresh) => Start = start;

    private bool ReturningFromWorld => Start == LobbyStart.ReturningFromWorld;

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
        ServerSelectControl = new ServerSelectControl();

        Menu = new MainMenuControl();
        Menu.ContinueButton.Clicked += OnContinueClicked;
        Menu.ExitButton.Clicked += OnExitClicked;
        Menu.CreateButton.Clicked += OnCreateClicked;
        Menu.PasswordButton.Clicked += OnPasswordClicked;
        Menu.HomepageButton.Clicked += OnHomepageClicked;
        Menu.ConfigButton.Clicked += OnConfigClicked;

        //Enter clicks the last of these again; Config switches the screen, so it is not one of them
        foreach (var button in (TextButton[]) [
                     Menu.CreateButton,
                     Menu.ContinueButton,
                     Menu.PasswordButton,
                     Menu.AboutButton,
                     Menu.HomepageButton,
                     Menu.StoryButton,
                     Menu.ExitButton
                 ])
            button.Clicked += () => LastClickedButton = button;

        Greeting = new ServerGreetingControl();
        Greeting.Accepted += OnLoginAccepted;
        Greeting.Declined += OnLoginCancelled;

        //the greeting covers the menu buttons and the alerts until the player answers it. Each lobby connection waits for
        //it with them hidden (BeginLobbyConnect); a return from the world in the Login state gets no greeting
        Greeting.VisibilityChanged += visible => Menu.SetCovered(visible);

        //the server list covers the menu too. When it closes before the greeting, the menu waits for the greeting again
        ServerSelectControl.VisibilityChanged += visible =>
        {
            if (visible)
                Menu.SetCovered(true);
            else if (LobbyReady)
                Menu.SetCovered(false);
            else
                Menu.CoverUntilGreeting();
        };

        Creator = new CharacterCreatorControl(Game.AislingRenderer, CharacterCreatorControl.DyeSwatchColor);
        Creator.OnOk += OnCharCreateOkClicked;
        Creator.OnCancel += OnCharCreateCancelClicked;

        LoginForm = new LoginFormControl();
        LoginForm.OnOk += OnLoginOkClicked;
        LoginForm.OnCancel += OnLoginCancelClicked;

        PasswordForm = new PasswordFormControl();
        PasswordForm.OnOk += OnPasswordChangeOkClicked;
        PasswordForm.OnCancel += OnPasswordChangeCancelClicked;

        ServerSelectControl.OnServerSelected += OnServerSelected;

        LobbyLoginPopupMessage = new OkPopupMessageControl("Notification")
        {
            ZIndex = 1,
            Name = "LobbyLoginPopupMessage"
        };
        LobbyLoginPopupMessage.OnOk += OnLobbyLoginPopupMessageOk;

        //the connection prompts can open while a notice above is still up; keep them on top so the visible prompt
        //is the one the control stack routes Enter/Escape to
        TrustPrompt = new CertificateTrustPromptControl { Name = "TrustPrompt", ZIndex = 3 };
        TrustPrompt.OnTrusted += OnCertificateTrusted;
        TrustPrompt.OnDeclined += CertificateTrustStore.Decline;

        InsecurePrompt = new InsecureConnectionPromptControl { Name = "InsecurePrompt", ZIndex = 3 };
        InsecurePrompt.OnContinued += OnInsecureConnectionAccepted;
        InsecurePrompt.OnRefused += OnInsecureConnectionRefused;

        Root = new LobbyRootPanel
        {
            Name = "LobbyRoot",
            Width = ChaosGame.VIRTUAL_WIDTH,
            Height = ChaosGame.VIRTUAL_HEIGHT
        };
        Root.AddChild(Menu);
        Root.AddChild(Greeting);
        Root.AddChild(LoginForm);
        Root.AddChild(ServerSelectControl);
        Root.AddChild(Creator);
        Root.AddChild(PasswordForm);
        Root.AddChild(LobbyLoginPopupMessage);
        Root.AddChild(TrustPrompt);
        Root.AddChild(InsecurePrompt);

        //build ui atlas after all login controls are constructed
        UiRenderer.Instance?.BuildAtlas();

        WireRootInputHandlers();

        //a lobby modal (a form, the greeting, a prompt) takes the mouse; ScreenManager.Switch resets this for the world
        Game.Dispatcher.EnforceModals = true;

        MaybeAddLegacyImportAlert();
        MaybeAddNoSoundAlert();
        MaybeAddAssetsAlert();

        //a logout redirect leaves the client connected to the login server. A server that closes the connection at
        //logout leaves no connection, and a login or a create would then wait for a reply that never comes, so start
        //again from the lobby
        if (ReturningFromWorld && (Game.Connection.State == ConnectionState.Login))
        {
            //already connected to login server via redirect — skip lobby handshake, show login directly. The greeting
            //was accepted before the world, so the menu is ready when the login form closes
            LobbyReady = true;
            LoginForm.Show();

            //the answer to the homepage request of the logout hop came while the world screen was shown
            Game.Connection.RequestHomepage();
        } else if ((Start == LobbyStart.ReturningFromConfig) && (Game.Connection.State == ConnectionState.Login))
        {
            //back from Config with the same server (BRIG-105): the connection stays, so the menu is ready at once. The
            //homepage URL came to the screen before Config, so ask for it again
            LobbyReady = true;
            Game.Connection.RequestHomepage();
        } else

            //fresh start, a new server from Config, or the server closed the connection — connect to lobby
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
            //the lobby connection stays only when the lobby is ready now (BRIG-105)
            var lobbyReady = LobbyReady && (Game.Connection.State == ConnectionState.Login);
            Game.Screens.Switch(new LauncherScreen(openedFromMainMenu: true, lobbyReady));

            return;
        }

        MaybeShowTrustPrompt();
        MaybeShowInsecurePrompt();

        if (ConnectionLost)
        {
            ConnectionLost = false;
            StopWaitsOnConnectionLoss();
            ShowOffline();
        }

        StopWaitsThatTimedOut();

        if (ServerListPending && !AnyFormOpen)
        {
            ServerListPending = false;
            ServerSelectControl.Visible = true;
        }

        Game.Dispatcher.ProcessInput(Root!, gameTime);

        //polled after the input, so that a form that the input opens or closes disables or enables the buttons in the
        //same frame. The transport changes on the connect task, and this loop is the thread that can touch the control
        //tree. The lobby learns the server name after the connection. The menu changes nothing when the state is the
        //same
        Menu.SetButtonsEnabled(LobbyReady && !AnyFormOpen);
        Menu.SetConnection(
            GlobalSettings.LobbyHost,
            Game.Connection.Client.Negotiated is not null,
            Game.Connection.ServerName);

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
        {
            Menu.Alerts.Remove(MenuAlerts.OFFLINE_ID);
            Menu.Alerts.Remove(MenuAlerts.GREETING_UNREADABLE_ID);

            return;
        }

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
        LobbyLoginPopupMessage.Show("Disconnected. The server did not offer an encrypted connection.");
    }

    private void WireRootInputHandlers() => ((LobbyRootPanel)Root!).Screen = this;

    #region Button Handlers
    private bool AnyFormOpen => LoginForm.Visible || PasswordForm.Visible || Creator.Visible;

    //a form that timed out stays open while the lobby connects again; with several servers, the list waits for it to close
    private string NotConnectedMessage => ServerListPending ? CHOOSE_SERVER : NOT_CONNECTED;

    private void OnContinueClicked()
    {
        if (Connecting || AnyFormOpen)
            return;

        LoginForm.Show();
    }

    private void OnExitClicked() => Game.Exit();

    //open the launcher/config screen over the loaded session. Deferred (PendingConfigSwitch) rather than switched here:
    //this runs inside input dispatch, and switching mid-walk would dispose this screen's tree while it's being handled.
    //The summoned launcher surfaces its Return-to-Main-Menu close and locks the data-folder selector.
    private void OnConfigClicked() => PendingConfigSwitch = true;

    private void OnCreateClicked()
    {
        if (Connecting || AnyFormOpen)
            return;

        Creator.Show();
    }

    private void OnCharCreateOkClicked()
    {
        if (Game.Connection.State != ConnectionState.Login)
        {
            Creator.ShowError(NotConnectedMessage);

            return;
        }

        var name = Creator.NameField.Text;
        var password = Creator.PasswordField.Text;

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(password))
        {
            Creator.ShowError("Name and password are required.");

            return;
        }

        if (password != Creator.PasswordConfirmField.Text)
        {
            Creator.ShowError("Passwords do not match.");
            Creator.ClearPasswords();

            return;
        }

        AwaitingCharFinalize = false;
        Creator.SetBusy(true);
        Game.Connection.CreateCharInitial(name, password);
    }

    private void OnCharCreateCancelClicked() => CloseForm(Creator);

    private void OnPasswordClicked()
    {
        if (Connecting || AnyFormOpen)
            return;

        PasswordForm.Show();
    }

    private void OnPasswordChangeOkClicked()
    {
        var name = PasswordForm.NameField.Text;
        var currentPassword = PasswordForm.CurrentPasswordField.Text;
        var newPassword = PasswordForm.NewPasswordField.Text;

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(currentPassword) || string.IsNullOrWhiteSpace(newPassword))
        {
            PasswordForm.ShowError("All fields are required.");

            return;
        }

        if (newPassword != PasswordForm.ConfirmPasswordField.Text)
        {
            PasswordForm.ShowError("New passwords do not match.");
            PasswordForm.ClearNewPasswords();

            return;
        }

        //a change sent with no connection gets no answer, so the form would wait with no way out
        if (Game.Connection.State != ConnectionState.Login)
        {
            PasswordForm.ShowError(NotConnectedMessage);

            return;
        }

        PasswordForm.SetBusy(true);
        Game.Connection.ChangePassword(name, currentPassword, newPassword);
    }

    private void OnPasswordChangeCancelClicked() => CloseForm(PasswordForm);

    private void OnHomepageClicked()
    {
        if (string.IsNullOrWhiteSpace(HomepageUrl))

            //homepage url not yet received
            return;

        Browser.Open(HomepageUrl);
    }

    private void OnLoginOkClicked()
    {
        var username = LoginForm.NameField.Text;
        var password = LoginForm.PasswordField.Text;

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            LoginForm.ShowError("Name and password are required.");

            return;
        }

        //a login sent with no connection gets no answer, so the form would wait with no way out
        if (Game.Connection.State != ConnectionState.Login)
        {
            LoginForm.ShowError(NotConnectedMessage);

            return;
        }

        LoginForm.SetBusy(true);

        //logging in...
        WorldState.PlayerName = username;

        Game.Connection.Login(username, password);
    }

    private void OnLoginCancelClicked() => CloseForm(LoginForm);

    //closes a form. A form that waits for the server also ends its wait
    private void CloseForm(LobbyFormPanel form)
    {
        var waited = form.IsBusy;
        form.Hide();

        if (waited)
            AbandonWait();
    }

    //a form that waits longer than WAIT_TIMEOUT_MS stays open with an error, and its wait ends
    private void StopWaitsThatTimedOut()
    {
        foreach (var form in (LobbyFormPanel[]) [Creator, PasswordForm, LoginForm])
            if (form.IsBusy && (form.BusyMs >= WAIT_TIMEOUT_MS))
            {
                NoticeDebugLog.Write($"lobby: {form.Name} waited {form.BusyMs:F0} ms with no answer");
                form.SetBusy(false);
                form.ShowError(NO_ANSWER);
                AbandonWait();
            }
    }

    //ends a wait for the server. The server does not see a cancel, so its answer would still arrive and go to the next
    //form that waits (BRIG-117). The screen closes the connection and connects to the lobby again, so that the answer
    //does not arrive. A login that reached the world keeps its connection: the screen switches to the world
    private void AbandonWait()
    {
        AwaitingCharFinalize = false;

        if (PendingWorldSwitch || (Game.Connection.State == ConnectionState.World))
            return;

        Game.Connection.Disconnect();
        BeginLobbyConnect();
    }

    private void OnLobbyLoginPopupMessageOk() => LobbyLoginPopupMessage.Hide();

    //── once-per-process alert that the audio libraries are missing ──
    private void MaybeAddNoSoundAlert()
    {
        if (NoSoundAlertAdded || !Game.SoundSystem.MixerLibraryMissing)
            return;

        NoSoundAlertAdded = true;
        Menu.Alerts.Add(MenuAlerts.NoSound(OperatingSystem.IsLinux()));
    }

    //── once-per-process alert that the assets folder could not be listed ──
    private void MaybeAddAssetsAlert()
    {
        if (AssetsAlertAdded || AssetPackRegistry.UnlistableFolder is not { } folder)
            return;

        AssetsAlertAdded = true;
        Menu.Alerts.Add(MenuAlerts.AssetsUnlistable(folder));
    }

    //── legacy character-data import offer, until the player decides ──

    private void MaybeAddLegacyImportAlert()
    {
        //ask only until the player decides, and only when there is legacy per-character config to import
        if (ClientSettings.LegacyCharacterImport != ClientSettings.MigrationUndecided)
            return;

        if (!DataContext.LocalPlayerSettings.HasLegacyCharacterData())
        {
            //nothing to import — record the decision, so that the next start does not look or ask again
            SetMigrationDecision(ClientSettings.MigrationSkip);

            return;
        }

        //the alert removes itself when the player picks an action; with no answer it shows again at the next start
        Menu.Alerts.Add(
            MenuAlerts.LegacyImport(
                static () => SetMigrationDecision(ClientSettings.MigrationImport),
                static () => SetMigrationDecision(ClientSettings.MigrationSkip)));
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
        Menu.Alerts.Remove(MenuAlerts.OFFLINE_ID);
        Menu.Alerts.Remove(MenuAlerts.GREETING_UNREADABLE_ID);
        Menu.CoverUntilGreeting();

        //a new connection needs the greeting, or the cached greeting, again before the connection buttons are enabled
        LobbyReady = false;

        await Game.Connection.ConnectToLobbyAsync(
            GlobalSettings.LobbyHost,
            GlobalSettings.LobbyPort,
            DataContext.ClientVersion);
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

                //the greeting (OnLoginNotice) makes the lobby ready
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
                ConnectionLost = true;

                break;

            case ConnectionState.Disconnected:
                Connecting = false;
                ConnectionLost = true;

                break;
        }
    }

    private void OnConnectionError(string error)
    {
        FollowingRedirect = false;
        Connecting = false;
        ConnectionLost = true;
    }

    //with no connection, the menu disables the connection buttons and shows the offline alert with Reconnect. The
    //greeting and the server list need the connection, so they close. Runs on the game loop, from Update
    private void ShowOffline()
    {
        if (Game.Connection.State != ConnectionState.Disconnected)
            return;

        LobbyReady = false;
        Greeting.Hide();
        ServerListPending = false;
        ServerSelectControl.Visible = false;
        Menu.SetCovered(false);
        Menu.Alerts.Add(MenuAlerts.Offline(BeginLobbyConnect));
    }

    //a form that waits for the server cannot get its answer without the connection. Runs on the game loop, from Update
    private void StopWaitsOnConnectionLoss()
    {
        AwaitingCharFinalize = false;

        foreach (var form in (LobbyFormPanel[]) [Creator, PasswordForm, LoginForm])
            if (form.IsBusy)
            {
                form.SetBusy(false);
                form.ShowError(CONNECTION_LOST);
            }
    }

    private void OnServerTableReceived(IList<ServerEntry> servers)
    {
        ServerList = servers;

        //the 0x56 wire is [count][entries] with no server-list flag (retail-verified); show the picker
        //whenever more than one server is advertised, else auto-select the only one
        if (servers.Count > 1)
        {
            ServerSelectControl.SetServers(servers);

            //the list does not show over a form, for example a form that timed out and connects again
            if (AnyFormOpen)
                ServerListPending = true;
            else
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

    //the answer is for the form that waits; only one form is open at a time
    private void OnLoginMessage(LoginMessagePacket pkt)
    {
        if (Creator.IsBusy)
        {
            HandleCharCreateMessage(pkt);

            return;
        }

        if (PasswordForm.IsBusy)
        {
            HandlePasswordChangeMessage(pkt);

            return;
        }

        if (!LoginForm.IsBusy)
        {
            NoticeDebugLog.Write($"login message 0x{pkt.Type:X2} with no form that waits — \"{pkt.Message}\"");

            return;
        }

        if ((LoginMessageType)pkt.Type == LoginMessageType.Confirm)

            //login accepted. waiting for redirect...
            return;

        //login failed — the form stays open for a retry, with the password cleared
        LoginForm.SetBusy(false);
        LoginForm.ClearPassword();
        LoginForm.ShowError(pkt.Message ?? "Login failed.");
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

                Game.Connection.CreateCharFinalize(Creator.SelectedHairStyle, Creator.SelectedGender, Creator.SelectedHairColor);
            } else
            {
                //finalize confirmed — character created, show popup
                AwaitingCharFinalize = false;
                Creator.Hide();
                LobbyLoginPopupMessage.Show("Character has been created. Choose \"CONTINUE\".");
            }

            return;
        }

        //creation failed — the status line shows the error, and the relevant field clears
        AwaitingCharFinalize = false;

        if (!CharCreateFailure.IsDocumented(pkt.Type))
            NoticeDebugLog.Write($"char-create: undocumented 0x02 result code 0x{pkt.Type:X2} — \"{pkt.Message}\"");

        Creator.SetBusy(false);
        ClearCreatorField(CharCreateFailure.FieldFor(pkt.Type));
        Creator.ShowError(pkt.Message ?? "Character creation failed.");
    }

    private void ClearCreatorField(CharCreateFailureField field)
    {
        switch (field)
        {
            case CharCreateFailureField.Name:
                Creator.ClearName();

                break;
            case CharCreateFailureField.Password:
                Creator.ClearPasswords();

                break;
            case CharCreateFailureField.None:
                break;
        }
    }

    private void HandlePasswordChangeMessage(LoginMessagePacket pkt)
    {
        if ((LoginMessageType)pkt.Type == LoginMessageType.Confirm)
        {
            PasswordForm.Hide();
            LobbyLoginPopupMessage.Show("Password has been changed.");

            return;
        }

        PasswordForm.SetBusy(false);
        PasswordForm.ShowError(pkt.Message ?? "Password change failed.");
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
            LobbyReady = true;

            return;
        }

        if (!isFullResponse)
        {
            //checksum-only probe — request full notice if we don't have a cached match
            if (CachedNoticeCheckSum.HasValue && (CachedNoticeCheckSum.Value == checkSum))
            {
                //already accepted this notice, skip display and enable buttons
                NoticeDebugLog.Write("  checksum cache hit, enabling buttons");
                LobbyReady = true;

                return;
            }

            NoticeDebugLog.Write("  probe received, calling RequestNotice()");
            Game.Connection.RequestNotice();

            return;
        }

        //full response — decompress and display. A greeting with no text, or one that does not decompress, has nothing to
        //accept, so the lobby is ready without it. A zlib stream is at least 7 bytes: a 2-byte header, a deflate body and
        //a 4-byte checksum
        if (data is null || (data.Length < 7))
        {
            NoticeDebugLog.Write($"  full response with {data?.Length ?? 0} bytes, so no greeting to show");
            LobbyReady = true;

            return;
        }

        string noticeText;
        try
        {
            noticeText = DecompressNotice(data);
            NoticeDebugLog.Write($"  decompressed ok, text length={noticeText.Length}");
        }
        catch (InvalidDataException ex)
        {
            NoticeDebugLog.Write($"  !!! the greeting does not decompress, so no greeting to show: {ex.Message}");
            Menu.Alerts.Add(MenuAlerts.GreetingUnreadable());
            LobbyReady = true;

            return;
        }

        NoticeDebugLog.Write($"  showing the greeting, prior Visible={Greeting.Visible}");
        Greeting.Show(noticeText);
        NoticeDebugLog.Write($"  after Show, Visible={Greeting.Visible}");
    }

    //the greeting hides itself before it raises Accepted
    private void OnLoginAccepted()
    {
        CachedNoticeCheckSum = ShownNoticeCheckSum;
        LobbyReady = true;
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
        ShownNoticeCheckSum = CRC32.Calculate(rawBytes);

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
    ///     at the root level when no focused sub-panel claims keyboard input. A key that a panel on the control stack
    ///     does not handle bubbles up to here, so Enter repeats only while the stack is empty: no form, greeting,
    ///     prompt, About or Story is open.
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
                && Screen.LastClickedButton is { } button
                && (Screen.Game.Dispatcher.ControlStackCount == 0)
                && !Screen.ServerSelectControl.Visible
                && !Screen.Menu.IsWaitingForGreeting)
            {
                button.PerformClick();
                e.Handled = true;

                return;
            }

            //escape — close the server list when it is visible and nothing else claims focus. With no server the lobby
            //cannot go on, so the connection closes, and the offline alert offers Reconnect
            if ((e.Key == Keys.Escape) && Screen.ServerSelectControl.Visible)
            {
                Screen.ServerSelectControl.Visible = false;
                Screen.Game.Connection.Disconnect();
                e.Handled = true;
            }
        }
    }
}