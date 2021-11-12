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

using Ardalis.GuardClauses;
using FellowOakDicom;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Monai.Deploy.InformaticsGateway.Api;
using Monai.Deploy.InformaticsGateway.Api.Rest;
using Monai.Deploy.InformaticsGateway.Common;
using Monai.Deploy.InformaticsGateway.DicomWeb.Client;
using Monai.Deploy.InformaticsGateway.DicomWeb.Client.API;
using Monai.Deploy.InformaticsGateway.Repositories;
using Monai.Deploy.InformaticsGateway.Services.Common;
using Monai.Deploy.InformaticsGateway.Services.Scp;
using Monai.Deploy.InformaticsGateway.Services.Storage;
using Polly;
using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Monai.Deploy.InformaticsGateway.Services.Connectors
{
    public class DataRetrievalService : IHostedService, IMonaiService
    {
        private readonly ILoggerFactory _loggerFactory;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<DataRetrievalService> _logger;
        private readonly IStorageInfoProvider _storageInfoProvider;
        private readonly IFileSystem _fileSystem;
        private readonly IDicomToolkit _dicomToolkit;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IFileStoredNotificationQueue _fileStoredNotificationQueue;

        public ServiceStatus Status { get; set; }

        public string ServiceName => "Data Retrieval Service";

        public DataRetrievalService(
            ILoggerFactory loggerFactory,
            IHttpClientFactory httpClientFactory,
            ILogger<DataRetrievalService> logger,
            IFileSystem fileSystem,
            IDicomToolkit dicomToolkit,
            IServiceScopeFactory serviceScopeFactory,
            IFileStoredNotificationQueue fileStoredNotificationQueue,
            IStorageInfoProvider storageInfoProvider)
        {
            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _dicomToolkit = dicomToolkit ?? throw new ArgumentNullException(nameof(dicomToolkit));
            _serviceScopeFactory = serviceScopeFactory ?? throw new ArgumentNullException(nameof(serviceScopeFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _fileStoredNotificationQueue = fileStoredNotificationQueue ?? throw new ArgumentNullException(nameof(fileStoredNotificationQueue));
            _storageInfoProvider = storageInfoProvider ?? throw new ArgumentNullException(nameof(storageInfoProvider));
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            var task = Task.Run(async () =>
            {
                await BackgroundProcessing(cancellationToken);
            });

            Status = ServiceStatus.Running;
            if (task.IsCompleted)
                return task;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Data Retriever Hosted Service is stopping.");
            Status = ServiceStatus.Stopped;
            return Task.CompletedTask;
        }

        private async Task BackgroundProcessing(CancellationToken cancellationToken)
        {
            _logger.Log(LogLevel.Information, "Data Retriever Hosted Service is running.");

            while (!cancellationToken.IsCancellationRequested)
            {
                using var scope = _serviceScopeFactory.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<IInferenceRequestRepository>();
                if (!_storageInfoProvider.HasSpaceAvailableToRetrieve)
                {
                    _logger.Log(LogLevel.Warning, $"Data retrieval paused due to insufficient storage space.  Available storage space: {_storageInfoProvider.AvailableFreeSpace:D}.");
                    await Task.Delay(500);
                    continue;
                }

                InferenceRequest request = null;
                try
                {
                    request = await repository.Take(cancellationToken);
                    using (_logger.BeginScope(new LoggingDataDictionary<string, object> { { "TransactionId", request.TransactionId } }))
                    {
                        _logger.Log(LogLevel.Information, "Processing inference request.");
                        await ProcessRequest(request, cancellationToken);
                        await repository.Update(request, InferenceRequestStatus.Success);
                        _logger.Log(LogLevel.Information, "Inference request completed and ready for job submission.");
                    }
                }
                catch (OperationCanceledException ex)
                {
                    _logger.Log(LogLevel.Warning, ex, "Data Retriever Service canceled.");
                }
                catch (InvalidOperationException ex)
                {
                    _logger.Log(LogLevel.Warning, ex, "Data Retriever Service may be disposed.");
                }
                catch (Exception ex)
                {
                    _logger.Log(LogLevel.Error, ex, $"Error processing request: TransactionId = {request?.TransactionId}");
                    if (request != null)
                    {
                        await repository.Update(request, InferenceRequestStatus.Fail);
                    }
                }
            }
            Status = ServiceStatus.Cancelled;
            _logger.Log(LogLevel.Information, "Cancellation requested.");
        }

        private async Task ProcessRequest(InferenceRequest inferenceRequest, CancellationToken cancellationToken)
        {
            Guard.Against.Null(inferenceRequest, nameof(inferenceRequest));

            var retrievedFiles = new Dictionary<string, FileStorageInfo>(StringComparer.OrdinalIgnoreCase);
            RestoreExistingInstances(inferenceRequest, retrievedFiles);

            foreach (var source in inferenceRequest.InputResources)
            {
                switch (source.Interface)
                {
                    case InputInterfaceType.DicomWeb:
                        await RetrieveViaDicomWeb(inferenceRequest, source, retrievedFiles);
                        break;

                    case InputInterfaceType.Fhir:
                        _logger.Log(LogLevel.Information, $"Processing input source '{source.Interface}' from {source.ConnectionDetails.Uri}");
                        await RetrieveViaFhir(inferenceRequest, source, retrievedFiles);
                        break;

                    case InputInterfaceType.Algorithm:
                        continue;
                    default:
                        _logger.Log(LogLevel.Warning, $"Specified input interface is not supported '{source.Interface}`");
                        break;
                }
            }

            await NotifyNewInstance(inferenceRequest, retrievedFiles);
        }

        private async Task NotifyNewInstance(InferenceRequest inferenceRequest, Dictionary<string, FileStorageInfo> retrievedFiles)
        {
            Guard.Against.Null(inferenceRequest, nameof(inferenceRequest));

            if (retrievedFiles.IsNullOrEmpty())
            {
                throw new InferenceRequestException("No files found/retrieved with the request.");
            }

            foreach (var key in retrievedFiles.Keys)
            {
                if (inferenceRequest.Application is not null)
                {
                    retrievedFiles[key].SetApplications(inferenceRequest.Application.Id);
                }
                await _fileStoredNotificationQueue.Queue(retrievedFiles[key]);
            }
        }

        #region Data Retrieval

        private void RestoreExistingInstances(InferenceRequest inferenceRequest, Dictionary<string, FileStorageInfo> retrievedInstances)
        {
            Guard.Against.Null(inferenceRequest, nameof(inferenceRequest));
            Guard.Against.Null(retrievedInstances, nameof(retrievedInstances));

            _logger.Log(LogLevel.Debug, $"Restoring previously retrieved DICOM instances from {inferenceRequest.StoragePath}");
            foreach (var file in _fileSystem.Directory.EnumerateFiles(inferenceRequest.StoragePath, "*", System.IO.SearchOption.AllDirectories))
            {
                var instance = new FileStorageInfo { StorageRootPath = inferenceRequest.StoragePath, CorrelationId = inferenceRequest.TransactionId, FilePath = file };

                if (retrievedInstances.ContainsKey(instance.FilePath))
                {
                    continue;
                }
                retrievedInstances.Add(instance.FilePath, instance);
                _logger.Log(LogLevel.Debug, $"Restored previously retrieved instance {instance.FilePath}");
            }
        }

        private async Task RetrieveViaFhir(InferenceRequest inferenceRequest, RequestInputDataResource source, Dictionary<string, FileStorageInfo> retrievedResources)
        {
            Guard.Against.Null(inferenceRequest, nameof(inferenceRequest));
            Guard.Against.Null(retrievedResources, nameof(retrievedResources));

            foreach (var input in inferenceRequest.InputMetadata.Inputs)
            {
                if (input.Resources.IsNullOrEmpty())
                {
                    continue;
                }
                await RetrieveFhirResources(inferenceRequest.TransactionId, input, source, retrievedResources, inferenceRequest.StoragePath);
            }
        }

        private async Task RetrieveFhirResources(string transactionId, InferenceRequestDetails requestDetails, RequestInputDataResource source, Dictionary<string, FileStorageInfo> retrievedResources, string storagePath)
        {
            Guard.Against.NullOrWhiteSpace(transactionId, nameof(transactionId));
            Guard.Against.Null(requestDetails, nameof(requestDetails));
            Guard.Against.Null(source, nameof(source));
            Guard.Against.Null(retrievedResources, nameof(retrievedResources));
            Guard.Against.NullOrWhiteSpace(storagePath, nameof(storagePath));

            var pendingResources = new Queue<FhirResource>(requestDetails.Resources.Where(p => !p.IsRetrieved));

            if (pendingResources.Count == 0)
            {
                return;
            }

            var authenticationHeaderValue = AuthenticationHeaderValueExtensions.ConvertFrom(source.ConnectionDetails.AuthType, source.ConnectionDetails.AuthId);

            var httpClient = _httpClientFactory.CreateClient("fhir");
            httpClient.DefaultRequestHeaders.Clear();
            httpClient.DefaultRequestHeaders.Authorization = authenticationHeaderValue;
            _fileSystem.Directory.CreateDirectory(storagePath);

            FhirResource resource = null;
            try
            {
                while (pendingResources.Count > 0)
                {
                    resource = pendingResources.Dequeue();
                    resource.IsRetrieved = await RetrieveFhirResource(
                        transactionId,
                        httpClient,
                        resource,
                        source,
                        retrievedResources,
                        storagePath,
                        requestDetails.FhirFormat,
                        requestDetails.FhirAcceptHeader);
                }
            }
            catch (Exception ex)
            {
                _logger.Log(LogLevel.Error, ex, $"Error retrieving FHIR resource {resource?.Type}/{resource?.Id}");
            }
        }

        private async Task<bool> RetrieveFhirResource(string transactionId, HttpClient httpClient, FhirResource resource, RequestInputDataResource source, Dictionary<string, FileStorageInfo> retrievedResources, string storagePath, FhirStorageFormat fhirFormat, string acceptHeader)
        {
            Guard.Against.NullOrWhiteSpace(transactionId, nameof(transactionId));
            Guard.Against.Null(httpClient, nameof(httpClient));
            Guard.Against.Null(resource, nameof(resource));
            Guard.Against.Null(source, nameof(source));
            Guard.Against.Null(retrievedResources, nameof(retrievedResources));
            Guard.Against.NullOrWhiteSpace(storagePath, nameof(storagePath));
            Guard.Against.NullOrWhiteSpace(acceptHeader, nameof(acceptHeader));

            _logger.Log(LogLevel.Debug, $"Retriving FHIR resource {resource.Type}/{resource.Id} with media format {acceptHeader} and file format {fhirFormat}.");
            var request = new HttpRequestMessage(HttpMethod.Get, $"{source.ConnectionDetails.Uri}{resource.Type}/{resource.Id}");
            request.Headers.Accept.Add(MediaTypeWithQualityHeaderValue.Parse(acceptHeader));
            var response = await Policy
                .HandleResult<HttpResponseMessage>(p => !p.IsSuccessStatusCode)
                .WaitAndRetryAsync(3,
                    (retryAttempt) =>
                    {
                        return retryAttempt == 1 ? TimeSpan.FromMilliseconds(250) : TimeSpan.FromMilliseconds(500);
                    },
                    (result, timeSpan, retryCount, context) =>
                    {
                        _logger.Log(LogLevel.Error, result.Exception, $"Failed to retrieve resource {resource.Type}/{resource.Id} with status code {result.Result.StatusCode}, retry count={retryCount}.");
                    })
                .ExecuteAsync(async () => await httpClient.SendAsync(request));

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var file = new FileStorageInfo(transactionId, storagePath, $"{resource.Type}-{resource.Id}", $".{fhirFormat}");
                await _fileSystem.File.WriteAllTextAsync(file.FilePath, json);
                retrievedResources.Add(file.FilePath, file);
                return true;
            }
            else
            {
                _logger.Log(LogLevel.Error, $"Error retriving FHIR resource {resource.Type}/{resource.Id}. Recevied HTTP status code {response.StatusCode}.");
                return false;
            }
        }

        private async Task RetrieveViaDicomWeb(InferenceRequest inferenceRequest, RequestInputDataResource source, Dictionary<string, FileStorageInfo> retrievedInstance)
        {
            Guard.Against.Null(inferenceRequest, nameof(inferenceRequest));
            Guard.Against.Null(retrievedInstance, nameof(retrievedInstance));

            var authenticationHeaderValue = AuthenticationHeaderValueExtensions.ConvertFrom(source.ConnectionDetails.AuthType, source.ConnectionDetails.AuthId);

            var dicomWebClient = new DicomWebClient(_httpClientFactory.CreateClient("dicomweb"), _loggerFactory.CreateLogger<DicomWebClient>());
            dicomWebClient.ConfigureServiceUris(new Uri(source.ConnectionDetails.Uri, UriKind.Absolute));

            if (!(authenticationHeaderValue is null))
            {
                dicomWebClient.ConfigureAuthentication(authenticationHeaderValue);
            }

            foreach (var input in inferenceRequest.InputMetadata.Inputs)
            {
                switch (input.Type)
                {
                    case InferenceRequestType.DicomUid:
                        await RetrieveStudies(inferenceRequest.TransactionId, dicomWebClient, input.Studies, inferenceRequest.StoragePath, retrievedInstance);
                        break;

                    case InferenceRequestType.DicomPatientId:
                        await QueryStudies(inferenceRequest.TransactionId, dicomWebClient, inferenceRequest, retrievedInstance, $"{DicomTag.PatientID.Group:X4}{DicomTag.PatientID.Element:X4}", input.PatientId);
                        break;

                    case InferenceRequestType.AccessionNumber:
                        foreach (var accessionNumber in input.AccessionNumber)
                        {
                            await QueryStudies(inferenceRequest.TransactionId, dicomWebClient, inferenceRequest, retrievedInstance, $"{DicomTag.AccessionNumber.Group:X4}{DicomTag.AccessionNumber.Element:X4}", accessionNumber);
                        }
                        break;

                    case InferenceRequestType.FhireResource:
                        continue;
                    default:
                        throw new InferenceRequestException($"The 'inputMetadata' type '{input.Type}' specified is not supported.");
                }
            }
        }

        private async Task QueryStudies(string transactionId, DicomWebClient dicomWebClient, InferenceRequest inferenceRequest, Dictionary<string, FileStorageInfo> retrievedInstance, string dicomTag, string queryValue)
        {
            Guard.Against.NullOrWhiteSpace(transactionId, nameof(transactionId));
            Guard.Against.Null(dicomWebClient, nameof(dicomWebClient));
            Guard.Against.Null(inferenceRequest, nameof(inferenceRequest));
            Guard.Against.Null(retrievedInstance, nameof(retrievedInstance));
            Guard.Against.NullOrWhiteSpace(dicomTag, nameof(dicomTag));
            Guard.Against.NullOrWhiteSpace(queryValue, nameof(queryValue));

            _logger.Log(LogLevel.Information, $"Performing QIDO with {dicomTag}={queryValue}.");
            var queryParams = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            queryParams.Add(dicomTag, queryValue);

            var studies = new List<RequestedStudy>();
            await foreach (var result in dicomWebClient.Qido.SearchForStudies<DicomDataset>(queryParams))
            {
                if (result.Contains(DicomTag.StudyInstanceUID))
                {
                    var studyInstanceUid = result.GetString(DicomTag.StudyInstanceUID);
                    studies.Add(new RequestedStudy
                    {
                        StudyInstanceUid = studyInstanceUid
                    });
                    _logger.Log(LogLevel.Debug, $"Study {studyInstanceUid} found with QIDO query {dicomTag}={queryValue}.");
                }
                else
                {
                    _logger.Log(LogLevel.Warning, $"Instance {result.GetSingleValueOrDefault(DicomTag.SOPInstanceUID, "UKNOWN")} does not contain StudyInstanceUid.");
                }
            }

            if (studies.Count != 0)
            {
                await RetrieveStudies(transactionId, dicomWebClient, studies, inferenceRequest.StoragePath, retrievedInstance);
            }
            else
            {
                _logger.Log(LogLevel.Warning, $"No studies found with specified query parameter {dicomTag}={queryValue}.");
            }
        }

        private async Task RetrieveStudies(string transactionId, IDicomWebClient dicomWebClient, IList<RequestedStudy> studies, string storagePath, Dictionary<string, FileStorageInfo> retrievedInstance)
        {
            Guard.Against.NullOrWhiteSpace(transactionId, nameof(transactionId));
            Guard.Against.Null(studies, nameof(studies));
            Guard.Against.Null(storagePath, nameof(storagePath));
            Guard.Against.Null(retrievedInstance, nameof(retrievedInstance));

            foreach (var study in studies)
            {
                if (study.Series.IsNullOrEmpty())
                {
                    _logger.Log(LogLevel.Information, $"Retrieving study {study.StudyInstanceUid}");
                    var files = dicomWebClient.Wado.Retrieve(study.StudyInstanceUid);
                    await SaveFiles(transactionId, files, storagePath, retrievedInstance);
                }
                else
                {
                    await RetrieveSeries(transactionId, dicomWebClient, study, storagePath, retrievedInstance);
                }
            }
        }

        private async Task RetrieveSeries(string transactionId, IDicomWebClient dicomWebClient, RequestedStudy study, string storagePath, Dictionary<string, FileStorageInfo> retrievedInstance)
        {
            Guard.Against.NullOrWhiteSpace(transactionId, nameof(transactionId));
            Guard.Against.Null(study, nameof(study));
            Guard.Against.Null(storagePath, nameof(storagePath));
            Guard.Against.Null(retrievedInstance, nameof(retrievedInstance));

            foreach (var series in study.Series)
            {
                if (series.Instances.IsNullOrEmpty())
                {
                    _logger.Log(LogLevel.Information, $"Retrieving series {series.SeriesInstanceUid}");
                    var files = dicomWebClient.Wado.Retrieve(study.StudyInstanceUid, series.SeriesInstanceUid);
                    await SaveFiles(transactionId, files, storagePath, retrievedInstance);
                }
                else
                {
                    await RetrieveInstances(transactionId, dicomWebClient, study.StudyInstanceUid, series, storagePath, retrievedInstance);
                }
            }
        }

        private async Task RetrieveInstances(string transactionId, IDicomWebClient dicomWebClient, string studyInstanceUid, RequestedSeries series, string storagePath, Dictionary<string, FileStorageInfo> retrievedInstance)
        {
            Guard.Against.NullOrWhiteSpace(transactionId, nameof(transactionId));
            Guard.Against.NullOrWhiteSpace(studyInstanceUid, nameof(studyInstanceUid));
            Guard.Against.Null(series, nameof(series));
            Guard.Against.Null(storagePath, nameof(storagePath));
            Guard.Against.Null(retrievedInstance, nameof(retrievedInstance));

            var count = retrievedInstance.Count;
            foreach (var instance in series.Instances)
            {
                foreach (var sopInstanceUid in instance.SopInstanceUid)
                {
                    _logger.Log(LogLevel.Information, $"Retrieving instance {sopInstanceUid}");
                    var file = await dicomWebClient.Wado.Retrieve(studyInstanceUid, series.SeriesInstanceUid, sopInstanceUid);
                    if (file is null) continue;
                    var fileStorageInfo = new FileStorageInfo(transactionId, storagePath, count.ToString(), ".dcm");
                    if (retrievedInstance.ContainsKey(fileStorageInfo.FilePath))
                    {
                        _logger.Log(LogLevel.Warning, $"Instance '{fileStorageInfo.FilePath}' already retrieved/stored.");
                        continue;
                    }

                    SaveFile(file, fileStorageInfo);
                    retrievedInstance.Add(fileStorageInfo.FilePath, fileStorageInfo);
                    count++;
                }
            }
        }

        private async Task SaveFiles(string transactionId, IAsyncEnumerable<DicomFile> files, string storagePath, Dictionary<string, FileStorageInfo> retrievedInstance)
        {
            Guard.Against.NullOrWhiteSpace(transactionId, nameof(transactionId));
            Guard.Against.Null(files, nameof(files));
            Guard.Against.Null(storagePath, nameof(storagePath));
            Guard.Against.Null(retrievedInstance, nameof(retrievedInstance));

            var count = retrievedInstance.Count;
            await foreach (var file in files)
            {
                count++;
                var instance = new FileStorageInfo(transactionId, storagePath, count.ToString(), ".dcm");
                if (retrievedInstance.ContainsKey(instance.FilePath))
                {
                    _logger.Log(LogLevel.Warning, $"Instance '{instance.FilePath}' already retrieved/stored.");
                    continue;
                }

                SaveFile(file, instance);
                retrievedInstance.Add(instance.FilePath, instance);
                _logger.Log(LogLevel.Debug, $"Instance saved in {instance.FilePath}.");
            }
        }

        private void SaveFile(DicomFile file, FileStorageInfo instanceStorageInfo)
        {
            Guard.Against.Null(file, nameof(file));
            Guard.Against.Null(instanceStorageInfo, nameof(instanceStorageInfo));

            Policy.Handle<Exception>()
                .WaitAndRetry(3,
                (retryAttempt) =>
                {
                    return retryAttempt == 1 ? TimeSpan.FromMilliseconds(250) : TimeSpan.FromMilliseconds(500);
                },
                (exception, retryCount, context) =>
                {
                    _logger.Log(LogLevel.Error, "Failed to save instance, retry count={retryCount}: {exception}", retryCount, exception);
                })
                .Execute(() =>
                {
                    _logger.Log(LogLevel.Information, "Saving DICOM instance {path}.", instanceStorageInfo.FilePath);
                    _dicomToolkit.Save(file, instanceStorageInfo.FilePath);
                    _logger.Log(LogLevel.Debug, "Instance saved successfully.");
                });
        }

        #endregion Data Retrieval
    }
}
