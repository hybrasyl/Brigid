using Xunit;

namespace Brigid.Tests;

// Every test collection is defined here (BRIG-111). A test class that changes static state joins the collection of that
// state with [Collection("Name")]. DisableParallelization keeps a collection from running beside any other test class.

// AssetPackRegistry is static: RegisterForTest and ResetForTest change it.
[CollectionDefinition("AssetPackRegistry", DisableParallelization = true)]
public sealed class AssetPackRegistryCollection;

// DebugOverlay is a static class, so tests that change its state run serially.
[CollectionDefinition("DebugOverlay", DisableParallelization = true)]
public sealed class DebugOverlayCollection;

// DataDiagnostics.Log is static, so the tests that replace it run alone.
[CollectionDefinition("DataDiagnostics", DisableParallelization = true)]
public sealed class DataDiagnosticsCollection;

// The font tests share the process-global static FontEngine/FontStashSharp FontSystem, which is single-threaded by
// design (the game drives it from the draw thread only). Serialize them into one non-parallel collection so concurrent
// test classes don't race the shared instance.
[CollectionDefinition("FontEngine", DisableParallelization = true)]
public sealed class FontEngineCollection;

// UITextBox keeps a process-global static FocusedTextBox, and focusing one box unfocuses any other. Test classes that
// construct focused text boxes must therefore run serially, not in parallel, or they steal focus from each other's
// in-flight boxes. This collection groups all such classes so they never overlap.
[CollectionDefinition("UITextBoxFocus", DisableParallelization = true)]
public sealed class UITextBoxFocusCollection;

// WorldState is static, so the tests that change it run alone.
[CollectionDefinition("WorldState", DisableParallelization = true)]
public sealed class WorldStateCollection;
