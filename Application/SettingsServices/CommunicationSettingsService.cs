using System;
using System.IO;
using TripleDetection.Application.Security;
using TripleDetection.Infrastructure;
using TripleDetection.Presentation.Models;

namespace TripleDetection.Application.SettingsServices;

public class CommunicationSettingsService
{
    private readonly string _configPath;
    private CommunicationSettings _settings;
    private readonly IAuthorizationService? _authz;

    public CommunicationSettingsService(IAuthorizationService? authz = null)
    {
        _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "communication.json");
        _authz = authz;
    }

    public CommunicationSettings Load()
    {
        _settings = JsonHelper.Load<CommunicationSettings>(_configPath);
        return _settings;
    }

    public void Save(CommunicationSettings settings)
    {
        _authz?.Require(AuthorizationAction.SettingsModify);
        _settings = settings;
        JsonHelper.Save(settings, _configPath);
    }

    public string GetConfigPath() => _configPath;
}