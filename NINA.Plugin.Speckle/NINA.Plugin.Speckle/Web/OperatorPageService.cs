using NINA.Equipment.Interfaces.Mediator;
using NINA.Plugin.Speckle.Services;
using NINA.Plugin.Speckle.Workflow;
using NINA.Profile.Interfaces;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;

namespace NINA.Plugin.Speckle.Web {

    [Export(typeof(IOperatorPageService))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    public class OperatorPageService : IOperatorPageService {

        [ImportingConstructor]
        public OperatorPageService(
            IProfileService profileService,
            ITelescopeMediator telescopeMediator,
            ISpeckleWorkflowCoordinator coordinator,
            ISpeckleOptionsProvider optionsProvider) {
            OperatorPageHost.Attach(profileService, telescopeMediator, coordinator, optionsProvider);
        }

        public OperatorState State => OperatorPageHost.State;

        public bool IsRunning => OperatorPageHost.IsRunning;

        public int Port => OperatorPageHost.Port;

        public IReadOnlyList<string> Urls => OperatorPageHost.Urls;

        public string CurrentTargetName => OperatorPageHost.CurrentTargetName;

        public event EventHandler Changed {
            add { OperatorPageHost.Changed += value; }
            remove { OperatorPageHost.Changed -= value; }
        }

        public bool Acquire(string owner) {
            return OperatorPageHost.Acquire(owner);
        }

        public void Release(string owner) {
            OperatorPageHost.Release(owner);
        }

        public void ApplyOptions() {
            OperatorPageHost.ApplyOptions();
        }

        public void Stop() {
            OperatorPageHost.Stop();
        }
    }
}
