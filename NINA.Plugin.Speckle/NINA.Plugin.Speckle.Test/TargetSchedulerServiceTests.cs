using NINA.Plugin.Speckle.Model;
using NINA.Plugin.Speckle.Services;
using System;
using System.Collections.Generic;
using Xunit;

namespace NINA.Plugin.Speckle.Test {

    public class TargetSchedulerServiceTests {
        private static readonly DateTime Now = new DateTime(2026, 8, 6, 22, 0, 0);

        private static SpeckleTarget Target(string name, string type = "M", int cycles = 1, int completedCycles = 0,
                                            int nights = 1, int completedNights = 0, double rightAscension = 10,
                                            DateTime? imageTime = null, DateTime? imagedAt = null) {
            return new SpeckleTarget {
                Name1 = name,
                Name2 = name,
                Type = type,
                Cycles = cycles,
                Completed_cycles = completedCycles,
                Nights = nights,
                Completed_nights = completedNights,
                RA2000 = rightAscension,
                ImageTarget = true,
                ImageTime = imageTime ?? Now.AddMinutes(-1),
                ImagedAt = imagedAt
            };
        }

        private static SchedulingOptions Options(bool sortByRa = false, bool ignoreLimits = false) {
            return new SchedulingOptions { SortByRa = sortByRa, IgnoreLimits = ignoreLimits };
        }

        [Fact]
        public void ATargetWithNoCyclesLeftIsNotPicked() {
            var scheduler = new TargetSchedulerService();
            var exhausted = Target("Exhausted", cycles: 2, completedCycles: 2);

            var picked = scheduler.PickNextTarget(new List<SpeckleTarget> { exhausted }, Options(), null, Now);

            Assert.Null(picked);
        }

        [Fact]
        public void ATargetWithNoNightsLeftIsNotPicked() {
            var scheduler = new TargetSchedulerService();
            var exhausted = Target("Exhausted", nights: 1, completedNights: 1);

            var picked = scheduler.PickNextTarget(new List<SpeckleTarget> { exhausted }, Options(), null, Now);

            Assert.Null(picked);
        }

        [Theory]
        [InlineData("M")]
        [InlineData("C")]
        [InlineData("G")]
        public void OnlyTheImagedTypesAreEligible(string type) {
            var scheduler = new TargetSchedulerService();
            var target = Target("Eligible", type: type);

            var picked = scheduler.PickNextTarget(new List<SpeckleTarget> { target }, Options(), null, Now);

            Assert.Same(target, picked);
        }

        [Theory]
        [InlineData("R")]
        [InlineData("")]
        [InlineData("X")]
        public void EveryOtherTypeIsSkipped(string type) {
            var scheduler = new TargetSchedulerService();
            var target = Target("Skipped", type: type);

            var picked = scheduler.PickNextTarget(new List<SpeckleTarget> { target }, Options(), null, Now);

            Assert.Null(picked);
        }

        [Fact]
        public void ATargetImagedInsideTheLastQuarterHourIsSkipped() {
            var scheduler = new TargetSchedulerService();
            var justImaged = Target("JustImaged", imagedAt: Now.AddMinutes(-5));

            var picked = scheduler.PickNextTarget(new List<SpeckleTarget> { justImaged }, Options(), null, Now);

            Assert.Null(picked);
        }

        [Fact]
        public void ATargetImagedOverAQuarterHourAgoIsEligibleAgain() {
            var scheduler = new TargetSchedulerService();
            var earlier = Target("Earlier", imagedAt: Now.AddMinutes(-20));

            var picked = scheduler.PickNextTarget(new List<SpeckleTarget> { earlier }, Options(), null, Now);

            Assert.Same(earlier, picked);
        }

        [Fact]
        public void TheLeastCompletedTargetGoesFirst() {
            var scheduler = new TargetSchedulerService();
            var busy = Target("Busy", cycles: 5, completedCycles: 3);
            var fresh = Target("Fresh", cycles: 5, completedCycles: 0);

            var picked = scheduler.PickNextTarget(new List<SpeckleTarget> { busy, fresh }, Options(), null, Now);

            Assert.Same(fresh, picked);
        }

