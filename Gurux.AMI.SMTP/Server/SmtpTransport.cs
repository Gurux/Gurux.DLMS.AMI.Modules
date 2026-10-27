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

using System.Net;
using System.Net.Mail;
using System.Runtime.Versioning;

namespace Gurux.AMI.SMTP.Server;

/// <summary>Provides an SMTP transport for a single email operation.</summary>
/// <remarks>The caller owns the transport and must dispose it after sending.</remarks>
[UnsupportedOSPlatform("browser")]
public interface ISmtpTransport : IDisposable
{
    /// <summary>Sends the supplied email message asynchronously.</summary>
    /// <param name="message">The message to send. The caller retains ownership of the message.</param>
    /// <param name="cancellationToken">A token that cancels the send operation.</param>
    /// <returns>A task that completes when the send operation finishes.</returns>
    Task SendAsync(MailMessage message, CancellationToken cancellationToken);
}

/// <summary>Creates SMTP transports with independent lifetimes.</summary>
[UnsupportedOSPlatform("browser")]
public interface ISmtpTransportFactory
{
    /// <summary>Creates a transport configured with the supplied SMTP settings.</summary>
    /// <param name="options">The validated connection settings, including any plaintext authentication password.</param>
    /// <returns>A transport that the caller must dispose after use.</returns>
    ISmtpTransport Create(SmtpOptions options);
}

/// <summary>Creates SMTP transports backed by <see cref="SmtpClient"/>.</summary>
/// <remarks>
/// Uses the sender email address as the authentication username when a password is supplied.
/// When enabled, connection encryption uses STARTTLS rather than implicit TLS.
/// </remarks>
[UnsupportedOSPlatform("browser")]
public sealed class SmtpTransportFactory : ISmtpTransportFactory
{
    /// <inheritdoc />
    public ISmtpTransport Create(SmtpOptions options) => new Transport(options);

    /// <summary>Wraps the SMTP client used by one email operation.</summary>
    private sealed class Transport : ISmtpTransport
    {
        /// <summary>The SMTP client owned by this transport.</summary>
        private readonly SmtpClient client;
        /// <summary>Initializes the SMTP client without opening a connection.</summary>
        /// <param name="options">The connection and authentication settings.</param>
        public Transport(SmtpOptions options)
        {
            client = new SmtpClient(options.Host!, options.Port) { UseDefaultCredentials = false, EnableSsl = options.EnableSsl };
            if (!string.IsNullOrEmpty(options.Password))
            {
                client.Credentials = new NetworkCredential(options.Email, options.Password);
            }
        }
        /// <inheritdoc />
        public Task SendAsync(MailMessage message, CancellationToken cancellationToken)
            => client.SendMailAsync(message, cancellationToken);
        /// <summary>Releases the SMTP client and its connection resources.</summary>
        public void Dispose() => client.Dispose();
    }
}

