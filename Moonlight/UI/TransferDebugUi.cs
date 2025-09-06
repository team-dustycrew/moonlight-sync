using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Windowing;
using Microsoft.Extensions.Logging;
using MoonLight.API.Data.Extensions;
using Moonlight.MoonlightConfiguration;
using Moonlight.PlayerData.Handlers;
using Moonlight.PlayerData.Pairs;
using Moonlight.Services;
using Moonlight.Services.Mediator;
using Moonlight.Services.Events;
using Moonlight.WebAPI;
using Moonlight.WebAPI.Files;
using Moonlight.WebAPI.Files.Models;
using System.Collections.Concurrent;
using System.Numerics;

namespace Moonlight.UI;

public class TransferDebugUi : WindowMediatorSubscriberBase
{
	private readonly ApiController _api;
	private readonly FileTransferOrchestrator _orchestrator;
	private readonly FileUploadManager _uploader;
	private readonly MoonlightConfigService _config;
	private readonly PairManager _pairs;
	private readonly DalamudUtilService _dalamud;
	private readonly ConcurrentDictionary<GameObjectHandler, Dictionary<string, FileDownloadStatus>> _downloads = new();
	private readonly ConcurrentDictionary<GameObjectHandler, bool> _uploadingPlayers = new();
	private readonly ConcurrentQueue<string> _events = new();
	private readonly List<Guid> _readyRequests = new();
	private bool _stickToBottom = true;
	private bool _showEnv;
	private bool _showTransfers = true;
	private bool _showEvents = true;
	private const int MaxEvents = 200;

	public TransferDebugUi(ILogger<TransferDebugUi> logger, MoonlightMediator mediator,
		ApiController apiController, FileTransferOrchestrator orchestrator, FileUploadManager uploader,
		MoonlightConfigService config, PairManager pairs, DalamudUtilService dalamud, PerformanceCollectorService perf)
		: base(logger, mediator, "Moonlight Transfer Debug", perf)
	{
		_api = apiController;
		_orchestrator = orchestrator;
		_uploader = uploader;
		_config = config;
		_pairs = pairs;
		_dalamud = dalamud;

		SizeConstraints = new Window.WindowSizeConstraints()
		{
			MinimumSize = new Vector2(700, 400),
			MaximumSize = new Vector2(4096, 4096),
		};

		Flags |= ImGuiWindowFlags.NoScrollbar;

		Mediator.Subscribe<ConnectedMessage>(this, (msg) =>
		{
			EnqueueEvent($"Connected: Server={msg.Connection.ServerInfo.ShardName}, CDN={msg.Connection.ServerInfo.FileServerAddress}");
		});
		Mediator.Subscribe<DisconnectedMessage>(this, (msg) =>
		{
			EnqueueEvent("Disconnected");
		});
		Mediator.Subscribe<DownloadReadyMessage>(this, (msg) =>
		{
			lock (_readyRequests) { _readyRequests.Add(msg.RequestId); if (_readyRequests.Count > 50) _readyRequests.RemoveAt(0); }
			EnqueueEvent($"DownloadReady: {msg.RequestId}");
		});
		Mediator.Subscribe<DownloadStartedMessage>(this, (msg) =>
		{
			_downloads[msg.DownloadId] = msg.DownloadStatus;
			var totals = msg.DownloadStatus.Sum(s => s.Value.TotalFiles);
			EnqueueEvent($"DownloadStarted: {msg.DownloadId.Name} files={totals}");
		});
		Mediator.Subscribe<DownloadFinishedMessage>(this, (msg) =>
		{
			_downloads.TryRemove(msg.DownloadId, out _);
			EnqueueEvent($"DownloadFinished: {msg.DownloadId.Name}");
		});
		Mediator.Subscribe<DownloadLimitChangedMessage>(this, (msg) =>
		{
			EnqueueEvent($"DownloadLimitChanged");
		});
		Mediator.Subscribe<PlayerUploadingMessage>(this, (msg) =>
		{
			if (msg.IsUploading) _uploadingPlayers[msg.Handler] = true; else _uploadingPlayers.TryRemove(msg.Handler, out _);
		});
		Mediator.Subscribe<EventMessage>(this, (msg) =>
		{
			if (!string.IsNullOrEmpty(msg.Event.UID))
			{
				EnqueueEvent($"{msg.Event.UID}: {msg.Event.Message}");
			}
		});
	}

