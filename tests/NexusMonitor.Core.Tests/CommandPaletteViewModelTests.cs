using FluentAssertions;
using NexusMonitor.Core.Models;
using Xunit;

namespace NexusMonitor.Core.Tests;

public class CommandPaletteItemTests
{
    [Fact]
    public void Constructor_SetsAllProperties()
    {
        var item = new CommandPaletteItem("Dashboard", "\uF119", "Navigate", () => { });

        item.Label.Should().Be("Dashboard");
        item.Icon.Should().Be("\uF119");
        item.Category.Should().Be("Navigate");
        item.StateLabel.Should().BeNull();
        item.IsSelected.Should().BeFalse();
    }

    [Fact]
    public void Constructor_WithStateLabel_SetsStateLabel()
    {
        var item = new CommandPaletteItem("Gaming Mode", "\uF451", "Toggle", () => { }, "ON");
        item.StateLabel.Should().Be("ON");
    }

    [Fact]
    public void Execute_RunsAction()
    {
        bool executed = false;
        var item = new CommandPaletteItem("Test", "", "Navigate", () => executed = true);
        item.Execute();
        executed.Should().BeTrue();
    }

    [Fact]
    public void IsSelected_PropertyChanged_Fires()
    {
        var item = new CommandPaletteItem("Test", "", "Navigate", () => { });
        var fired = false;
        item.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(item.IsSelected)) fired = true; };

        item.IsSelected = true;

        fired.Should().BeTrue();
        item.IsSelected.Should().BeTrue();
    }

    [Fact]
    public void StateLabel_PropertyChanged_Fires()
    {
        var item = new CommandPaletteItem("Gaming Mode", "", "Toggle", () => { }, "OFF");
        var fired = false;
        item.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(item.StateLabel)) fired = true; };

        item.StateLabel = "ON";

        fired.Should().BeTrue();
        item.StateLabel.Should().Be("ON");
    }
}
