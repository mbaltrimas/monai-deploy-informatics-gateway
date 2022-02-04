﻿// Copyright 2021-2022 MONAI Consortium
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//     http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

/*
 * Apache License, Version 2.0
 * Copyright 2019-2021 NVIDIA Corporation
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using FellowOakDicom;
using FellowOakDicom.Imaging.Codec;
using FellowOakDicom.Log;
using FellowOakDicom.Network;
using Microsoft.Extensions.Logging;
using Monai.Deploy.InformaticsGateway.Api;
using Monai.Deploy.InformaticsGateway.Common;
using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Monai.Deploy.InformaticsGateway.Services.Scp
{
    /// <summary>
    /// A new instance of <c>ScpServiceInternal</c> is created for every new association.
    /// </summary>
    internal class ScpServiceInternal :
        DicomService,
        IDicomServiceProvider,
        IDicomCEchoProvider,
        IDicomCStoreProvider
    {
        private Microsoft.Extensions.Logging.ILogger _logger;
        private IApplicationEntityManager _associationDataProvider;
        private IDisposable _loggerScope;
        private Guid _associationId;
        private string _associationIdStr;

        public ScpServiceInternal(INetworkStream stream, Encoding fallbackEncoding, FellowOakDicom.Log.ILogger log, ILogManager logManager, INetworkManager network, ITranscoderManager transcoder)
                : base(stream, fallbackEncoding, log, logManager, network, transcoder)
        {
        }

        public Task<DicomCEchoResponse> OnCEchoRequestAsync(DicomCEchoRequest request)
        {
            _logger?.Log(LogLevel.Information, $"C-ECH request received");
            return Task.FromResult(new DicomCEchoResponse(request, DicomStatus.Success));
        }

        public void OnConnectionClosed(Exception exception)
        {
            if (exception != null)
            {
                _logger?.Log(LogLevel.Error, exception, "Connection closed with exception.");
            }

            _loggerScope?.Dispose();
            Interlocked.Decrement(ref ScpService.ActiveConnections);
        }

        public async Task<DicomCStoreResponse> OnCStoreRequestAsync(DicomCStoreRequest request)
        {
            try
            {
                _logger?.Log(LogLevel.Information, $"Transfer syntax used: {request.TransferSyntax}");
                await _associationDataProvider.HandleCStoreRequest(request, Association.CalledAE, Association.CallingAE, _associationId);
                return new DicomCStoreResponse(request, DicomStatus.Success);
            }
            catch (InsufficientStorageAvailableException ex)
            {
                _logger?.Log(LogLevel.Error, "Failed to process C-STORE request, out of storage space: {ex}", ex);
                return new DicomCStoreResponse(request, DicomStatus.ResourceLimitation);
            }
            catch (System.IO.IOException ex) when ((ex.HResult & 0xFFFF) == 0x27 || (ex.HResult & 0xFFFF) == 0x70)
            {
                _logger?.Log(LogLevel.Error, "Failed to process C-STORE request, out of storage space: {ex}", ex);
                return new DicomCStoreResponse(request, DicomStatus.ResourceLimitation);
            }
            catch (Exception ex)
            {
                _logger?.Log(LogLevel.Error, "Failed to process C-STORE request: {ex}", ex);
                return new DicomCStoreResponse(request, DicomStatus.ProcessingFailure);
            }
        }

        public Task OnCStoreRequestExceptionAsync(string tempFileName, Exception e)
        {
            _logger?.Log(LogLevel.Error, e, "Exception handling C-STORE Request");
            return Task.CompletedTask;
        }

        public void OnReceiveAbort(DicomAbortSource source, DicomAbortReason reason)
        {
            _logger?.Log(LogLevel.Warning, $"Aborted {source} with reason {reason}");
        }

        /// <summary>
        /// Start timer only if a receive association release request is received.
        /// </summary>
        /// <returns></returns>
        public Task OnReceiveAssociationReleaseRequestAsync()
        {
            _logger?.Log(LogLevel.Information, "Association release request received");
            return SendAssociationReleaseResponseAsync();
        }

        public Task OnReceiveAssociationRequestAsync(DicomAssociation association)
        {
            Interlocked.Increment(ref ScpService.ActiveConnections);
            _associationDataProvider = UserState as IApplicationEntityManager;

            if (_associationDataProvider is null)
            {
                throw new ArgumentNullException("userState must be an instance of IAssociationDataProvider");
            }

            _logger = _associationDataProvider.GetLogger(Association.CalledAE);

            _associationId = Guid.NewGuid();
            _associationIdStr = $"#{_associationId} {association.RemoteHost}:{association.RemotePort}";

            _loggerScope = _logger?.BeginScope(new LoggingDataDictionary<string, object> { { "Association", _associationIdStr } });
            _logger?.Log(LogLevel.Information, $"Association received from {association.RemoteHost}:{association.RemotePort}");

            if (!IsValidSourceAe(association.CallingAE, association.RemoteHost))
            {
                return SendAssociationRejectAsync(
                    DicomRejectResult.Permanent,
                    DicomRejectSource.ServiceUser,
                    DicomRejectReason.CallingAENotRecognized);
            }

            if (!IsValidCalledAe(association.CalledAE))
            {
                return SendAssociationRejectAsync(
                    DicomRejectResult.Permanent,
                    DicomRejectSource.ServiceUser,
                    DicomRejectReason.CalledAENotRecognized);
            }

            foreach (var pc in association.PresentationContexts)
            {
                if (pc.AbstractSyntax == DicomUID.Verification)
                {
                    if (!_associationDataProvider.Configuration.Value.Dicom.Scp.EnableVerification)
                    {
                        _logger?.Log(LogLevel.Warning, "Verification service is disabled: rejecting association");
                        return SendAssociationRejectAsync(
                            DicomRejectResult.Permanent,
                            DicomRejectSource.ServiceUser,
                            DicomRejectReason.ApplicationContextNotSupported
                        );
                    }
                    pc.AcceptTransferSyntaxes(_associationDataProvider.Configuration.Value.Dicom.Scp.VerificationServiceTransferSyntaxes.ToDicomTransferSyntaxArray());
                }
                else if (pc.AbstractSyntax.StorageCategory != DicomStorageCategory.None)
                {
                    if (!_associationDataProvider.CanStore)
                    {
                        return SendAssociationRejectAsync(
                            DicomRejectResult.Permanent,
                            DicomRejectSource.ServiceUser,
                            DicomRejectReason.NoReasonGiven);
                    }
                    // Accept any proposed TS
                    pc.AcceptTransferSyntaxes(pc.GetTransferSyntaxes().ToArray());
                }
            }

            return SendAssociationAcceptAsync(association);
        }

        private bool IsValidCalledAe(string calledAe)
        {
            return _associationDataProvider.IsAeTitleConfigured(calledAe);
        }

        private bool IsValidSourceAe(string callingAe, string host)
        {
            if (!_associationDataProvider.Configuration.Value.Dicom.Scp.RejectUnknownSources) return true;

            return _associationDataProvider.IsValidSource(callingAe, host);
        }
    }
}