	protected override void DrawInternal()
	{
		try
		{
			ImGui.TextColored(ImGuiColors.DalamudYellow, "Moonlight Transfer Debug");
			ImGui.Separator();

			if (ImGui.BeginTable("dbg.toggles", 4, ImGuiTableFlags.SizingStretchProp))
			{
				ImGui.TableNextRow();
				ImGui.TableNextColumn(); ImGui.Checkbox("Environment", ref _showEnv);
				ImGui.TableNextColumn(); ImGui.Checkbox("Transfers", ref _showTransfers);
				ImGui.TableNextColumn(); ImGui.Checkbox("Events", ref _showEvents);
				ImGui.TableNextColumn(); ImGui.Checkbox("Stick to bottom", ref _stickToBottom);
				ImGui.EndTable();
			}

			if (_showEnv)
			{
				DrawEnvironment();
				ImGui.Separator();
			}

			if (_showTransfers)
			{
				DrawTransfers();
				ImGui.Separator();
			}

			if (_showEvents)
			{
				DrawEvents();
			}

			DrawPairsSection();
		}
		catch { }
	}

	private void DrawEnvironment()
	{
		ImGui.TextColored(ImGuiColors.HealerGreen, "Connection");
		ImGui.BulletText($"State: {_api.ServerState}");
		ImGui.BulletText($"IsConnected: {_api.IsConnected}");
		ImGui.BulletText($"PublicUserID: {_api.PublicUserID}");
		ImGui.BulletText($"CDN: {_orchestrator.FilesCdnUri ?? null}");

		ImGui.TextColored(ImGuiColors.HealerGreen, "Config");
		ImGui.BulletText($"ParallelDownloads: {_config.Current.ParallelDownloads}");
		ImGui.BulletText($"DownloadLimit: {_config.Current.DownloadSpeedLimitInBytes} {_config.Current.DownloadSpeedType}");
	}

	private void DrawTransfers()
	{
		ImGui.TextColored(ImGuiColors.TankBlue, "Active downloads");
		if (_downloads.Any())
		{
			foreach (var kvp in _downloads.ToList())
			{
				var dlSlot = kvp.Value.Count(c => c.Value.DownloadStatus == DownloadStatus.WaitingForSlot);
				var dlQueue = kvp.Value.Count(c => c.Value.DownloadStatus == DownloadStatus.WaitingForQueue);
				var dlProg = kvp.Value.Count(c => c.Value.DownloadStatus == DownloadStatus.Downloading);
				var dlDecomp = kvp.Value.Count(c => c.Value.DownloadStatus == DownloadStatus.Decompressing);
				var totalFiles = kvp.Value.Sum(c => c.Value.TotalFiles);
				var transferredFiles = kvp.Value.Sum(c => c.Value.TransferredFiles);
				var totalBytes = kvp.Value.Sum(c => c.Value.TotalBytes);
				var transferredBytes = kvp.Value.Sum(c => c.Value.TransferredBytes);
				ImGui.BulletText($"{kvp.Key.Name}: W:{dlSlot} Q:{dlQueue} P:{dlProg} D:{dlDecomp} | {transferredFiles}/{totalFiles} ({UiSharedService.ByteToString(transferredBytes, addSuffix: false)}/{UiSharedService.ByteToString(totalBytes)})");
			}
		}
		else
		{
			ImGui.TextDisabled("No active downloads");
		}

		ImGui.TextColored(ImGuiColors.TankBlue, "Active uploads");
		if (_uploader.CurrentUploads.Any())
		{
			var done = _uploader.CurrentUploads.Count(c => c.IsTransferred);
			var total = _uploader.CurrentUploads.Count;
			var up = _uploader.CurrentUploads.Sum(c => c.Transferred);
			var tot = _uploader.CurrentUploads.Sum(c => c.Total);
			ImGui.BulletText($"Compressing+Uploading {done}/{total} ({UiSharedService.ByteToString(up, addSuffix: false)}/{UiSharedService.ByteToString(tot)})");
		}
		else
		{
			ImGui.TextDisabled("No active uploads");
		}

		if (_uploadingPlayers.Any())
		{
			ImGui.TextColored(ImGuiColors.TankBlue, "Players flagged as uploading");
			foreach (var pl in _uploadingPlayers.Keys.ToList())
			{
				ImGui.BulletText(pl.Name);
			}
		}

		lock (_readyRequests)
		{
			if (_readyRequests.Any())
			{
				ImGui.TextColored(ImGuiColors.TankBlue, "Recent download-ready IDs");
				foreach (var id in _readyRequests.ToList())
				{
					ImGui.BulletText(id.ToString());
				}
			}
		}
	}

	private void DrawEvents()
	{
		ImGui.TextColored(ImGuiColors.DalamudViolet, "Recent events");
		ImGui.BeginChild("dbg.events", new Vector2(0, 200), true);
		foreach (var line in _events.ToList())
		{
			ImGui.TextUnformatted(line);
			if (_stickToBottom) ImGui.SetScrollHereY(1f);
		}
		ImGui.EndChild();
	}

