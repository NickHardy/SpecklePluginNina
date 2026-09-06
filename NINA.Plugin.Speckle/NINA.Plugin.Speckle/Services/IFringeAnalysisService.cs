using NINA.Core.Utility;
using NINA.Plugin.Speckle.Imaging;
using System;

namespace NINA.Plugin.Speckle.Services {

    public interface IFringeAnalysisService {

        bool IsEnabled { get; }

        FringeAnalysisResult Latest { get; }

        FringePaneMode PaneMode { get; set; }

        string CachedReferenceLabel { get; }

        event EventHandler<FringeAnalysisResult> Updated;

        void BeginRun(FringeRunContext context);

        void Push(ushort[] pixels, int width, int height, ObservableRectangle roi);

        void EndRun();

        void Reset();
    }
}
