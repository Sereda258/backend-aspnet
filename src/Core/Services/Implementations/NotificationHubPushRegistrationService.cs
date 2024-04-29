﻿using Bit.Core.Enums;
using Bit.Core.Models.Data;
using Bit.Core.Repositories;
using Bit.Core.Settings;
using Microsoft.Azure.NotificationHubs;
using Microsoft.Extensions.Logging;

namespace Bit.Core.Services;

public class NotificationHubPushRegistrationService : IPushRegistrationService
{
    private readonly IInstallationDeviceRepository _installationDeviceRepository;
    private readonly GlobalSettings _globalSettings;
    private readonly ILogger<NotificationHubPushRegistrationService> _logger;
    private Dictionary<NotificationHubType, NotificationHubClient> _clients = [];

    public NotificationHubPushRegistrationService(
        IInstallationDeviceRepository installationDeviceRepository,
        GlobalSettings globalSettings,
        ILogger<NotificationHubPushRegistrationService> logger)
    {
        _installationDeviceRepository = installationDeviceRepository;
        _globalSettings = globalSettings;
        _logger = logger;

        // Is this dirty to do in the ctor?
        void addHub(NotificationHubType type)
        {
            var hubRegistration = globalSettings.NotificationHubs.FirstOrDefault(
                h => h.HubType == type && h.EnableRegistration);
            if (hubRegistration != null)
            {
                var client = NotificationHubClient.CreateClientFromConnectionString(
                    hubRegistration.ConnectionString,
                    hubRegistration.HubName,
                    hubRegistration.EnableSendTracing);
                _clients.Add(type, client);
            }
        }

        addHub(NotificationHubType.General);
        addHub(NotificationHubType.iOS);
        addHub(NotificationHubType.Android);
    }

    public async Task CreateOrUpdateRegistrationAsync(string pushToken, string deviceId, string userId,
        string identifier, DeviceType type)
    {
        if (string.IsNullOrWhiteSpace(pushToken))
        {
            return;
        }

        var installation = new Installation
        {
            InstallationId = deviceId,
            PushChannel = pushToken,
            Templates = new Dictionary<string, InstallationTemplate>()
        };

        installation.Tags = new List<string>
        {
            $"userId:{userId}"
        };

        if (!string.IsNullOrWhiteSpace(identifier))
        {
            installation.Tags.Add("deviceIdentifier:" + identifier);
        }

        string payloadTemplate = null, messageTemplate = null, badgeMessageTemplate = null;
        switch (type)
        {
            case DeviceType.Android:
                payloadTemplate = "{\"data\":{\"data\":{\"type\":\"#(type)\",\"payload\":\"$(payload)\"}}}";
                messageTemplate = "{\"data\":{\"data\":{\"type\":\"#(type)\"}," +
                    "\"notification\":{\"title\":\"$(title)\",\"body\":\"$(message)\"}}}";

                installation.Platform = NotificationPlatform.Fcm;
                break;
            case DeviceType.iOS:
                payloadTemplate = "{\"data\":{\"type\":\"#(type)\",\"payload\":\"$(payload)\"}," +
                    "\"aps\":{\"content-available\":1}}";
                messageTemplate = "{\"data\":{\"type\":\"#(type)\"}," +
                    "\"aps\":{\"alert\":\"$(message)\",\"badge\":null,\"content-available\":1}}";
                badgeMessageTemplate = "{\"data\":{\"type\":\"#(type)\"}," +
                    "\"aps\":{\"alert\":\"$(message)\",\"badge\":\"#(badge)\",\"content-available\":1}}";

                installation.Platform = NotificationPlatform.Apns;
                break;
            case DeviceType.AndroidAmazon:
                payloadTemplate = "{\"data\":{\"type\":\"#(type)\",\"payload\":\"$(payload)\"}}";
                messageTemplate = "{\"data\":{\"type\":\"#(type)\",\"message\":\"$(message)\"}}";

                installation.Platform = NotificationPlatform.Adm;
                break;
            default:
                break;
        }

        BuildInstallationTemplate(installation, "payload", payloadTemplate, userId, identifier);
        BuildInstallationTemplate(installation, "message", messageTemplate, userId, identifier);
        BuildInstallationTemplate(installation, "badgeMessage", badgeMessageTemplate ?? messageTemplate,
            userId, identifier);

        await GetClient(type).CreateOrUpdateInstallationAsync(installation);
        if (InstallationDeviceEntity.IsInstallationDeviceId(deviceId))
        {
            await _installationDeviceRepository.UpsertAsync(new InstallationDeviceEntity(deviceId));
        }
    }

