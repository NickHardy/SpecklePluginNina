using NINA.Astrometry;
using NINA.Core.Model;
using NINA.Plugin.Speckle.Model;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.Speckle.Services {

    public interface ISimbadUtils {

        Task<List<SimbadStarCluster>> FindSimbadStarClusters(IProgress<ApplicationStatus> externalProgress, CancellationToken token, Coordinates coords, double maxDistance = 5d);

        Task<List<ReferenceStar>> FindSimbadSaoStars(IProgress<ApplicationStatus> externalProgress, CancellationToken token, Coordinates coords, double maxDistance = 5d, double minMag = 0.0d, double maxMag = 10.0d);

        Task<List<ReferenceStar>> FindSingleBrightStars(IProgress<ApplicationStatus> externalProgress, CancellationToken token, Coordinates coords, double maxDistance = 5d, double minMag = 0.0d, double maxMag = 10.0d);

        Task<ReferenceStar> GetStarByPosition(IProgress<ApplicationStatus> externalProgress, CancellationToken token, double ra, double dec, double targetMag);

        Task<List<SimbadBinaryStar>> FindSimbadBinaryStars(IProgress<ApplicationStatus> externalProgress, CancellationToken token, Coordinates coords, double maxDistance = 5d);

        Task<List<SimbadGalaxy>> FindSimbadGalaxies(IProgress<ApplicationStatus> externalProgress, CancellationToken token, Coordinates coords, double maxDistance = 10d, double maxMag = 18.0d);
    }
}
