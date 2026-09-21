using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Build.Framework;
using Scarlet.Sass.Core;

namespace Scarlet.Sass.MSBuild;

/// <summary>
/// Builds <see cref="SassRuntimePack"/> instances from MSBuild items.
/// </summary>
/// <remarks>
/// This is the only piece of the pack contract that knows about MSBuild, which is why it lives here rather
/// than beside <see cref="SassRuntimePack"/> in Scarlet.Sass.Core - the CLI shares the pack type but has no
/// <see cref="ITaskItem"/> to translate.
/// </remarks>
public static class SassRuntimePackFactory
{
    /// <summary>
    /// Builds the list of packs described by the given MSBuild items, dropping malformed and duplicate entries.
    /// </summary>
    /// <param name="items">The <c>SassRuntimePack</c> items. The sequence, and any entry in it, may be <see langword="null"/>.</param>
    /// <param name="onInvalidItem">Invoked with a human readable reason for every item that had to be dropped.</param>
    /// <returns>The valid, de-duplicated packs in declaration order.</returns>
    public static IReadOnlyList<SassRuntimePack> FromTaskItems(IEnumerable<ITaskItem?>? items, Action<string>? onInvalidItem = null)
    {
        if (items is null)
        {
            return Array.Empty<SassRuntimePack>();
        }

        var packs = new List<SassRuntimePack>();

        foreach (var item in items)
        {
            if (item is null)
            {
                continue;
            }

            var id = item.ItemSpec?.Trim();
            if (string.IsNullOrEmpty(id))
            {
                onInvalidItem?.Invoke($"A {SassRuntimePack.ItemName} item without an identity was ignored.");
                continue;
            }

            var rid = item.GetMetadata(SassRuntimePack.RidMetadataName)?.Trim();
            if (string.IsNullOrEmpty(rid))
            {
                onInvalidItem?.Invoke($"{SassRuntimePack.ItemName} \"{id}\" was ignored because it does not set the \"{SassRuntimePack.RidMetadataName}\" metadata.");
                continue;
            }

            var runtimesPath = item.GetMetadata(SassRuntimePack.RuntimesPathMetadataName)?.Trim();
            if (string.IsNullOrEmpty(runtimesPath))
            {
                onInvalidItem?.Invoke($"{SassRuntimePack.ItemName} \"{id}\" was ignored because it does not set the \"{SassRuntimePack.RuntimesPathMetadataName}\" metadata.");
                continue;
            }

            var variant = item.GetMetadata(SassRuntimePack.VariantMetadataName);
            var priorityText = item.GetMetadata(SassRuntimePack.PriorityMetadataName)?.Trim();
            var priority = 0;

            if (!string.IsNullOrEmpty(priorityText)
                && !int.TryParse(priorityText, NumberStyles.Integer, CultureInfo.InvariantCulture, out priority))
            {
                onInvalidItem?.Invoke($"{SassRuntimePack.ItemName} \"{id}\" has a non-numeric \"{SassRuntimePack.PriorityMetadataName}\" metadata (\"{priorityText}\"); 0 was used instead.");
                priority = 0;
            }

            packs.Add(new SassRuntimePack(id!, rid!, runtimesPath!, variant, priority));
        }

        return SassRuntimePack.Deduplicate(packs);
    }
}
