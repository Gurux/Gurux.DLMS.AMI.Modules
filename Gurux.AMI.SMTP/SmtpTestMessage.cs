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
namespace Gurux.AMI.SMTP
{
    /// <summary>
    /// Contains the recipient and optional temporary settings for an SMTP test email.
    /// </summary>
    public class SmtpTestMessage
    {
        /// <summary>
        /// Gets or sets the email address that receives the test message.
        /// </summary>
        public string? Address { get; set; }

        /// <summary>
        /// Gets or sets temporary SMTP settings, or <see langword="null"/> to use the configured settings.
        /// </summary>
        /// <remarks>An empty temporary password reuses the configured password. Settings are not persisted.</remarks>
        public SmtpOptions? Options { get; set; }
    }
}
