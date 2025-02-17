﻿//
// Copyright 2020 Google LLC
//
// Licensed to the Apache Software Foundation (ASF) under one
// or more contributor license agreements.  See the NOTICE file
// distributed with this work for additional information
// regarding copyright ownership.  The ASF licenses this file
// to you under the Apache License, Version 2.0 (the
// "License"); you may not use this file except in compliance
// with the License.  You may obtain a copy of the License at
// 
//   http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing,
// software distributed under the License is distributed on an
// "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY
// KIND, either express or implied.  See the License for the
// specific language governing permissions and limitations
// under the License.
//

using Google.Apis.Compute.v1.Data;
using Google.Solutions.Common.ApiExtensions.Instance;
using Google.Solutions.Common.Locator;
using Google.Solutions.Common.Test.Integration;
using Google.Solutions.Common.Util;
using Google.Solutions.Ssh.Auth;
using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Google.Solutions.Ssh.Test
{
    internal static class InstanceUtil
    {
        public static async Task<IPAddress> PublicIpAddressForInstanceAsync(
            InstanceLocator instanceLocator)
        {
            using (var service = TestProject.CreateComputeService())
            {
                var instance = await service
                    .Instances.Get(
                            instanceLocator.ProjectId,
                            instanceLocator.Zone,
                            instanceLocator.Name)
                    .ExecuteAsync()
                    .ConfigureAwait(false);
                var ip = instance
                    .NetworkInterfaces
                    .EnsureNotNull()
                    .Where(nic => nic.AccessConfigs != null)
                    .SelectMany(nic => nic.AccessConfigs)
                    .EnsureNotNull()
                    .Where(accessConfig => accessConfig.Type == "ONE_TO_ONE_NAT")
                    .Select(accessConfig => accessConfig.NatIP)
                    .FirstOrDefault();
                return IPAddress.Parse(ip);
            }
        }

        public static async Task AddPublicKeyToMetadataAsync(
            InstanceLocator instanceLocator,
            string username,
            ISshKeyPair key)
        {
            using (var service = TestProject.CreateComputeService())
            {
                await service.Instances
                    .AddMetadataAsync(
                        instanceLocator,
                        new Metadata()
                        {
                            Items = new[]
                            {
                                new Metadata.ItemsData()
                                {
                                    Key = "ssh-keys",
                                    Value = $"{username}:{key.Type} {key.PublicKeyString} {username}"
                                }
                            }
                        },
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }

        public static async Task<ISshKeyPair> CreateEphemeralKeyAndPushKeyToMetadata(
            InstanceLocator instanceLocator,
            string username,
            SshKeyType keyType)
        {
            var key = SshKeyPair.NewEphemeralKeyPair(keyType);
            await AddPublicKeyToMetadataAsync(
                    instanceLocator,
                    username,
                    key)
                .ConfigureAwait(false);
            return key;
        }
    }
}
