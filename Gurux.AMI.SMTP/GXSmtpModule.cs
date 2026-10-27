//
// --------------------------------------------------------------------------
//  Gurux Ltd
//
//
//
// Filename:        $HeadURL$
//
// Version:         $Revision$,
//                  $Date$
//                  $Author$
//
// Copyright (c) Gurux Ltd
//
//---------------------------------------------------------------------------
//
//  DESCRIPTION
//
// This file is a part of Gurux Device Framework.
//
// Gurux Device Framework is Open Source software; you can redistribute it
// and/or modify it under the terms of the GNU General Public License
// as published by the Free Software Foundation; version 2 of the License.
// Gurux Device Framework is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
// See the GNU General Public License for more details.
//
// This code is licensed under the GNU General Public License v2.
// Full text may be retrieved at http://www.gnu.org/licenses/gpl-2.0.txt
//---------------------------------------------------------------------------

using Gurux.DLMS.AMI.Module.Enums;
using Gurux.DLMS.AMI.Module;
using Gurux.AMI.SMTP.Server;

namespace Gurux.AMI.SMTP;

/// <summary>Registers SMTP email delivery, configuration views and the test email endpoint in AMI.</summary>
public sealed class GXSmtpModule : GXAmiModuleBase
{
    /// <summary>Gets the module identifier used by the catalog and extension endpoints.</summary>
    public override string Id => "Gurux.AMI.SMTP";
    /// <summary>Gets the module's display name.</summary>
    public override string Name => "Smtp";
    /// <summary>Gets the icon resource displayed for the module.</summary>
    public override string Icon => Properties.Resources.SmtpIcon;
    /// <summary>Gets the localized description of the module.</summary>
    public override string Description => Properties.Resources.SmtpTitle;

    /// <summary>Registers the SMTP sender, transport factory, configuration view and test endpoint.</summary>
    /// <param name="builder">The builder used to register module services and endpoints.</param>
    /// <remarks>Editing configuration and sending test emails require the module.edit policy.</remarks>
    public override void Configure(AmiModuleBuilder builder)
    {
        builder.AddView<Configuration>(AmiViewArea.Configuration, "module.edit");
        if (!OperatingSystem.IsBrowser())
        {
            SmtpServer.Configure(builder);
        }
    }

    /// <summary>Ensures the default notification resources exist when the module is installed.</summary>
    /// <param name="context">The context providing access to module resources.</param>
    /// <param name="cancellationToken">A token that cancels resource initialization.</param>
    /// <returns>A task that completes when the notification defaults have been ensured.</returns>
    public override Task InstallAsync(AmiModuleContext context, CancellationToken cancellationToken)
        => context.Resources.EnsureNotificationDefaultsAsync(cancellationToken);

    /// <summary>Ensures the default notification resources exist when the module starts.</summary>
    /// <param name="context">The context providing access to module resources.</param>
    /// <param name="cancellationToken">A token that cancels resource initialization.</param>
    /// <returns>A task that completes when the notification defaults have been ensured.</returns>
    public override Task StartAsync(AmiModuleContext context, CancellationToken cancellationToken)
        => context.Resources.EnsureNotificationDefaultsAsync(cancellationToken);

    /// <summary>Preserves or removes notification defaults according to the uninstall options.</summary>
    /// <param name="context">The context providing uninstall options and module resources.</param>
    /// <param name="cancellationToken">A token that cancels removal of notification defaults.</param>
    /// <returns>A task that completes when the requested resource handling finishes.</returns>
    public override Task UninstallAsync(AmiModuleContext context, CancellationToken cancellationToken)
    {
        context.UninstallOptions.Validate();
        return context.UninstallOptions.Mode == ModuleDataRemoval.Preserve
            ? Task.CompletedTask
            : context.Resources.RemoveNotificationDefaultsAsync(cancellationToken);
    }

}