        [Fact]
        public void TargetsWithEqualProgressAreOrderedByTheirImageTime() {
            var scheduler = new TargetSchedulerService();
            var later = Target("Later", imageTime: Now.AddMinutes(-1));
            var earlier = Target("Earlier", imageTime: Now.AddMinutes(-4));

            var picked = scheduler.PickNextTarget(new List<SpeckleTarget> { later, earlier }, Options(), null, Now);

            Assert.Same(earlier, picked);
        }

        [Fact]
        public void SortByRaTakesTheNextTargetEastOfWhereTheMountIsPointing() {
            var scheduler = new TargetSchedulerService();
            var behind = Target("Behind", rightAscension: 5);
            var justAhead = Target("JustAhead", rightAscension: 12);
            var farAhead = Target("FarAhead", rightAscension: 200);

            var picked = scheduler.PickNextTarget(new List<SpeckleTarget> { farAhead, behind, justAhead },
                Options(sortByRa: true), 10, Now);

            Assert.Same(justAhead, picked);
        }

        [Fact]
        public void SortByRaFallsBackToTheOrdinaryOrderWhenNothingIsFurtherEast() {
            var scheduler = new TargetSchedulerService();
            var behind = Target("Behind", rightAscension: 5);

            var picked = scheduler.PickNextTarget(new List<SpeckleTarget> { behind }, Options(sortByRa: true), 10, Now);

            Assert.Same(behind, picked);
        }

        [Fact]
        public void APromotedTargetJumpsTheQueueExactlyOnce() {
            var scheduler = new TargetSchedulerService();
            var ordinary = Target("Ordinary", imageTime: Now.AddMinutes(-4));
            var promoted = Target("Promoted", imageTime: Now.AddMinutes(-1));
            var targets = new List<SpeckleTarget> { ordinary, promoted };
            scheduler.PromoteTarget(promoted);

            Assert.Same(promoted, scheduler.PickNextTarget(targets, Options(), null, Now));
            Assert.Same(ordinary, scheduler.PickNextTarget(targets, Options(), null, Now));
        }

        [Fact]
        public void AnImageTimeMoreThanFiveMinutesPastIsTreatedAsStaleAndSkipped() {
            var scheduler = new TargetSchedulerService();
            var stale = Target("Stale", imageTime: Now.AddMinutes(-30));

            var picked = scheduler.PickNextTarget(new List<SpeckleTarget> { stale }, Options(), null, Now);

            Assert.Null(picked);
        }

        [Fact]
        public void IgnoringLimitsRevivesAStaleImageTime() {
            var scheduler = new TargetSchedulerService();
            var stale = Target("Stale", imageTime: Now.AddMinutes(-30));

            var picked = scheduler.PickNextTarget(new List<SpeckleTarget> { stale }, Options(ignoreLimits: true), null, Now);

            Assert.Same(stale, picked);
        }

        [Fact]
        public void APromotedTargetWithNoCyclesLeftIsIgnored() {
            var scheduler = new TargetSchedulerService();
            var ordinary = Target("Ordinary");
            var exhausted = Target("Exhausted", cycles: 1, completedCycles: 1);
            scheduler.PromoteTarget(exhausted);

            var picked = scheduler.PickNextTarget(new List<SpeckleTarget> { ordinary, exhausted }, Options(), null, Now);

            Assert.Same(ordinary, picked);
        }

        [Fact]
        public void AnImageTimeInsideTheNextFiveMinutesIsPulledForwardToNow() {
            var scheduler = new TargetSchedulerService();
            var soon = Target("Soon", imageTime: Now.AddMinutes(3));

            var picked = scheduler.PickNextTarget(new List<SpeckleTarget> { soon }, Options(ignoreLimits: true), null, Now);

            Assert.Same(soon, picked);
            Assert.Equal(Now, picked.ImageTime);
        }

        [Fact]
        public void MarkingACycleCompleteAdvancesTheNightOnlyOnTheLastCycle() {
            var scheduler = new TargetSchedulerService();
            var target = Target("Counted", cycles: 2);

            scheduler.MarkCycleComplete(target, Now);
            Assert.Equal(1, target.Completed_cycles);
            Assert.Equal(0, target.Completed_nights);

            scheduler.MarkCycleComplete(target, Now);
            Assert.Equal(2, target.Completed_cycles);
            Assert.Equal(1, target.Completed_nights);
        }
    }
}
