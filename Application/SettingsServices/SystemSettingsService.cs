using System;
using System.IO;
using TripleDetection.Application.Security;
using TripleDetection.Infrastructure;
using TripleDetection.Presentation.Models;

namespace TripleDetection.Application.SettingsServices;

public class SystemSettingsService
{
    private readonly string _configPath;
    private SystemSettings _settings;
    private readonly IAuthorizationService? _authz;

    public SystemSettingsService(IAuthorizationService? authz = null)
    {
        _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "system.json");
        _authz = authz;
    }

    public SystemSettings Load()
    {
        _settings = JsonHelper.Load<SystemSettings>(_configPath);
        return _settings;
    }

    public void Save(SystemSettings settings)
    {
        _authz?.Require(AuthorizationAction.SettingsModify);
        _settings = settings;
        JsonHelper.Save(settings, _configPath);
    }
}