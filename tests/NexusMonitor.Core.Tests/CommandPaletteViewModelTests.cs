using FluentAssertions;
using NexusMonitor.Core.Models;
using NexusMonitor.Core.ViewModels;
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

public class CommandPaletteViewModelTests
{
    private static CommandPaletteViewModel CreateVm(int itemCount = 3)
    {
        var items = Enumerable.Range(0, itemCount)
            .Select(i => new CommandPaletteItem($"Item {i}", "", "Navigate", () => { }))
            .ToList();
        return new CommandPaletteViewModel(items);
    }

    [Fact]
    public void Open_SetsIsOpenTrue()
    {
        var vm = CreateVm();
        vm.Open();
        vm.IsOpen.Should().BeTrue();
    }

    [Fact]
    public void Close_SetsIsOpenFalse()
    {
        var vm = CreateVm();
        vm.Open();
        vm.Close();
        vm.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void Toggle_WhenClosed_Opens()
    {
        var vm = CreateVm();
        vm.Toggle();
        vm.IsOpen.Should().BeTrue();
    }

    [Fact]
    public void Toggle_WhenOpen_Closes()
    {
        var vm = CreateVm();
        vm.Open();
        vm.Toggle();
        vm.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void Open_ClearsSearchText()
    {
        var vm = CreateVm();
        vm.SearchText = "something";
        vm.Open();
        vm.SearchText.Should().Be(string.Empty);
    }

    [Fact]
    public void Open_PopulatesFilteredItemsWithAllItems()
    {
        var vm = CreateVm(itemCount: 5);
        vm.Open();
        vm.FilteredItems.Should().HaveCount(5);
    }

    [Fact]
    public void Open_ResetsSelectedIndexToZero()
    {
        var vm = CreateVm();
        vm.SelectedIndex = 2;
        vm.Open();
        vm.SelectedIndex.Should().Be(0);
    }

    [Fact]
    public void Filter_EmptyString_ReturnsAllItems()
    {
        var vm = CreateVm(3);
        vm.Open(); // SearchText is "", all items
        vm.FilteredItems.Should().HaveCount(3);
    }

    [Fact]
    public void Filter_ByLabel_ReturnsMatchingItems()
    {
        var items = new[]
        {
            new CommandPaletteItem("Dashboard", "", "Navigate", () => { }),
            new CommandPaletteItem("Network", "", "Navigate", () => { }),
            new CommandPaletteItem("Processes", "", "Navigate", () => { }),
        };
        var vm = new CommandPaletteViewModel(items);
        vm.Open();

        vm.SearchText = "dash";

        vm.FilteredItems.Should().HaveCount(1);
        vm.FilteredItems[0].Label.Should().Be("Dashboard");
    }

    [Fact]
    public void Filter_ByCategory_ReturnsMatchingItems()
    {
        var items = new[]
        {
            new CommandPaletteItem("Dashboard", "", "Navigate", () => { }),
            new CommandPaletteItem("Gaming Mode", "", "Toggle", () => { }),
        };
        var vm = new CommandPaletteViewModel(items);
        vm.Open();

        vm.SearchText = "toggle";

        vm.FilteredItems.Should().HaveCount(1);
        vm.FilteredItems[0].Label.Should().Be("Gaming Mode");
    }

    [Fact]
    public void Filter_CaseInsensitive()
    {
        var items = new[] { new CommandPaletteItem("Dashboard", "", "Navigate", () => { }) };
        var vm = new CommandPaletteViewModel(items);
        vm.Open();

        vm.SearchText = "DASH";

        vm.FilteredItems.Should().HaveCount(1);
    }

    [Fact]
    public void Filter_NoMatch_ReturnsEmpty()
    {
        var items = new[] { new CommandPaletteItem("Dashboard", "", "Navigate", () => { }) };
        var vm = new CommandPaletteViewModel(items);
        vm.Open();

        vm.SearchText = "xyz_no_match";

        vm.FilteredItems.Should().BeEmpty();
    }

    [Fact]
    public void Filter_ResetsSelectedIndexToZero()
    {
        var vm = CreateVm(3);
        vm.Open();
        vm.SelectedIndex = 2;

        vm.SearchText = "Item";

        vm.SelectedIndex.Should().Be(0);
    }

    [Fact]
    public void Filter_PartialMatch_MultipleResults()
    {
        var items = new[]
        {
            new CommandPaletteItem("Dashboard", "", "Navigate", () => { }),
            new CommandPaletteItem("Dark Theme", "", "Theme", () => { }),
            new CommandPaletteItem("Network", "", "Navigate", () => { }),
        };
        var vm = new CommandPaletteViewModel(items);
        vm.Open();

        vm.SearchText = "d";

        vm.FilteredItems.Should().HaveCount(2);
    }
}
