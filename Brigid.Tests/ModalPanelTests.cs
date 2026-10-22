#region
using Brigid.Controls.Generic;
using Brigid.Rendering;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     The connection prompts are modal: they gate a connection in progress, so the lobby blocks the input behind them
///     (BRIG-107).
/// </summary>
[Collection("FontEngine")]
public class ModalPanelTests
{
    public ModalPanelTests() => FontEngine.Initialize(0);

    [Fact]
    public void TheConnectionPromptsAreModal()
    {
        Assert.True(new CertificateTrustPromptControl().IsModal);
        Assert.True(new InsecureConnectionPromptControl().IsModal);
    }
}
