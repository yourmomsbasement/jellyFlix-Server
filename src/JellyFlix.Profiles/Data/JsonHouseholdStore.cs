using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Extensions.Json;
using MediaBrowser.Common.Configuration;

namespace JellyFlix.Profiles.Data;

/// <summary>
/// Stores households as a JSON file in the server data directory.
/// </summary>
public class JsonHouseholdStore : IHouseholdStore
{
    private readonly string _path;

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonHouseholdStore"/> class.
    /// </summary>
    /// <param name="applicationPaths">The application paths.</param>
    public JsonHouseholdStore(IApplicationPaths applicationPaths)
    {
        _path = Path.Combine(applicationPaths.DataPath, "jellyflix-profiles", "households.json");
    }

    /// <inheritdoc />
    public async Task<List<Household>> LoadAsync()
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        var stream = File.OpenRead(_path);
        await using (stream.ConfigureAwait(false))
        {
            return await JsonSerializer.DeserializeAsync<List<Household>>(stream, JsonDefaults.Options).ConfigureAwait(false) ?? [];
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(IReadOnlyList<Household> households)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

        // Write to a temp file first so a crash mid-write can't corrupt the household list.
        var tempPath = _path + ".tmp";
        var stream = File.Create(tempPath);
        await using (stream.ConfigureAwait(false))
        {
            await JsonSerializer.SerializeAsync(stream, households, JsonDefaults.Options).ConfigureAwait(false);
        }

        File.Move(tempPath, _path, true);
    }
}
