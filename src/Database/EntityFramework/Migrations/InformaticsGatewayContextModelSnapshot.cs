﻿// <auto-generated />
using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Monai.Deploy.InformaticsGateway.Database.EntityFramework;

#nullable disable

namespace Monai.Deploy.InformaticsGateway.Database.Migrations
{
    [DbContext(typeof(InformaticsGatewayContext))]
    partial class InformaticsGatewayContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder.HasAnnotation("ProductVersion", "6.0.10");

            modelBuilder.Entity("Monai.Deploy.InformaticsGateway.Api.DestinationApplicationEntity", b =>
                {
                    b.Property<string>("Name")
                        .HasColumnType("TEXT");

                    b.Property<string>("AeTitle")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<DateTime>("DateTimeCreated")
                        .HasColumnType("TEXT");

                    b.Property<string>("HostIp")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<int>("Port")
                        .HasColumnType("INTEGER");

                    b.HasKey("Name");

                    b.HasIndex(new[] { "Name" }, "idx_destination_name")
                        .IsUnique();

                    b.HasIndex(new[] { "Name", "AeTitle", "HostIp", "Port" }, "idx_source_all")
                        .IsUnique();

                    b.ToTable("DestinationApplicationEntities");
                });

            modelBuilder.Entity("Monai.Deploy.InformaticsGateway.Api.MonaiApplicationEntity", b =>
                {
                    b.Property<string>("Name")
                        .HasColumnType("TEXT")
                        .HasColumnOrder(0);

                    b.Property<string>("AeTitle")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("AllowedSopClasses")
                        .HasColumnType("TEXT");

                    b.Property<DateTime>("DateTimeCreated")
                        .HasColumnType("TEXT");

                    b.Property<string>("Grouping")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("IgnoredSopClasses")
                        .HasColumnType("TEXT");

                    b.Property<uint>("Timeout")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Workflows")
                        .HasColumnType("TEXT");

                    b.HasKey("Name");

                    b.HasIndex(new[] { "Name" }, "idx_monaiae_name")
                        .IsUnique();

                    b.ToTable("MonaiApplicationEntities");
                });

            modelBuilder.Entity("Monai.Deploy.InformaticsGateway.Api.Rest.InferenceRequest", b =>
                {
                    b.Property<Guid>("InferenceRequestId")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("TEXT");

                    b.Property<string>("InputMetadata")
                        .HasColumnType("TEXT");

                    b.Property<string>("InputResources")
                        .HasColumnType("TEXT");

                    b.Property<string>("OutputResources")
                        .HasColumnType("TEXT");

                    b.Property<byte>("Priority")
                        .HasColumnType("INTEGER");

                    b.Property<int>("State")
                        .HasColumnType("INTEGER");

                    b.Property<int>("Status")
                        .HasColumnType("INTEGER");

                    b.Property<string>("TransactionId")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<int>("TryCount")
                        .HasColumnType("INTEGER");

                    b.HasKey("InferenceRequestId");

                    b.HasIndex(new[] { "InferenceRequestId" }, "idx_inferencerequest_inferencerequestid")
                        .IsUnique();

                    b.HasIndex(new[] { "State" }, "idx_inferencerequest_state");

                    b.HasIndex(new[] { "TransactionId" }, "idx_inferencerequest_transactionid")
                        .IsUnique();

                    b.ToTable("InferenceRequests");
                });

            modelBuilder.Entity("Monai.Deploy.InformaticsGateway.Api.SourceApplicationEntity", b =>
                {
                    b.Property<string>("Name")
                        .HasColumnType("TEXT");

                    b.Property<string>("AeTitle")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<DateTime>("DateTimeCreated")
                        .HasColumnType("TEXT");

                    b.Property<string>("HostIp")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.HasKey("Name");

                    b.HasIndex(new[] { "Name", "AeTitle", "HostIp" }, "idx_source_all")
                        .IsUnique()
                        .HasDatabaseName("idx_source_all1");

                    b.HasIndex(new[] { "Name" }, "idx_source_name")
                        .IsUnique();

                    b.ToTable("SourceApplicationEntities");
                });

            modelBuilder.Entity("Monai.Deploy.InformaticsGateway.Api.Storage.Payload", b =>
                {
                    b.Property<Guid>("PayloadId")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("TEXT");

                    b.Property<string>("CorrelationId")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<DateTime>("DateTimeCreated")
                        .HasColumnType("TEXT");

                    b.Property<string>("Files")
                        .HasColumnType("TEXT");

                    b.Property<string>("Key")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<int>("RetryCount")
                        .HasColumnType("INTEGER");

                    b.Property<int>("State")
                        .HasColumnType("INTEGER");

                    b.Property<uint>("Timeout")
                        .HasColumnType("INTEGER");

                    b.HasKey("PayloadId");

                    b.HasIndex(new[] { "CorrelationId", "PayloadId" }, "idx_payload_ids")
                        .IsUnique();

                    b.HasIndex(new[] { "State" }, "idx_payload_state");

                    b.ToTable("Payloads");
                });

            modelBuilder.Entity("Monai.Deploy.InformaticsGateway.Database.Api.StorageMetadataWrapper", b =>
                {
                    b.Property<string>("CorrelationId")
                        .HasColumnType("TEXT");

                    b.Property<string>("Identity")
                        .HasColumnType("TEXT");

                    b.Property<DateTime>("DateTimeCreated")
                        .HasColumnType("TEXT");

                    b.Property<bool>("IsUploaded")
                        .HasColumnType("INTEGER");

                    b.Property<string>("TypeName")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("Value")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.HasKey("CorrelationId", "Identity");

                    b.HasIndex(new[] { "CorrelationId" }, "idx_storagemetadata_correlation");

                    b.HasIndex(new[] { "CorrelationId", "Identity" }, "idx_storagemetadata_ids");

                    b.HasIndex(new[] { "IsUploaded" }, "idx_storagemetadata_uploaded");

                    b.ToTable("StorageMetadataWrapperEntities");
                });
#pragma warning restore 612, 618
        }
    }
}
