/*
 * Copyright 2021-2022 MONAI Consortium
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

using System;
using System.Net.Http;
using Microsoft.Extensions.Logging;

namespace Monai.Deploy.InformaticsGateway.Client.Services
{
    internal abstract class ServiceBase
    {
        protected readonly HttpClient HttpClient;
        protected readonly ILogger Logger;
        protected string RequestServicePrefix { get; private set; } = string.Empty;

        protected ServiceBase(HttpClient httpClient, ILogger logger = null)
        {
            HttpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            Logger = logger;
        }
    }
}
