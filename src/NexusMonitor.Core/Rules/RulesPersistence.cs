using NexusMonitor.Core.Services;

namespace NexusMonitor.Core.Rules;

/// <summary>Thin helper for rule CRUD — persistence is handled by SettingsService.</summary>
public sealed class RulesPersistence(SettingsService settings)
{
    public IReadOnlyList<ProcessRule> GetAll() => settings.Current.Rules ?? [];

    public void Add(ProcessRule rule)
    {
        settings.Current.Rules ??= new();
        settings.Current.Rules.Add(rule);
        settings.Save();
    }

    public void Update(ProcessRule rule)
    {
        var list = settings.Current.Rules;
        if (list is null) return;
        var idx = list.FindIndex(r => r.Id == rule.Id);
        if (idx >= 0) { list[idx] = rule; settings.Save(); }
    }

    public void Remove(Guid id)
    {
        settings.Current.Rules?.RemoveAll(r => r.Id == id);
        settings.Save();
    }
}
