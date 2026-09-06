using NINA.Core.Utility;
using NINA.Plugin.Speckle.Imaging;
using NINA.Plugin.Speckle.Services;
using System;
using System.Threading;
using Xunit;

namespace NINA.Plugin.Speckle.Test {

    public class FringeAnalysisServiceTests {
        private const int FrameSize = TestVectorData.Size;

        private static FringeRunContext Context(string label, bool isReference = false) {
            return new FringeRunContext {
                Label = label,
                IsReference = isReference,
                RoiX = 0,
                RoiY = 0,
                RoiWidth = FrameSize,
                RoiHeight = FrameSize,
                ArcsecPerPixel = TestVectorData.Scalar("generator", "arcsec_per_pixel"),
                ScaleSource = "test",
                Binning = 1,
                TotalExposureCount = TestVectorData.FrameCount
            };
        }

        [Fact]
        public void Service_AccumulatesPushedFramesAndPublishesAFinalResult() {
            var frames = TestVectorData.LoadFrames();
            using (var service = new FringeAnalysisService(new FakeOptionsProvider(), null)) {
                FringeAnalysisResult final = null;
                var done = new ManualResetEventSlim(false);
                service.Updated += (sender, result) => {
                    if (result.IsFinal) {
                        final = result;
                        done.Set();
                    }
                };

                service.BeginRun(Context("T1"));
                foreach (var frame in frames) {
                    service.Push(frame, FrameSize, FrameSize, new ObservableRectangle(0, 0, FrameSize, FrameSize));
                    Thread.Sleep(2);
                }
                service.EndRun();

                Assert.True(done.Wait(TimeSpan.FromSeconds(20)), "no final fringe result was published");
                Assert.NotNull(final);
                Assert.Equal("T1", final.Label);
                Assert.Equal(FrameSize, final.Size);
                Assert.Equal(frames.Length, final.FramesCaptured);
                Assert.True(final.FramesAnalysed > 0);
                Assert.Equal(frames.Length, final.FramesAnalysed + final.FramesDropped);
                Assert.NotNull(final.Image);
                Assert.True(final.Image.IsFrozen);
                Assert.Equal(FrameSize, final.Image.PixelWidth);
                Assert.NotNull(final.MetricsLine);
                Assert.Contains("frames", final.MetricsLine);
            }
        }

        [Fact]
        public void Service_DropsFramesInsteadOfBlockingTheCaller() {
            var frames = TestVectorData.LoadFrames();
            using (var service = new FringeAnalysisService(new FakeOptionsProvider(), null)) {
                FringeAnalysisResult final = null;
                var done = new ManualResetEventSlim(false);
                service.Updated += (sender, result) => {
                    if (result.IsFinal) {
                        final = result;
                        done.Set();
                    }
                };

                service.BeginRun(Context("Flood"));
                var pushes = 0;
                var watch = System.Diagnostics.Stopwatch.StartNew();
                for (var i = 0; i < 400; i++) {
                    service.Push(frames[i % frames.Length], FrameSize, FrameSize, new ObservableRectangle(0, 0, FrameSize, FrameSize));
                    pushes++;
                }
                watch.Stop();
                service.EndRun();

                Assert.True(done.Wait(TimeSpan.FromSeconds(20)), "no final fringe result was published");
                Assert.Equal(pushes, final.FramesCaptured);
                Assert.Equal(pushes, final.FramesAnalysed + final.FramesDropped);
                Assert.True(final.FramesAnalysed > 0);
            }
        }

        [Fact]
        public void Service_ResetsTheAccumulationBetweenLegs() {
            var frames = TestVectorData.LoadFrames();
            using (var service = new FringeAnalysisService(new FakeOptionsProvider(), null)) {
                var finals = new System.Collections.Generic.List<FringeAnalysisResult>();
                var done = new SemaphoreSlim(0);
                service.Updated += (sender, result) => {
                    if (result.IsFinal) {
                        lock (finals) {
                            finals.Add(result);
                        }
                        done.Release();
                    }
                };

                for (var leg = 0; leg < 2; leg++) {
                    service.BeginRun(Context("Leg" + leg, leg == 1));
                    for (var i = 0; i < 8; i++) {
                        service.Push(frames[i], FrameSize, FrameSize, new ObservableRectangle(0, 0, FrameSize, FrameSize));
                        Thread.Sleep(2);
                    }
                    service.EndRun();
                    Assert.True(done.Wait(TimeSpan.FromSeconds(20)), "leg " + leg + " produced no final result");
                }

                lock (finals) {
                    Assert.Equal(2, finals.Count);
                    Assert.Equal("Leg0", finals[0].Label);
                    Assert.Equal("Leg1", finals[1].Label);
                    Assert.Equal(8, finals[1].FramesCaptured);
                    Assert.True(finals[1].FramesAnalysed <= 8);
                }
                Assert.Equal("Leg1", service.CachedReferenceLabel);
            }
        }

        [Fact]
        public void Service_IgnoresPushesWhenNoRunIsActive() {
            var frames = TestVectorData.LoadFrames();
            using (var service = new FringeAnalysisService(new FakeOptionsProvider(), null)) {
                service.Push(frames[0], FrameSize, FrameSize, new ObservableRectangle(0, 0, FrameSize, FrameSize));
                Assert.Null(service.Latest);
            }
        }

        [Fact]
        public void PaneMode_DefaultsToAverage() {
            using (var service = new FringeAnalysisService(new FakeOptionsProvider(), null)) {
                Assert.Equal(FringePaneMode.Average, service.PaneMode);
            }
        }
    }
}
