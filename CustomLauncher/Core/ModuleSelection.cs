using CustomLauncher.Shared.Models;

namespace CustomLauncher.Core;

/// <summary>
/// Applies the user's optional-module choices to a server distribution.
/// The manifest is server-owned, so the launcher never writes selection state back into it;
/// the disabled set lives in the user's settings and is applied as a filter at use time.
/// </summary>
public static class ModuleSelection
{
    /// <summary>Modules the user is allowed to turn off.</summary>
    public static bool IsOptional(DistroModule module) => module.Type == ModuleType.OptionalMod;

    /// <summary>
    /// Returns the ids that are effectively off: the explicitly disabled optional modules plus
    /// every descendant of a disabled module. A submodule cannot be installed without its parent.
    /// </summary>
    public static HashSet<string> ResolveDisabled(
        ServerDistribution distribution,
        IEnumerable<string>? disabledIds)
    {
        var disabled = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in disabledIds ?? [])
        {
            var module = distribution.Modules.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, id, StringComparison.Ordinal));
            // Ignore ids that no longer exist, and never honour a request to drop a required module.
            if (module is not null && IsOptional(module)) disabled.Add(id);
        }

        // Walk parents so that disabling a group also disables everything under it.
        // The manifest is cycle-checked by ModuleValidation, so this terminates.
        bool changed;
        do
        {
            changed = false;
            foreach (var module in distribution.Modules)
            {
                if (module.ParentId is null || disabled.Contains(module.Id)) continue;
                if (disabled.Contains(module.ParentId)) changed |= disabled.Add(module.Id);
            }
        } while (changed);

        return disabled;
    }

    /// <summary>Produces a copy of the distribution with the disabled modules removed.</summary>
    public static ServerDistribution Filter(ServerDistribution distribution, IEnumerable<string>? disabledIds)
    {
        var disabled = ResolveDisabled(distribution, disabledIds);
        if (disabled.Count == 0) return distribution;
        return new ServerDistribution
        {
            ServerId = distribution.ServerId,
            Java = distribution.Java,
            DefaultShaderPackId = distribution.DefaultShaderPackId,
            Modules = distribution.Modules.Where(module => !disabled.Contains(module.Id)).ToList(),
        };
    }
}
