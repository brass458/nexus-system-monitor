using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NexusMonitor.UI.Views;

public partial class AffinityDialog : Window
{
    public AffinityDialogViewModel ViewModel { get; }

    public AffinityDialog(string processName, long currentMask, long systemMask)
    {
        ViewModel = new AffinityDialogViewModel(processName, currentMask, systemMask);
        DataContext = ViewModel;
        InitializeComponent();
    }

    // parameterless constructor for XAML designer
    public AffinityDialog() : this("Process", 0xFF, 0xFF) { }

    private void OnSelectAll(object? sender, RoutedEventArgs e)
    {
        foreach (var cpu in ViewModel.Cpus) cpu.IsSelected = true;
    }

    private void OnSelectNone(object? sender, RoutedEventArgs e)
    {
        foreach (var cpu in ViewModel.Cpus) cpu.IsSelected = false;
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        Close(ViewModel.GetAffinityMask());
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}

public partial class AffinityDialogViewModel : ObservableObject
{
    public string ProcessName { get; }
    public ObservableCollection<CpuItem> Cpus { get; } = [];

    public AffinityDialogViewModel(string processName, long currentMask, long systemMask)
    {
        ProcessName = processName;

        int logicalCpus = 0;
        for (int i = 0; i < 64; i++)
        {
            if ((systemMask & (1L << i)) != 0)
                logicalCpus = i + 1;
        }

        for (int i = 0; i < logicalCpus; i++)
        {
            bool enabled = (systemMask & (1L << i)) != 0;
            bool selected = (currentMask & (1L << i)) != 0;
            Cpus.Add(new CpuItem(i, $"CPU {i}", enabled && selected));
        }
    }

    public long GetAffinityMask()
    {
        long mask = 0;
        foreach (var cpu in Cpus)
            if (cpu.IsSelected) mask |= 1L << cpu.Index;
        return mask;
    }
}

public partial class CpuItem : ObservableObject
{
    public int Index { get; }
    public string Label { get; }
    [ObservableProperty] private bool _isSelected;

    public CpuItem(int index, string label, bool isSelected)
    {
        Index = index;
        Label = label;
        _isSelected = isSelected;
    }
}
