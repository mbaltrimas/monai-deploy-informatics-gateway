/*
 * Copyright 2021-2022 MONAI Consortium
 * Copyright 2021 NVIDIA Corporation
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

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Monai.Deploy.InformaticsGateway.Api.Rest;

namespace Monai.Deploy.InformaticsGateway.Database.EntityFramework.Configuration
{
#pragma warning disable CS8603 // Possible null reference return.
#pragma warning disable CS8604 // Possible null reference argument.
    internal class InferenceRequestConfiguration : IEntityTypeConfiguration<InferenceRequest>
    {
        public void Configure(EntityTypeBuilder<InferenceRequest> builder)
        {
            var reqestInputResourceComparer = new ValueComparer<IList<RequestInputDataResource>>(
                (c1, c2) => c1.SequenceEqual(c2),
                c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                c => c.ToList());

            var reqestOutputResourceComparer = new ValueComparer<IList<RequestOutputDataResource>>(
                (c1, c2) => c1.SequenceEqual(c2),
                c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                c => c.ToList());

            var jsonSerializerSettings = new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            builder.HasKey(j => j.InferenceRequestId);

            builder.Property(j => j.TransactionId).IsRequired();
            builder.Property(j => j.Priority).IsRequired();

            builder.Property(j => j.InputMetadata).IsRequired(false).HasConversion(
                        v => JsonSerializer.Serialize(v, jsonSerializerSettings),
                        v => JsonSerializer.Deserialize<InferenceRequestMetadata>(v, jsonSerializerSettings));

            builder.Property(j => j.InputResources).IsRequired(false).HasConversion(
                        v => JsonSerializer.Serialize(v, jsonSerializerSettings),
                        v => JsonSerializer.Deserialize<List<RequestInputDataResource>>(v, jsonSerializerSettings))
                .Metadata.SetValueComparer(reqestInputResourceComparer);

            builder.Property(j => j.OutputResources).IsRequired(false).HasConversion(
                        v => JsonSerializer.Serialize(v, jsonSerializerSettings),
                        v => JsonSerializer.Deserialize<List<RequestOutputDataResource>>(v, jsonSerializerSettings))
                .Metadata.SetValueComparer(reqestOutputResourceComparer);

            builder.Property(j => j.State).IsRequired();
            builder.Property(j => j.Status).IsRequired();
            builder.Property(j => j.TryCount).IsRequired();
            builder.Property(j => j.CreatedBy).IsRequired(false);
            builder.Property(j => j.DateTimeCreated).IsRequired();

            builder.Ignore(p => p.Application);

            builder.HasIndex(p => p.State, "idx_inferencerequest_state");
            builder.HasIndex(p => p.InferenceRequestId, "idx_inferencerequest_inferencerequestid").IsUnique();
            builder.HasIndex(p => p.TransactionId, "idx_inferencerequest_transactionid").IsUnique();
        }
    }
#pragma warning restore CS8604 // Possible null reference argument.
#pragma warning restore CS8603 // Possible null reference return.
}
