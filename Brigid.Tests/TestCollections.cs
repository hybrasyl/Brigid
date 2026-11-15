using Xunit;

namespace Brigid.Tests;

// Every test collection is defined here (BRIG-111). A test class that changes static state joins the collection of that
// state with [Collection("Name")]. A collection with DisableParallelization runs alone; the others run beside other
// test classes, and only their own classes run in series.

// AssetPackRegistry is static: RegisterForTest and ResetForTest change it. The tests that change it run alone.
[CollectionDefinition("AssetPackRegistry", DisableParallelization = true)]
public sealed class AssetPackRegistryCollection;

// DataDiagnostics.Log is static. The tests that replace it run alone.
[CollectionDefinition("DataDiagnostics", DisableParallelization = true)]
public sealed class DataDiagnosticsCollection;

// DebugOverlay is static. The tests that change it run alone.
[CollectionDefinition("DebugOverlay", DisableParallelization = true)]
public sealed class DebugOverlayCollection;

// FontEngine is static, and its FontStashSharp font system takes one thread at a time (the game draws text from one
// thread). The tests that use it run alone.
[CollectionDefinition("FontEngine", DisableParallelization = true)]
public sealed class FontEngineCollection;

// The GameClient connect timeouts (GreetingTimeout, UpgradeTimeout) are static. The tests that change them run in
// series with each other; the other tests do not read them.
[CollectionDefinition("GameClientTimeouts")]
public sealed class GameClientTimeoutsCollection;

// UITextBox keeps one static FocusedTextBox, and a focused box removes the focus from any other.
// InputDispatcher.Instance is static too, and an open DropDown writes to it. The tests that focus a text box or use the
// dispatcher run alone.
[CollectionDefinition("UITextBoxFocus", DisableParallelization = true)]
public sealed class UITextBoxFocusCollection;

// The GameClient transport tests use a test timeout, which xunit supports only when the tests run alone: a teardown that
// waits for itself deadlocks, and the timeout fails that test in place of a hung run.
[CollectionDefinition("GameClientTransport", DisableParallelization = true)]
public sealed class GameClientTransportCollection;

// The settings files (Keybinds, LauncherConfig, CertificateTrustStore) keep static state that their Load and Save change.
// The tests that load or save them run in series with each other; the other tests do not load them.
[CollectionDefinition("SettingsFiles")]
public sealed class SettingsFilesCollection;

// WorldState is static. The tests that change it run alone.
[CollectionDefinition("WorldState", DisableParallelization = true)]
public sealed class WorldStateCollection;
