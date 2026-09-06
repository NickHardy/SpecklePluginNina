using NINA.Core.Model;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.Speckle.Services {

    public enum LightPath {
        Wide,
        Science
    }

    public interface ILightPathService {

        LightPath? CurrentPath { get; }

        Task SwitchAsync(LightPath path, IProgress<ApplicationStatus> progress, CancellationToken ct);
    }
}
