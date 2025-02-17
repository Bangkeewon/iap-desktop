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

using Google.Apis.Util;
using Google.Solutions.IapDesktop.Application.Settings;
using Microsoft.Win32;
using System;

namespace Google.Solutions.IapDesktop.Application.Services.Settings
{
    /// <summary>
    /// Base class for all settings repositories.
    /// </summary>
    public abstract class SettingsRepositoryBase<TSettings> : IDisposable
        where TSettings : IRegistrySettingsCollection
    {
        protected readonly RegistryKey baseKey;

        protected SettingsRepositoryBase(RegistryKey baseKey)
        {
            this.baseKey = baseKey;
        }

        public virtual TSettings GetSettings()
        {
            return LoadSettings(this.baseKey);
        }

        public virtual void SetSettings(TSettings settings)
        {
            settings.Save(this.baseKey);
        }

        public void ClearSettings()
        {
            // Delete values, but keep any subkeys.
            foreach (string valueName in this.baseKey.GetValueNames())
            {
                this.baseKey.DeleteValue(valueName);
            }
        }

        protected abstract TSettings LoadSettings(RegistryKey key);

        //---------------------------------------------------------------------
        // IDisposable.
        //---------------------------------------------------------------------

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.baseKey.Dispose();
            }
        }
    }

    /// <summary>
    /// Base class for settings repositories that support group policies.
    /// </summary>
    public abstract class PolicyEnabledSettingsRepository<TSettings>
        : SettingsRepositoryBase<TSettings>
        where TSettings : IRegistrySettingsCollection
    {
        private readonly RegistryKey machinePolicyKey;
        private readonly RegistryKey userPolicyKey;

        public PolicyEnabledSettingsRepository(
            RegistryKey settingsKey,
            RegistryKey machinePolicyKey,
            RegistryKey userPolicyKey) : base(settingsKey)
        {
            Utilities.ThrowIfNull(settingsKey, nameof(settingsKey));

            this.machinePolicyKey = machinePolicyKey;
            this.userPolicyKey = userPolicyKey;
        }
        protected override TSettings LoadSettings(RegistryKey key)
            => LoadSettings(key, this.machinePolicyKey, this.userPolicyKey);

        protected abstract TSettings LoadSettings(
            RegistryKey settingsKey,
            RegistryKey machinePolicyKey,
            RegistryKey userPolicyKey);

        public bool IsPolicyPresent
            => this.machinePolicyKey != null || this.userPolicyKey != null;
    }
}
