using System;
using System.IO;
using TripleDetection.Application.Security;
using TripleDetection.Infrastructure;
using TripleDetection.Presentation.Models;

namespace TripleDetection.Application.SettingsServices;

public class VmSettingsService
{
    private readonly string _configPath;
    private VmSettings _settings;
    private readonly IAuthorizationService? _authz;

    public VmSettingsService(IAuthorizationService? authz = null)
    {
        _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "vm_settings.json");
        _authz = authz;
    }

    public VmSettings Load()
    {
        _settings = JsonHelper.Load<VmSettings>(_configPath);
        return _settings;
    }

    public void Save(VmSettings settings)
    {
        _authz?.Require(AuthorizationAction.SettingsModify);
        _settings = settings;
        JsonHelper.Save(settings, _configPath);
    }
}