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

using Gurux.DLMS.AMI.Module;
using Gurux.DLMS.AMI.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Mail;
using System.Runtime.Versioning;
using System.Text;

namespace Gurux.AMI.SMTP.Server;

/// <summary>
/// Sends HTML email using the SMTP module's current runtime settings.
/// </summary>
/// <param name="context">The module context providing settings, host services and lifetime cancellation.</param>
/// <param name="transports">The factory used to create a transport for each message.</param>
[UnsupportedOSPlatform("browser")]
public sealed class GXSmtpSender(AmiModuleContext context, ISmtpTransportFactory transports) : IGXAmiEmailSender
{
    /// <summary>Sends an HTML email using the configured SMTP settings.</summary>
    /// <param name="email">The recipient email address.</param>
    /// <param name="subject">The email subject.</param>
    /// <param name="htmlMessage">The HTML content of the email body.</param>
    /// <param name="cancellationToken">A token that cancels the send operation.</param>
    /// <returns>A task that completes when the email has been sent.</returns>
    /// <remarks>The configured delivery timeout applies. The operation is also cancelled when the module's lifetime ends.</remarks>
    /// <exception cref="TimeoutException">Delivery exceeds the configured timeout.</exception>
    public Task SendEmailAsync(string email, string subject, string htmlMessage, CancellationToken cancellationToken = default)
        => SendTestEmailAsync(email, subject, htmlMessage, null, cancellationToken);

    /// <summary>Sends an HTML email with optional temporary SMTP settings.</summary>
    /// <param name="email">The recipient email address.</param>
    /// <param name="subject">The email subject.</param>
    /// <param name="htmlMessage">The HTML content of the email body.</param>
    /// <param name="testOptions">
    /// Temporary settings, or <see langword="null"/> to use the configured settings.
    /// An empty temporary password reuses the configured password.
    /// </param>
    /// <param name="cancellationToken">A token that cancels the send operation.</param>
    /// <returns>A task that completes when the email has been sent.</returns>
    /// <remarks>
    /// Temporary settings are not persisted. The host's Smtp configuration section is used
    /// when no module settings have been stored. Stored encrypted passwords are decrypted
    /// before creating the transport. Module lifetime cancellation also cancels the operation.
    /// </remarks>
    /// <exception cref="SmtpException">The SMTP server cannot be reached or rejects the message.</exception>
    /// <exception cref="TimeoutException">Delivery exceeds the configured timeout.</exception>
    /// <exception cref="OperationCanceledException">Caller or module lifetime cancellation is requested.</exception>
    public async Task SendTestEmailAsync(string email, string subject, string htmlMessage,
        SmtpOptions? testOptions, CancellationToken cancellationToken = default)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, context.CancellationToken);
        var token = linkedCancellation.Token;
        token.ThrowIfCancellationRequested();
        SmtpOptions stored = context.Settings.Read<SmtpOptions>();
        SmtpOptions options = new()
        {
            Host = stored.Host,
            Port = stored.Port,
            TimeoutSeconds = stored.TimeoutSeconds,
            EnableSsl = stored.EnableSsl,
            Email = stored.Email,
            Password = stored.Password
        };
        bool storedPasswordIsEncrypted = !string.IsNullOrWhiteSpace(context.Settings.Value);
        if (!storedPasswordIsEncrypted)
        {
            context.HostServices.GetService<IConfiguration>()?.GetSection("Smtp").Bind(options);
        }

        bool hasPlainTestPassword = !string.IsNullOrEmpty(testOptions?.Password);
        if (testOptions != null)
        {
            options = new SmtpOptions
            {
                Host = testOptions.Host,
                Port = testOptions.Port,
                TimeoutSeconds = testOptions.TimeoutSeconds,
                EnableSsl = testOptions.EnableSsl,
                Email = testOptions.Email,
                Password = string.IsNullOrEmpty(testOptions.Password) ? options.Password : testOptions.Password
            };
        }
        ValidateOptions(options);
        if (storedPasswordIsEncrypted && !hasPlainTestPassword && !string.IsNullOrEmpty(options.Password))
        {
            var crypto = context.HostServices.GetRequiredService<IAmiCryptoService>();
            options.Password = Encoding.UTF8.GetString(await crypto.DecryptAsync(options.Password));
            token.ThrowIfCancellationRequested();
        }
        using var message = new MailMessage(options.Email!, email, subject, htmlMessage) { IsBodyHtml = true };
        using var transport = transports.Create(options);
        linkedCancellation.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        try
        {
            await transport.SendAsync(message, token);
        }
        catch (OperationCanceledException exception) when (token.IsCancellationRequested &&
            !cancellationToken.IsCancellationRequested && !context.CancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("SMTP delivery timed out.", exception);
        }
    }

    /// <summary>Validates the SMTP host, port and sender address without opening a connection.</summary>
    /// <param name="options">The SMTP settings to validate.</param>
    /// <exception cref="ArgumentException">The host or sender email address is empty or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The port or delivery timeout is outside its allowed range.</exception>
    /// <exception cref="FormatException">The sender email address has an invalid format.</exception>
    public static void ValidateOptions(SmtpOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Host);
        if (options.TimeoutSeconds is < 1 or > 300)
        {
            throw new ArgumentOutOfRangeException(nameof(options.TimeoutSeconds), "SMTP timeout must be between 1 and 300 seconds.");
        }
        if (options.Port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(options.Port), "Invalid SMTP port.");
        }

        if (string.IsNullOrWhiteSpace(options.Email))
        {
            throw new ArgumentException("SMTP sender email address is required.");
        }
        _ = new MailAddress(options.Email);
    }
}

