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

using System.ComponentModel;

namespace Gurux.AMI.SMTP
{
    /// <summary>
    /// Defines the SMTP connection, sender and authentication settings used to send email.
    /// </summary>
    /// <remarks>The sender email address is also used as the SMTP authentication username.</remarks>
    public class SmtpOptions
    {
        /// <summary>
        /// Gets or sets the SMTP server hostname or IP address.
        /// </summary>
        [DefaultValue(null)]
        public string? Host { get; set; }

        /// <summary>
        /// Gets or sets the SMTP server port. The default is 587.
        /// </summary>
        [DefaultValue(587)]
        public int Port { get; set; } = 587;

        /// <summary>Gets or sets the maximum email delivery time in seconds. The default is 30.</summary>
        /// <remarks>Valid values are 1 through 300. Caller and module cancellation still take precedence.</remarks>
        [DefaultValue(30)]
        public int TimeoutSeconds { get; set; } = 30;

        /// <summary>Gets or sets whether to encrypt the connection using STARTTLS. The default is <see langword="true"/>.</summary>
        /// <remarks>This option does not enable implicit TLS connections.</remarks>
        [DefaultValue(true)]
        public bool EnableSsl { get; set; } = true;

        /// <summary>
        /// Gets or sets the sender email address, also used as the SMTP authentication username.
        /// </summary>
        [DefaultValue(null)]
        public string? Email { get; set; }

        /// <summary>
        /// Gets or sets the SMTP authentication password.
        /// </summary>
        /// <remarks>
        /// Persisted module settings contain an encrypted password. A password supplied for a test
        /// is plaintext; an empty test password reuses the configured password.
        /// </remarks>
        [DefaultValue(null)]
        public string? Password { get; set; }
    }
}


