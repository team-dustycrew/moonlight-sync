﻿using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Moonlight.API.Data.Extensions;
using Microsoft.Extensions.Logging;
using Moonlight.API.Dto.Group;
using Moonlight.Services;
using Moonlight.Services.Mediator;
using Moonlight.WebAPI;
using System.Numerics;

namespace Moonlight.UI;

public class CreateSyncshellUI : WindowMediatorSubscriberBase
{
    private readonly ApiController _apiController;
    private readonly UiSharedService _uiSharedService;
    private bool _errorGroupCreate;
    private GroupJoinDto? _lastCreatedGroup;

    public CreateSyncshellUI(ILogger<CreateSyncshellUI> logger, MoonlightMediator moonlightMediator, ApiController apiController, UiSharedService uiSharedService,
        PerformanceCollectorService performanceCollectorService)
        : base(logger, moonlightMediator, "Create new Syncshell###MoonlightCreateSyncshell", performanceCollectorService)
    {
        _apiController = apiController;
        _uiSharedService = uiSharedService;
        SizeConstraints = new()
        {
            MinimumSize = new(550, 330),
            MaximumSize = new(550, 330)
        };

        Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse;

        Mediator.Subscribe<DisconnectedMessage>(this, (_) => IsOpen = false);
    }

    protected override void DrawInternal()
    {
        using (_uiSharedService.UidFont.Push())
            ImGui.TextUnformatted("Create new Syncshell");

        if (_lastCreatedGroup == null)
        {
            if (_uiSharedService.IconTextButton(FontAwesomeIcon.Plus, "Create Syncshell"))
            {
                try
                {
                    _lastCreatedGroup = _apiController.GroupCreate().Result;
                }
                catch
                {
                    _lastCreatedGroup = null;
                    _errorGroupCreate = true;
                }
            }
            ImGui.SameLine();
        }

        ImGui.Separator();

        if (_lastCreatedGroup == null)
        {
            UiSharedService.TextWrapped("Creating a new Syncshell will create it with your current preferred permissions for Syncshells as default suggested permissions." + Environment.NewLine +
                "- You can own up to " + _apiController.ServerInfo.MaxGroupsCreatedByUser + " Syncshells on this server." + Environment.NewLine +
                "- You can join up to " + _apiController.ServerInfo.MaxGroupsJoinedByUser + " Syncshells on this server (including your own)" + Environment.NewLine +
                "- Syncshells on this server can have a maximum of " + _apiController.ServerInfo.MaxGroupUserCount + " users");
            ImGuiHelpers.ScaledDummy(2f);
            ImGui.TextUnformatted("Your current Syncshell preferred permissions are:");
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("- Animations");
            _uiSharedService.BooleanToColoredIcon(!_apiController.DefaultPermissions!.DisableGroupAnimations);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("- Sounds");
            _uiSharedService.BooleanToColoredIcon(!_apiController.DefaultPermissions!.DisableGroupSounds);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("- VFX");
            _uiSharedService.BooleanToColoredIcon(!_apiController.DefaultPermissions!.DisableGroupVFX);
            UiSharedService.TextWrapped("(Those preferred permissions can be changed anytime after Syncshell creation, your defaults can be changed anytime in the Moonlight Settings)");
        }
        else
        {
            _errorGroupCreate = false;
            ImGui.TextUnformatted("Syncshell ID: " + _lastCreatedGroup.Group.GID);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Syncshell Password: " + _lastCreatedGroup.Password);
            ImGui.SameLine();
            if (_uiSharedService.IconButton(FontAwesomeIcon.Copy))
            {
                ImGui.SetClipboardText(_lastCreatedGroup.Password);
            }
            UiSharedService.TextWrapped("You can change the Syncshell password later at any time.");
            ImGui.Separator();
            UiSharedService.TextWrapped("These settings were set based on your preferred syncshell permissions:");
            ImGuiHelpers.ScaledDummy(2f);
            ImGui.AlignTextToFramePadding();
            UiSharedService.TextWrapped("Suggest Animation sync:");
            _uiSharedService.BooleanToColoredIcon(!_lastCreatedGroup.GroupUserPreferredPermissions.IsDisableAnimations());
            ImGui.AlignTextToFramePadding();
            UiSharedService.TextWrapped("Suggest Sounds sync:");
            _uiSharedService.BooleanToColoredIcon(!_lastCreatedGroup.GroupUserPreferredPermissions.IsDisableSounds());
            ImGui.AlignTextToFramePadding();
            UiSharedService.TextWrapped("Suggest VFX sync:");
            _uiSharedService.BooleanToColoredIcon(!_lastCreatedGroup.GroupUserPreferredPermissions.IsDisableVFX());
        }

        if (_errorGroupCreate)
        {
            UiSharedService.ColorTextWrapped("Something went wrong during creation of a new Syncshell", new Vector4(1, 0, 0, 1));
        }
    }

    public override void OnOpen()
    {
        _lastCreatedGroup = null;
    }
}