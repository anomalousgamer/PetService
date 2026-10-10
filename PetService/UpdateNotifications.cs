using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Plugin;

namespace PetService;

internal sealed class UpdateNotifications(Plugin plugin,bool hadConfiguration)
{
    private static readonly TimeSpan LoginDelay = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(12);

    private readonly HashSet<Version> notifiedVersions = new();
    private DateTime? loginEligibleAtUtc;
    private DateTime nextCheckAtUtc;
    private Task<PluginUpdate?>? pendingCheck;
    private bool manualCheckRequested;
    private bool installedVersionRecorded;

    internal void Update()
    {
        if (!Plugin.Player.IsLoaded)
        {
            loginEligibleAtUtc = null;
            nextCheckAtUtc = DateTime.MinValue;
            return;
        }

        if (pendingCheck is { IsCompleted: true })
        {
            CompleteCheck();
        }

        var now = DateTime.UtcNow;
        loginEligibleAtUtc ??= now + LoginDelay;
        if(now>=loginEligibleAtUtc.Value && Plugin.PluginInterface.IsAutoUpdateComplete)AnnounceInstalledVersion();
        if (now < loginEligibleAtUtc.Value || now < nextCheckAtUtc
            || pendingCheck is not null || !Plugin.PluginInterface.IsAutoUpdateComplete)
        {
            return;
        }

        StartCheck(now);
    }

    private void AnnounceInstalledVersion()
    {
        if(installedVersionRecorded)return;
        var version=typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "0.5.0.0";
        var previous=plugin.Configuration.LastLoadedVersion;
        if(previous==version){installedVersionRecorded=true;return;}
        if(!plugin.Mutate(c=>c.LastLoadedVersion=version))return;
        installedVersionRecorded=true;
        if(!hadConfiguration && previous.Length==0)return;
        var message=$"Pet Service is now running version {version}.";
        Plugin.Notifications.AddNotification(new Notification{Title="Pet Service updated",Content=message,Type=NotificationType.Info});
        Plugin.Chat.Print(message,"Pet Service");
    }

    internal void CheckNow()
    {
        manualCheckRequested = true;
        if (pendingCheck is null)
        {
            StartCheck(DateTime.UtcNow);
        }
    }

    private void StartCheck(DateTime now)
    {
        nextCheckAtUtc = now + CheckInterval;
        try
        {
            pendingCheck = Plugin.PluginInterface.CheckForUpdateAsync();
        }
        catch (Exception exception)
        {
            Plugin.Log.Warning(exception, "Could not check for Pet Service updates.");
            if (manualCheckRequested)
            {
                Plugin.Chat.PrintError("Could not check for updates. Try again later.", "Pet Service");
                manualCheckRequested = false;
            }
        }
    }

    private void CompleteCheck()
    {
        var completedCheck = pendingCheck!;
        pendingCheck = null;

        try
        {
            var update = completedCheck.GetAwaiter().GetResult();
            if (update is null)
            {
                if (manualCheckRequested)
                {
                    Plugin.Chat.Print("No update is currently available.", "Pet Service");
                }

                return;
            }

            var version = update.Version;
            if (manualCheckRequested && notifiedVersions.Contains(version))
            {
                Plugin.Chat.Print($"Version {version} is available. Update through /xlplugins.", "Pet Service");
                return;
            }

            if (!notifiedVersions.Contains(version))
            {
                var message = $"Version {version} is available. Update through /xlplugins.";
                Plugin.Notifications.AddNotification(new Notification
                {
                    Title = "Pet Service update available",
                    Content = message,
                    Type = NotificationType.Info,
                });
                notifiedVersions.Add(version);
                Plugin.Chat.Print(message, "Pet Service");
            }
        }
        catch (Exception exception)
        {
            Plugin.Log.Warning(exception, "Could not check for Pet Service updates.");
            if (manualCheckRequested)
            {
                Plugin.Chat.PrintError("Could not check for updates. Try again later.", "Pet Service");
            }
        }
        finally
        {
            manualCheckRequested = false;
        }
    }
}
