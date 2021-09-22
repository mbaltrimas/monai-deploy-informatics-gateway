// Copyright 2021 MONAI Consortium
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//     http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Monai.Deploy.InformaticsGateway.Api.Rest;
using Monai.Deploy.InformaticsGateway.Client;
using Monai.Deploy.InformaticsGateway.Shared.Test;
using Moq;
using System;
using System.Collections.Generic;
using System.CommandLine.Builder;
using System.CommandLine.Hosting;
using System.CommandLine.Parsing;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Monai.Deploy.InformaticsGateway.CLI.Test
{
    public class StatusCommandTest

    {
        private readonly Mock<IConfigurationService> _configurationService;
        private readonly CommandLineBuilder _commandLineBuilder;
        private readonly Parser _paser;
        private readonly Mock<ILoggerFactory> _loggerFactory;
        private readonly Mock<ILogger> _logger;
        private readonly Mock<IInformaticsGatewayClient> _informaticsGatewayClient;

        public StatusCommandTest()
        {
            _loggerFactory = new Mock<ILoggerFactory>();
            _logger = new Mock<ILogger>();
            _configurationService = new Mock<IConfigurationService>();
            _informaticsGatewayClient = new Mock<IInformaticsGatewayClient>();
            _commandLineBuilder = new CommandLineBuilder()
                .UseHost(
                    _ => Host.CreateDefaultBuilder(),
                    host =>
                    {
                        host.ConfigureServices(services =>
                        {
                            services.AddSingleton<ILoggerFactory>(p => _loggerFactory.Object);
                            services.AddSingleton<IConfigurationService>(p => _configurationService.Object);
                            services.AddSingleton<IInformaticsGatewayClient>(p => _informaticsGatewayClient.Object);
                        });
                    })
                .AddCommand(new StatusCommand());
            _paser = _commandLineBuilder.Build();
            _loggerFactory.Setup(p => p.CreateLogger(It.IsAny<string>())).Returns(_logger.Object);
            _configurationService.Setup(p => p.ConfigurationExists()).Returns(true);
            _configurationService.Setup(p => p.Load(It.IsAny<bool>())).Returns(new ConfigurationOptions { Endpoint = "http://test" });
        }

        [Fact(DisplayName = "status comand")]
        public async Task Status_Command()
        {
            var command = "status";
            var result = _paser.Parse(command);
            Assert.Equal(0, result.Errors.Count);

            _informaticsGatewayClient.Setup(p => p.Health.Status(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new HealthStatusResponse
                {
                    ActiveDimseConnections = 100,
                    Services = new Dictionary<string, ServiceStatus>() {
                        { "Test", ServiceStatus.Running }
                    }
                });

            int exitCode = await _paser.InvokeAsync(command);
            Assert.Equal(ExitCodes.Success, exitCode);

            _logger.VerifyLogging("Number of active DIMSE connections: 100", LogLevel.Information, Times.Once());
            _logger.VerifyLogging("Service Status: ", LogLevel.Information, Times.Once());
            _logger.VerifyLogging($"\t\tTest: {ServiceStatus.Running}", LogLevel.Information, Times.Once());
        }

        [Fact(DisplayName = "status comand exception")]
        public async Task Status_Command_Exception()
        {
            var command = "status";
            var result = _paser.Parse(command);
            Assert.Equal(0, result.Errors.Count);

            _informaticsGatewayClient.Setup(p => p.Health.Status(It.IsAny<CancellationToken>()))
                .Throws(new Exception("error"));

            int exitCode = await _paser.InvokeAsync(command);
            Assert.Equal(ExitCodes.Status_Error, exitCode);

            _logger.VerifyLoggingMessageBeginsWith("Error retrieving service status:", LogLevel.Critical, Times.Once());
        }
    }
}
