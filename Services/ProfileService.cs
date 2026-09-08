using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ECCR.Models;

namespace ECCR.Services;

/// <summary>
/// Owns profile load/save/CRUD - extracted out of <see cref="ECCR.ViewModels.MainWindowViewModel"/>,
/// which previously did all of this inline. This service owns file I/O, the profile name list,
/// and each profile's stable <see cref="UserProfile.Id"/>; it does not own the live mapping
/// state itself (<c>Mappings</c>, LED, wheel/gamepad settings) since that still lives on
/// <see cref="ECCR.ViewModels.MainWindowViewModel"/> and needs private members there
/// (<c>AddUncategorizedMapping</c>, etc.) to snapshot/restore - hence the two delegates taken
/// in the constructor, the same delegate-injection shape already used for
/// <see cref="ECCR.ViewModels.HidHideViewModel"/>.
/// </summary>
public class ProfileService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _profilesDirectory;
    private readonly Func<string, Guid, UserProfile> _captureCurrentStateAsProfile;
    private readonly Action<UserProfile?> _applyLoadedProfileToLiveState;
    private readonly Dictionary<string, Guid> _nameToId = new();
    private readonly object _writeLock = new();

    public ObservableCollection<string> ProfileNames { get; } = new();

    /// <summary>True while a profile load is in progress - mirrors the old <c>_isLoadingProfile</c> guard so mutations triggered by applying loaded state don't re-trigger an autosave mid-load.</summary>
    public bool IsLoading { get; private set; }

    /// <summary>Fired after a profile is deleted, so <see cref="GameLibraryService"/> can unbind any games pointing at it.</summary>
    public event Action<Guid>? ProfileDeleted;

    /// <summary>Design-time constructor. Never touches disk.</summary>
    public ProfileService() : this(string.Empty, (_, _) => new UserProfile(), _ => { })
    {
    }

    public ProfileService(string profilesDirectory, Func<string, Guid, UserProfile> captureCurrentStateAsProfile, Action<UserProfile?> applyLoadedProfileToLiveState)
    {
        _profilesDirectory = profilesDirectory;
        _captureCurrentStateAsProfile = captureCurrentStateAsProfile;
        _applyLoadedProfileToLiveState = applyLoadedProfileToLiveState;
    }

    public Guid? GetProfileId(string profileName) => _nameToId.TryGetValue(profileName, out var id) ? id : null;

    public string? GetProfileName(Guid profileId)
    {
        foreach (var kv in _nameToId)
        {
            if (kv.Value == profileId) return kv.Key;
        }
        return null;
    }

    public void LoadProfileListFromDisk()
    {
        ProfileNames.Clear();
        _nameToId.Clear();

        foreach (var file in Directory.GetFiles(_profilesDirectory, "*.json"))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            _nameToId[name] = ReadOrAssignProfileId(file);
            ProfileNames.Add(name);
        }

        if (ProfileNames.Count == 0)
        {
            var id = Guid.NewGuid();
            _nameToId["Default"] = id;
            ProfileNames.Add("Default");
            WriteProfile("Default", _captureCurrentStateAsProfile("Default", id));
        }
    }

    /// <summary>Reads a profile file's Id, migrating (assigning + rewriting) it in place if the file predates the Id field.</summary>
    private Guid ReadOrAssignProfileId(string filePath)
    {
        try
        {
            var profile = JsonSerializer.Deserialize<UserProfile>(File.ReadAllText(filePath));
            if (profile == null) return Guid.NewGuid();

            if (profile.Id == Guid.Empty)
            {
                profile.Id = Guid.NewGuid();
                File.WriteAllText(filePath, JsonSerializer.Serialize(profile, JsonOptions));
            }
            return profile.Id;
        }
        catch
        {
            return Guid.NewGuid();
        }
    }

    /// <summary>Adds a name to the tracked list (with a fresh Id) if not already present - used when a not-yet-saved profile name needs to exist in <see cref="ProfileNames"/> before it's selected/saved.</summary>
    public void RegisterName(string name)
    {
        if (_nameToId.ContainsKey(name)) return;
        _nameToId[name] = Guid.NewGuid();
        if (!ProfileNames.Contains(name)) ProfileNames.Add(name);
    }

    /// <summary>
    /// Reads a profile file's mapping summary for the Profiles page's card gallery, without
    /// touching live state the way <see cref="SelectProfile"/>'s apply delegate does - just a
    /// deserialize-and-classify peek. Returns a zeroed/false summary for a profile with no file
    /// yet (e.g. a brand-new, never-saved name).
    /// </summary>
    public (int MappingCount, bool HasGamepadMappings, bool HasWheelMappings) PeekProfileSummary(string profileName)
    {
        string path = Path.Combine(_profilesDirectory, $"{profileName}.json");
        if (!File.Exists(path)) return (0, false, false);

        try
        {
            var profile = JsonSerializer.Deserialize<UserProfile>(File.ReadAllText(path));
            if (profile?.Mappings == null) return (0, false, false);

            bool hasGamepad = false;
            bool hasWheel = false;
            foreach (var mapping in profile.Mappings)
            {
                var category = DevicePresetService.DetectCategory(mapping.SourceDeviceName ?? string.Empty);
                if (DevicePresetService.IsWheelOrPedalCategory(category)) hasWheel = true;
                else hasGamepad = true;
            }

            return (profile.Mappings.Count, hasGamepad, hasWheel);
        }
        catch
        {
            return (0, false, false);
        }
    }

    public void SelectProfile(string profileName)
    {
        IsLoading = true;
        try
        {
            string path = Path.Combine(_profilesDirectory, $"{profileName}.json");
            UserProfile? profile = null;
            if (File.Exists(path))
            {
                try { profile = JsonSerializer.Deserialize<UserProfile>(File.ReadAllText(path)); }
                catch { profile = null; }
            }
            _applyLoadedProfileToLiveState(profile);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void SaveCurrentProfile(string profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName)) return;
        var id = GetProfileId(profileName) ?? Guid.NewGuid();
        _nameToId[profileName] = id;
        WriteProfile(profileName, _captureCurrentStateAsProfile(profileName, id));
    }

    /// <summary>
    /// Serializes <paramref name="profile"/> - a fresh snapshot already captured on the calling
    /// thread by <see cref="_captureCurrentStateAsProfile"/>, never the live mutable state - and
    /// writes it out on a background thread, since <see cref="SaveCurrentProfile"/> runs on the UI
    /// thread on essentially every mapping edit via <c>MainWindowViewModel.AutoSaveCurrentProfile</c>.
    /// <see cref="_writeLock"/> serializes the actual writes so two rapid autosaves can't interleave.
    /// </summary>
    private void WriteProfile(string profileName, UserProfile profile)
    {
        string path = Path.Combine(_profilesDirectory, $"{profileName}.json");
        Task.Run(() =>
        {
            try
            {
                string json = JsonSerializer.Serialize(profile, JsonOptions);
                lock (_writeLock) { File.WriteAllText(path, json); }
            }
            catch { }
        });
    }

    /// <summary>Creates a brand-new, blank profile (empty mappings, default LED/wheel/gamepad settings) - distinct from the legacy "save the app's current live state under a new name" behavior of <c>MainWindowViewModel.ConfirmCreateProfile</c> (preserved as-is elsewhere), since the Profiles page creates profiles to bind to a specific game independent of whatever happens to be loaded right now.</summary>
    public void CreateProfile(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        string cleanName = name.Trim();
        if (ProfileNames.Contains(cleanName)) return;

        var id = Guid.NewGuid();
        WriteProfile(cleanName, new UserProfile { ProfileName = cleanName, Id = id });
        _nameToId[cleanName] = id;
        ProfileNames.Add(cleanName);
    }

    public void DeleteProfile(string name)
    {
        if (ProfileNames.Count <= 1) return;

        Guid? id = GetProfileId(name);
        string path = Path.Combine(_profilesDirectory, $"{name}.json");
        if (File.Exists(path)) File.Delete(path);

        ProfileNames.Remove(name);
        _nameToId.Remove(name);

        if (id.HasValue) ProfileDeleted?.Invoke(id.Value);
    }

    /// <summary>New: no equivalent existed before this overhaul.</summary>
    public void RenameProfile(string oldName, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName) || oldName == newName || !ProfileNames.Contains(oldName)) return;
        string cleanNew = newName.Trim();
        if (ProfileNames.Contains(cleanNew)) return;

        string oldPath = Path.Combine(_profilesDirectory, $"{oldName}.json");
        if (!File.Exists(oldPath)) return;

        try
        {
            var profile = JsonSerializer.Deserialize<UserProfile>(File.ReadAllText(oldPath));
            if (profile == null) return;

            profile.ProfileName = cleanNew;
            File.WriteAllText(Path.Combine(_profilesDirectory, $"{cleanNew}.json"), JsonSerializer.Serialize(profile, JsonOptions));
            File.Delete(oldPath);

            var id = _nameToId.TryGetValue(oldName, out var existingId) ? existingId : profile.Id;
            _nameToId.Remove(oldName);
            _nameToId[cleanNew] = id;

            int idx = ProfileNames.IndexOf(oldName);
            if (idx >= 0) ProfileNames[idx] = cleanNew;
        }
        catch { }
    }

    /// <summary>New: no equivalent existed before this overhaul. The clone always gets a fresh Id so it never collides with the source's game bindings.</summary>
    public void DuplicateProfile(string sourceName, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName)) return;
        string cleanNew = newName.Trim();
        if (ProfileNames.Contains(cleanNew)) return;

        string sourcePath = Path.Combine(_profilesDirectory, $"{sourceName}.json");
        if (!File.Exists(sourcePath)) return;

        try
        {
            var profile = JsonSerializer.Deserialize<UserProfile>(File.ReadAllText(sourcePath));
            if (profile == null) return;

            profile.Id = Guid.NewGuid();
            profile.ProfileName = cleanNew;
            File.WriteAllText(Path.Combine(_profilesDirectory, $"{cleanNew}.json"), JsonSerializer.Serialize(profile, JsonOptions));

            _nameToId[cleanNew] = profile.Id;
            ProfileNames.Add(cleanNew);
        }
        catch { }
    }
}
