namespace NINA.Plugin.Speckle.Imaging {

    public enum FringePaneMode {
        Average = 0,
        Frame = 1,
        Autocorrelation = 2
    }

    public enum FringeStretchMode {
        Asinh = 0,
        SignedSqrt = 1,
        Log = 2,
        Linear = 3
    }

    public enum FringeApodizationMode {
        None = 0,
        Hann = 1,
        Tukey = 2
    }

    public enum FringePaneOrientation {
        SideBySide = 0,
        Below = 1
    }
}
