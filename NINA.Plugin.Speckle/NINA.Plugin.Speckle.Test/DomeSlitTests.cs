using NINA.Plugin.Speckle.Model;
using Xunit;

namespace NINA.Plugin.Speckle.Test {

    public class DomeSlitTests {
        private const double SlitAzimuth = 180d;
        private const double SlitWidth = 20d;

        [Fact]
        public void TargetOnTheSlitAzimuthIsInside() {
            Assert.True(TargetBase.IsInsideDomeSlit(45d, SlitAzimuth, SlitAzimuth, SlitWidth));
        }

        [Fact]
        public void TargetWellOffTheSlitAzimuthIsOutside() {
            Assert.False(TargetBase.IsInsideDomeSlit(45d, SlitAzimuth + 60d, SlitAzimuth, SlitWidth));
        }

        [Fact]
        public void ShutterReachesTenDegreesPastZenith() {
            Assert.True(TargetBase.IsInsideDomeSlit(81d, SlitAzimuth + 180d, SlitAzimuth, SlitWidth));
            Assert.False(TargetBase.IsInsideDomeSlit(79d, SlitAzimuth + 180d, SlitAzimuth, SlitWidth));
        }

        [Fact]
        public void AClosedSlitLetsNothingThrough() {
            Assert.False(TargetBase.IsInsideDomeSlit(45d, SlitAzimuth, SlitAzimuth, 0d));
        }
    }
}
