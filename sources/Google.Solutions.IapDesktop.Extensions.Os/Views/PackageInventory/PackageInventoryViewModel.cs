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

using Google.Solutions.Common.Diagnostics;
using Google.Solutions.Common.Locator;
using Google.Solutions.IapDesktop.Application;
using Google.Solutions.IapDesktop.Application.ObjectModel;
using Google.Solutions.IapDesktop.Application.Services.Integration;
using Google.Solutions.IapDesktop.Application.Services.ProjectModel;
using Google.Solutions.IapDesktop.Application.Util;
using Google.Solutions.IapDesktop.Application.Views.ProjectExplorer;
using Google.Solutions.IapDesktop.Extensions.Os.Inventory;
using Google.Solutions.IapDesktop.Extensions.Os.Services.Inventory;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

#pragma warning disable CA1822 // Mark members as static

namespace Google.Solutions.IapDesktop.Extensions.Os.Views.PackageInventory
{
    public class PackageInventoryViewModel
        : ModelCachingViewModelBase<IProjectModelNode, PackageInventoryModel>
    {
        private const int ModelCacheCapacity = 5;

        private readonly PackageInventoryType inventoryType;
        private readonly IServiceProvider serviceProvider;

        private string filter;
        private bool isLoading;
        private bool isPackageListEnabled = false;
        private string windowTitle;
        private bool isInformationBarVisible = true;

        private string WindowTitlePrefix =>
                this.inventoryType == PackageInventoryType.AvailablePackages
                    ? "Available updates"
                    : "Installed packages";

        public string InformationText => "OS inventory data not available";

        public PackageInventoryViewModel(
            IServiceProvider serviceProvider,
            PackageInventoryType inventoryType)
            : base(ModelCacheCapacity)
        {
            this.serviceProvider = serviceProvider;
            this.inventoryType = inventoryType;
        }

        public void ResetWindowTitle()
        {
            this.WindowTitle = WindowTitlePrefix;
        }

        //---------------------------------------------------------------------
        // Static helpers.
        //---------------------------------------------------------------------

        private static bool IsInstanceMatch(InstanceLocator locator, string term)
        {
            term = term.ToLowerInvariant();

            return locator.Zone.Contains(term) ||
                   locator.Name.Contains(term);
        }

        private static bool IsPackageMatch(IPackage package, string term)
        {
            term = term.ToLowerInvariant();

            return
                (package.PackageType != null && package.PackageType.ToLowerInvariant().Contains(term)) ||
                (package.Architecture != null && package.Architecture.ToLowerInvariant().Contains(term)) ||
                (package.Description != null && package.Description.ToLowerInvariant().Contains(term)) ||
                (package.PackageId != null && package.PackageId.ToLowerInvariant().Contains(term)) ||
                (package.Version != null && package.Version.ToLowerInvariant().Contains(term));
        }

        //---------------------------------------------------------------------
        // Observable "output" properties.
        //---------------------------------------------------------------------

        public RangeObservableCollection<PackageInventoryModel.Item> AllPackages { get; }
            = new RangeObservableCollection<PackageInventoryModel.Item>();

        public RangeObservableCollection<PackageInventoryModel.Item> FilteredPackages { get; }
            = new RangeObservableCollection<PackageInventoryModel.Item>();

        public bool IsPackageListEnabled
        {
            get => this.isPackageListEnabled;
            set
            {
                this.isPackageListEnabled = value;
                RaisePropertyChange();
            }
        }

        public bool IsLoading
        {
            get => this.isLoading;
            set
            {
                this.isLoading = value;
                RaisePropertyChange();
            }
        }

        public string WindowTitle
        {
            get => this.windowTitle;
            set
            {
                this.windowTitle = value;
                RaisePropertyChange();
            }
        }

        public bool IsInformationBarVisible
        {
            get => this.isInformationBarVisible;
            private set
            {
                this.isInformationBarVisible = value;
                RaisePropertyChange();
            }
        }

        //---------------------------------------------------------------------
        // "Input" properties.
        //---------------------------------------------------------------------

        public string Filter
        {
            get => this.filter;
            set
            {
                this.filter = value;

                IEnumerable<PackageInventoryModel.Item> candidates = this.AllPackages;

                if (!string.IsNullOrWhiteSpace(this.filter))
                {
                    // Treat filter as an AND-combination of terms.

                    foreach (var term in this.filter
                        .Split(' ')
                        .Select(t => t.Trim())
                        .Where(t => !string.IsNullOrWhiteSpace(t)))
                    {
                        candidates = candidates.Where(p =>
                                IsPackageMatch(p.Package, term) ||
                                IsInstanceMatch(p.Instance, term))
                            .ToList();
                    }
                }

                this.FilteredPackages.Clear();
                this.FilteredPackages.AddRange(candidates);

                RaisePropertyChange((PackageInventoryViewModel m) => m.FilteredPackages);
                RaisePropertyChange();
            }
        }


        //---------------------------------------------------------------------
        // ModelCachingViewModelBase.
        //---------------------------------------------------------------------

        internal static CommandState GetCommandState(IProjectModelNode node)
        {
            return (node is IProjectModelInstanceNode ||
                    node is IProjectModelZoneNode ||
                    node is IProjectModelProjectNode)
                ? CommandState.Enabled
                : CommandState.Unavailable;
        }

        protected override async Task<PackageInventoryModel> LoadModelAsync(
            IProjectModelNode node,
            CancellationToken token)
        {
            using (ApplicationTraceSources.Default.TraceMethod().WithParameters(node))
            {
                try
                {
                    this.IsLoading = true;

                    //
                    // Reset window title, otherwise the default or previous title
                    // stays while data is loading.
                    //
                    this.WindowTitle = this.WindowTitlePrefix;

                    var jobService = this.serviceProvider.GetService<IJobService>();

                    // Load data using a job so that the task is retried in case
                    // of authentication issues.
                    return await jobService.RunInBackground(
                        new JobDescription(
                            $"Loading inventory for {node.DisplayName}",
                            JobUserFeedbackType.BackgroundFeedback),
                        async jobToken =>
                        {
                            using (var inventoryService = this.serviceProvider.GetService<IInventoryService>())
                            {
                                return await PackageInventoryModel.LoadAsync(
                                        inventoryService,
                                        this.inventoryType,
                                        node,
                                        jobToken)
                                    .ConfigureAwait(false);
                            }
                        }).ConfigureAwait(true);  // Back to original (UI) thread.
                }
                finally
                {
                    this.IsLoading = false;
                }
            }
        }

        protected override void ApplyModel(bool cached)
        {
            this.AllPackages.Clear();
            this.FilteredPackages.Clear();

            if (this.Model == null)
            {
                // Unsupported node.
                this.IsPackageListEnabled = false;
                this.IsInformationBarVisible = false;
                this.WindowTitle = this.WindowTitlePrefix;
            }
            else
            {
                this.IsPackageListEnabled = true;
                this.IsInformationBarVisible = !this.Model.IsInventoryDataAvailable;
                this.WindowTitle = this.WindowTitlePrefix + $": {this.Model.DisplayName}";
                this.AllPackages.AddRange(this.Model.Packages);
            }

            // Reset filter, implicitly populating the FilteredPackages property.
            this.Filter = string.Empty;
        }
    }
}