    private void BuildInstallationTemplate(Installation installation, string templateId, string templateBody,
        string userId, string identifier)
    {
        if (templateBody == null)
        {
            return;
        }

        var fullTemplateId = $"template:{templateId}";

        var template = new InstallationTemplate
        {
            Body = templateBody,
            Tags = new List<string>
            {
                fullTemplateId,
                $"{fullTemplateId}_userId:{userId}"
            }
        };

        if (!string.IsNullOrWhiteSpace(identifier))
        {
            template.Tags.Add($"{fullTemplateId}_deviceIdentifier:{identifier}");
        }

        installation.Templates.Add(fullTemplateId, template);
    }

    public async Task DeleteRegistrationAsync(string deviceId, DeviceType deviceType)
    {
        try
        {
            await GetClient(deviceType).DeleteInstallationAsync(deviceId);
            if (InstallationDeviceEntity.IsInstallationDeviceId(deviceId))
            {
                await _installationDeviceRepository.DeleteAsync(new InstallationDeviceEntity(deviceId));
            }
        }
        catch (Exception e) when (e.InnerException == null || !e.InnerException.Message.Contains("(404) Not Found"))
        {
            throw;
        }
    }

    public async Task AddUserRegistrationOrganizationAsync(IEnumerable<KeyValuePair<string, DeviceType>> devices, string organizationId)
    {
        await PatchTagsForUserDevicesAsync(devices, UpdateOperationType.Add, $"organizationId:{organizationId}");
        if (devices.Any() && InstallationDeviceEntity.IsInstallationDeviceId(devices.First().Key))
        {
            var entities = devices.Select(e => new InstallationDeviceEntity(e.Key));
            await _installationDeviceRepository.UpsertManyAsync(entities.ToList());
        }
    }

    public async Task DeleteUserRegistrationOrganizationAsync(IEnumerable<KeyValuePair<string, DeviceType>> devices, string organizationId)
    {
        await PatchTagsForUserDevicesAsync(devices, UpdateOperationType.Remove,
            $"organizationId:{organizationId}");
        if (devices.Any() && InstallationDeviceEntity.IsInstallationDeviceId(devices.First().Key))
        {
            var entities = devices.Select(e => new InstallationDeviceEntity(e.Key));
            await _installationDeviceRepository.UpsertManyAsync(entities.ToList());
        }
    }

    private async Task PatchTagsForUserDevicesAsync(IEnumerable<KeyValuePair<string, DeviceType>> devices, UpdateOperationType op,
        string tag)
    {
        if (!devices.Any())
        {
            return;
        }

        var operation = new PartialUpdateOperation
        {
            Operation = op,
            Path = "/tags"
        };

        if (op == UpdateOperationType.Add)
        {
            operation.Value = tag;
        }
        else if (op == UpdateOperationType.Remove)
        {
            operation.Path += $"/{tag}";
        }

        foreach (var device in devices)
        {
            try
            {
                await GetClient(device.Value).PatchInstallationAsync(device.Key, new List<PartialUpdateOperation> { operation });
            }
            catch (Exception e) when (e.InnerException == null || !e.InnerException.Message.Contains("(404) Not Found"))
            {
                throw;
            }
        }
    }

    private NotificationHubClient GetClient(DeviceType deviceType)
    {
        var hubType = NotificationHubType.General;
        switch (deviceType)
        {
            case DeviceType.Android:
                hubType = NotificationHubType.Android;
                break;
            case DeviceType.iOS:
                hubType = NotificationHubType.iOS;
                break;
            case DeviceType.ChromeExtension:
            case DeviceType.FirefoxExtension:
            case DeviceType.OperaExtension:
            case DeviceType.EdgeExtension:
            case DeviceType.VivaldiExtension:
            case DeviceType.SafariExtension:
                hubType = NotificationHubType.GeneralBrowserExtension;
                break;
            case DeviceType.WindowsDesktop:
            case DeviceType.MacOsDesktop:
            case DeviceType.LinuxDesktop:
                hubType = NotificationHubType.GeneralDesktop;
                break;
            case DeviceType.ChromeBrowser:
            case DeviceType.FirefoxBrowser:
            case DeviceType.OperaBrowser:
            case DeviceType.EdgeBrowser:
            case DeviceType.IEBrowser:
            case DeviceType.UnknownBrowser:
            case DeviceType.SafariBrowser:
            case DeviceType.VivaldiBrowser:
                hubType = NotificationHubType.GeneralWeb;
                break;
            default:
                break;
        }

        if (!_clients.ContainsKey(hubType))
        {
            _logger.LogWarning("No hub client for '{0}'. Using general hub instead.", hubType);
            hubType = NotificationHubType.General;
            if (!_clients.ContainsKey(hubType))
            {
                throw new Exception("No general hub client found.");
            }
        }
        return _clients[hubType];
    }
}
