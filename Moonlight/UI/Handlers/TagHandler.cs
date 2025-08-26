using Moonlight.Services.ServerConfiguration;

namespace Moonlight.UI.Handlers;

public class TagHandler
{
    public const string CustomAllTag = "Moonlight_All";
    public const string CustomOfflineTag = "Moonlight_Offline";
    public const string CustomOfflineSyncshellTag = "Moonlight_OfflineSyncshell";
    public const string CustomOnlineTag = "Moonlight_Online";
    public const string CustomUnpairedTag = "Moonlight_Unpaired";
    public const string CustomVisibleTag = "Moonlight_Visible";
    private readonly ServerConfigurationManager _serverConfigurationManager;

    public TagHandler(ServerConfigurationManager serverConfigurationManager)
    {
        _serverConfigurationManager = serverConfigurationManager;
    }

    public void AddTag(string tag)
    {
        _serverConfigurationManager.AddTag(tag);
    }

    public void AddTagToPairedUid(string uid, string tagName)
    {
        _serverConfigurationManager.AddTagForUid(new Guid(uid), tagName);
    }

    public List<string> GetAllTagsSorted()
    {
        return
        [
            .. _serverConfigurationManager.GetServerAvailablePairTags()
                        .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
,
        ];
    }

    public HashSet<string> GetOtherUidsForTag(string tag)
    {
        var uids = _serverConfigurationManager.GetUidsForTag(tag);
        HashSet<string> returnHashSet = new HashSet<string>();
        foreach (var uid in uids)
        {
            returnHashSet.Add(uid.ToString());
        }

        return returnHashSet;
    }

    public bool HasAnyTag(string uid)
    {
        return _serverConfigurationManager.HasTags(new Guid(uid));
    }

    public bool HasTag(string uid, string tagName)
    {
        return _serverConfigurationManager.ContainsTag(new Guid(uid), tagName);
    }

    /// <summary>
    /// Is this tag opened in the paired clients UI?
    /// </summary>
    /// <param name="tag">the tag</param>
    /// <returns>open true/false</returns>
    public bool IsTagOpen(string tag)
    {
        return _serverConfigurationManager.ContainsOpenPairTag(tag);
    }

    public void RemoveTag(string tag)
    {
        _serverConfigurationManager.RemoveTag(tag);
    }

    public void RemoveTagFromPairedUid(string uid, string tagName)
    {
        _serverConfigurationManager.RemoveTagForUid(new Guid(uid), tagName);
    }

    public void SetTagOpen(string tag, bool open)
    {
        if (open)
        {
            _serverConfigurationManager.AddOpenPairTag(tag);
        }
        else
        {
            _serverConfigurationManager.RemoveOpenPairTag(tag);
        }
    }
}