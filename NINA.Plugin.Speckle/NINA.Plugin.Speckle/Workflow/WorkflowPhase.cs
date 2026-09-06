namespace NINA.Plugin.Speckle.Workflow {

    public enum WorkflowPhase {
        Idle,
        ListLoaded,
        PickingTarget,
        WaitingForWindow,
        SwitchingToWide,
        AwaitingSlewConfirmation,
        Slewing,
        Centering,
        SwitchingToScience,
        PositioningRoi,
        CalibratingExposure,
        AwaitingImageConfirmation,
        RunningVideoExposures,
        TargetComplete,
        Faulted,
        Stopped
    }
}
