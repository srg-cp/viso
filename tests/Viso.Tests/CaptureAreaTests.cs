using System;
using System.Drawing;
using Xunit;

namespace Viso.Tests;

public sealed class CaptureAreaTests
{
    private static readonly Rectangle Monitor = new(-1920, 100, 1920, 1080);

    [Fact]
    public void AreaFollowsMonitorWhenItsDesktopPositionChanges()
    {
        var area = CaptureArea.Create("monitor", Monitor, new Rectangle(-1800, 250, 600, 400));
        Assert.Equal(new Rectangle(120, 150, 600, 400), area.Resolve("monitor", new Rectangle(0, 0, 1920, 1080)));
    }

    [Fact]
    public void DifferentMonitorIsRejected()
    {
        var area = CaptureArea.Create("monitor", Monitor, new Rectangle(-1800, 250, 600, 400));
        Assert.Throws<ArgumentException>(() => area.Resolve("other", Monitor));
    }

    [Fact]
    public void ResolutionChangeRequiresSelectingAnotherArea()
    {
        var area = CaptureArea.Create("monitor", Monitor, new Rectangle(-1800, 250, 600, 400));
        Assert.Throws<ArgumentException>(() => area.Resolve("monitor", new Rectangle(0, 0, 1280, 720)));
    }

    [Theory]
    [InlineData(-1921, 100, 50, 50)]
    [InlineData(-1920, 99, 50, 50)]
    [InlineData(-10, 100, 50, 50)]
    [InlineData(-1900, 1100, 50, 100)]
    [InlineData(-1900, 120, 7, 50)]
    public void InvalidSelectionIsRejected(int x, int y, int width, int height)
        => Assert.Throws<ArgumentException>(() => CaptureArea.Create("monitor", Monitor, new Rectangle(x, y, width, height)));
}