	private void EnqueueEvent(string text)
	{
		_events.Enqueue($"[{DateTime.Now:HH:mm:ss}] {text}");
		while (_events.Count > MaxEvents && _events.TryDequeue(out _)) { }
	}

	private string _pairFilter = string.Empty;
	private bool _onlyVisible = false;
	private void DrawPairsSection()
	{
		ImGui.Separator();
		ImGui.TextColored(ImGuiColors.DalamudWhite, "Pairs");
		ImGui.PushItemWidth(250);
		ImGui.InputText("Filter (alias/uid)", ref _pairFilter, 128);
		ImGui.PopItemWidth();
		ImGui.SameLine();
		ImGui.Checkbox("Only visible", ref _onlyVisible);

		HashSet<Pair> pairs = new();
		foreach (var p in _pairs.DirectPairs) pairs.Add(p);
		foreach (var p in _pairs.PairsWithGroups.Keys) pairs.Add(p);

		var filtered = pairs
			.Where(p => string.IsNullOrEmpty(_pairFilter)
				|| p.UserData.AliasOrUID.Contains(_pairFilter, StringComparison.OrdinalIgnoreCase))
			.Where(p => !_onlyVisible || p.IsVisible)
			.OrderBy(p => p.UserData.AliasOrUID)
			.ToList();

		if (ImGui.BeginTable("pairs.table", 11, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders))
		{
			ImGui.TableSetupColumn("Alias/UID");
			ImGui.TableSetupColumn("Status");
			ImGui.TableSetupColumn("Visible");
			ImGui.TableSetupColumn("Paused");
			ImGui.TableSetupColumn("Indiv");
			ImGui.TableSetupColumn("OwnPerm");
			ImGui.TableSetupColumn("OtherPerm");
			ImGui.TableSetupColumn("Player");
			ImGui.TableSetupColumn("LastData");
			ImGui.TableSetupColumn("Ident");
			ImGui.TableSetupColumn("Resolve");
			ImGui.TableHeadersRow();

			foreach (var p in filtered)
			{
				ImGui.TableNextRow();
				ImGui.TableNextColumn(); ImGui.TextUnformatted(p.UserData.AliasOrUID);
				ImGui.TableNextColumn(); ImGui.TextUnformatted(p.IsOnline ? "Online" : "Offline");
				ImGui.TableNextColumn(); ImGui.TextUnformatted(p.IsVisible ? "Yes" : "No");
				ImGui.TableNextColumn(); ImGui.TextUnformatted(p.IsPaused ? "Yes" : "No");
				ImGui.TableNextColumn(); ImGui.TextUnformatted(p.IndividualPairStatus.ToString());
				ImGui.TableNextColumn(); ImGui.TextUnformatted(PermSummary(p.UserPair.OwnPermissions));
				ImGui.TableNextColumn(); ImGui.TextUnformatted(PermSummary(p.UserPair.OtherPermissions));
				ImGui.TableNextColumn(); ImGui.TextUnformatted(string.IsNullOrEmpty(p.PlayerName) ? "-" : p.PlayerName);
				var lastData = p.LastReceivedCharacterData?.DataHash?.Value ?? "-";
				ImGui.TableNextColumn(); ImGui.TextUnformatted(lastData);
				ImGui.TableNextColumn(); ImGui.TextUnformatted(p.UserPair.User.publicUserID + ":" + (p.IsOnline ? (p.UserPair.User.AliasOrUID ?? "-") : "-"));
				ImGui.TableNextColumn();
				if (!p.IsVisible)
				{
					if (ImGui.SmallButton($"Find##{p.UserData.publicUserID}"))
					{
						var tuple = _dalamud.FindPlayerByNameHash(p.Ident);
						EnqueueEvent($"Try resolve {p.UserData.AliasOrUID}: ident={p.Ident}, found={(tuple.Address != nint.Zero ? tuple.Name : "<none>")}");
					}
				}
				else
				{
					ImGui.TextDisabled("-");
				}
			}
			ImGui.EndTable();
		}
	}

	private static string PermSummary(MoonLight.API.Data.Enum.UserPermissions perm)
	{
		List<string> flags = new();
		if (perm.IsPaused()) flags.Add("Paused");
		if (perm.IsDisableAnimations()) flags.Add("NoAnim");
		if (perm.IsDisableVFX()) flags.Add("NoVFX");
		if (perm.IsDisableSounds()) flags.Add("NoSfx");
		if (perm.HasFlag(MoonLight.API.Data.Enum.UserPermissions.Sticky)) flags.Add("Pref");
		return string.Join(',', flags);
	}
}


