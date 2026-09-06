using System.ComponentModel.Composition;

namespace NINA.Plugin.Speckle.Services {

    [Export(typeof(ISpeckleOptionsProvider))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    public class SpeckleOptionsProvider : ISpeckleOptionsProvider {

        [ImportingConstructor]
        public SpeckleOptionsProvider(Speckle speckle) {
            Current = speckle;
        }

        public Speckle Current { get; }
    }
}
