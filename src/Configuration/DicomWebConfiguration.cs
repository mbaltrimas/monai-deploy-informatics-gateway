/*
 * Copyright 2021-2023 MONAI Consortium
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 * http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using Microsoft.Extensions.Configuration;

namespace Monai.Deploy.InformaticsGateway.Configuration
{
    public class DicomWebConfiguration
    {
        public static readonly int DefaultClientTimeout = 3600;

        /// <summary>
        /// Gets or sets the client connection timeout in seconds.
        /// </summary>
        [ConfigurationKeyName("clientTimeout")]
        public int ClientTimeoutSeconds { get; set; } = DefaultClientTimeout;

        /// <summary>
        /// Gets or sets the (postfix) name of the DICOMweb export agent used for receiving messages.
        /// The agent name is combine with <see cref="MessageBrokerConfigurationKeys.ExportRequestPrefix"/>
        /// for subscribing messages from the message broker service.
        [ConfigurationKeyName("agentName")]
        public string AgentName { get; set; } = "monaidicomweb";

        /// <summary>
        /// Gets or sets the maximum number of simultaneous DICOMweb connections.
        /// </summary>
        [ConfigurationKeyName("maximumNumberOfConnections")]
        public ushort MaximumNumberOfConnection { get; set; } = 2;

        /// <summary>
        /// Gets or set the maximum allowed file size in bytes with default to 2GiB.
        /// </summary>
        [ConfigurationKeyName("maxAllowedFileSize")]
        public long MaxAllowedFileSize { get; set; } = 2147483648;

        /// <summary>
        /// Timeout, in seconds, to wait for instances before notifying other subsystems of data arrival
        /// for the specified data group.
        /// Defaults to two seconds.
        /// Note: the currently implementation of DICOMweb expects the entire payload to be received in a
        /// single POST request, therefore, the timeout value may be insignificant unless the load of the
        /// network affects the upload speed.
        /// </summary>
        [ConfigurationKeyName("timeout")]
        public uint Timeout { get; set; } = 10;

        public DicomWebConfiguration()
        {
        }
    }
}
