namespace Tents.Core.Persistence;

/// <summary>What <see cref="LayoutFileStore.Load"/> found in the main layout file.</summary>
public enum LayoutFileStatus
{
    /// <summary>The main file was read successfully.</summary>
    Loaded,

    /// <summary>There is no main file, for example on the first start.</summary>
    NotFound,

    /// <summary>The main file could not be read as a layout. It was moved aside (see <see cref="LayoutLoadResult.PreservedPath"/>).</summary>
    Corrupt,

    /// <summary>The main file was written by a newer Tents. It was moved aside, untouched (see <see cref="LayoutLoadResult.PreservedPath"/>).</summary>
    NewerSchemaVersion,
}

/// <summary>Where the returned layouts came from.</summary>
public enum LayoutSource
{
    /// <summary>The main layout file.</summary>
    MainFile,

    /// <summary>The <c>.bak</c> copy, because the main file was missing, corrupt or too new.</summary>
    Backup,

    /// <summary>Nothing usable was found; <see cref="LayoutLoadResult.Layouts"/> is <see cref="LayoutSet.Empty"/>.</summary>
    None,
}

/// <summary>The outcome of loading the layout file. Loading never throws for a bad file; it reports it here.</summary>
/// <param name="Layouts">The layouts to start with. <see cref="LayoutSet.Empty"/> when <paramref name="Source"/> is <see cref="LayoutSource.None"/>.</param>
/// <param name="Status">What was found in the main file.</param>
/// <param name="Source">Where <paramref name="Layouts"/> came from.</param>
/// <param name="PreservedPath">Where a corrupt or newer main file was moved to, so the user can recover it.</param>
/// <param name="Detail">A short reason when something went wrong. Never contains item ids.</param>
public sealed record LayoutLoadResult(
    LayoutSet Layouts,
    LayoutFileStatus Status,
    LayoutSource Source,
    string? PreservedPath = null,
    string? Detail = null);
