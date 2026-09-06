using NINA.Equipment.Interfaces;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Plugin.Speckle.Web;
using NINA.Profile.Interfaces;
using System.Collections.Generic;
using System.ComponentModel.Composition;

namespace NINA.Plugin.Speckle.ManualMount {

    [Export(typeof(IEquipmentProvider))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    public class ManualTelescopeProvider : IEquipmentProvider<ITelescope> {
        private readonly ITelescope instance;

        [ImportingConstructor]
        public ManualTelescopeProvider(IProfileService profileService, IOperatorPageService operatorPage) {
            instance = new ManualTelescope(profileService, operatorPage);
        }

        public string Name => "Speckle";

        public IList<ITelescope> GetEquipment() {
            return new List<ITelescope> { instance };
        }
    }
}
