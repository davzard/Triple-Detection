using System;
using System.IO;
using TripleDetection.Application.Security;
using TripleDetection.Infrastructure;
using TripleDetection.Presentation.Models;

namespace TripleDetection.Application.SettingsServices;

public class DeviceControlSettingsService
{
    private readonly string _configPath;
    private DeviceControlSettings _settings;
    private readonly IAuthorizationService? _authz;

    public DeviceControlSettingsService(IAuthorizationService? authz = null)
    {
        _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "device_control.json");
        _authz = authz;
    }

    public DeviceControlSettings Load()
    {
        _settings = JsonHelper.Load<DeviceControlSettings>(_configPath);
        return _settings;
    }

    public void Save(DeviceControlSettings settings)
    {
        _authz?.Require(AuthorizationAction.SettingsModify);
        _settings = settings;
        JsonHelper.Save(settings, _configPath);
    }
}