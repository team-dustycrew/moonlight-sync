using Moonlight.API.Data;
using Moonlight.FileCache;
using Moonlight.Services.CharaData.Models;

namespace Moonlight.Services.CharaData;

public sealed class MoonlightCharaFileDataFactory
{
    private readonly FileCacheManager _fileCacheManager;

    public MoonlightCharaFileDataFactory(FileCacheManager fileCacheManager)
    {
        _fileCacheManager = fileCacheManager;
    }

    public MoonlightCharaFileData Create(string description, CharacterData characterCacheDto)
    {
        return new MoonlightCharaFileData(_fileCacheManager, description, characterCacheDto);
    }
}