using Microsoft.Extensions.Logging;
using Moonlight.FileCache;
using Moonlight.Services.Mediator;
using Moonlight.WebAPI.Files;

namespace Moonlight.PlayerData.Factories;

public class FileDownloadManagerFactory
{
    private readonly FileCacheManager _fileCacheManager;
    private readonly FileCompactor _fileCompactor;
    private readonly FileTransferOrchestrator _fileTransferOrchestrator;
    private readonly ILoggerFactory _loggerFactory;
    private readonly MoonlightMediator _moonlightMediator;

    public FileDownloadManagerFactory(ILoggerFactory loggerFactory, MoonlightMediator moonlightMediator, FileTransferOrchestrator fileTransferOrchestrator,
        FileCacheManager fileCacheManager, FileCompactor fileCompactor)
    {
        _loggerFactory = loggerFactory;
        _moonlightMediator = moonlightMediator;
        _fileTransferOrchestrator = fileTransferOrchestrator;
        _fileCacheManager = fileCacheManager;
        _fileCompactor = fileCompactor;
    }

    public FileDownloadManager Create()
    {
        return new FileDownloadManager(_loggerFactory.CreateLogger<FileDownloadManager>(), _moonlightMediator, _fileTransferOrchestrator, _fileCacheManager, _fileCompactor);
    }
}