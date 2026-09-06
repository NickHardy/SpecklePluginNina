using NINA.Plugin.Speckle.Model;
using NINA.Plugin.Speckle.Services;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace NINA.Plugin.Speckle.Test {

    public class TargetListServiceTests {

        [Fact]
        public async Task SaveSnapshot_OneList_KeepsTheSingleFileName() {
            using (var directory = new TempDirectory()) {
                var service = new TargetListService(null, null, null, null);

                await service.SaveSnapshotAsync(new[] { MakeTarget("Alice1", "alice.csv"), MakeTarget("Alice2", "alice.csv") }, directory.Path);

                var file = Assert.Single(Directory.GetFiles(directory.Path));
                Assert.Equal("TargetList-" + Stamp() + ".csv", Path.GetFileName(file));
                var lines = File.ReadAllLines(file);
                Assert.Equal(3, lines.Length);
                Assert.DoesNotContain("SourceList", lines[0]);
            }
        }

        [Fact]
        public async Task SaveSnapshot_SeveralLists_WritesOneFilePerList() {
            using (var directory = new TempDirectory()) {
                var service = new TargetListService(null, null, null, null);
                var targets = new[] {
                    MakeTarget("Alice1", Path.Combine("C:", "lists", "alice.csv")),
                    MakeTarget("Bob1", Path.Combine("D:", "other", "bob.csv")),
                    MakeTarget("Alice2", Path.Combine("C:", "lists", "alice.csv"))
                };

                await service.SaveSnapshotAsync(targets, directory.Path);

                var files = Directory.GetFiles(directory.Path).Select(Path.GetFileName).OrderBy(name => name).ToList();
                Assert.Equal(new[] { "TargetList-alice-" + Stamp() + ".csv", "TargetList-bob-" + Stamp() + ".csv" }, files);

                var alice = File.ReadAllLines(Path.Combine(directory.Path, files[0]));
                var bob = File.ReadAllLines(Path.Combine(directory.Path, files[1]));
                Assert.Equal(3, alice.Length);
                Assert.Equal(2, bob.Length);
                Assert.Equal(alice[0], bob[0]);
                Assert.Contains("Alice1", alice[1]);
                Assert.Contains("Alice2", alice[2]);
                Assert.Contains("Bob1", bob[1]);
            }
        }

        [Fact]
        public void Order_PutsTheEarliestTargetFirstAndBreaksTiesByPriority() {
            var service = new TargetListService(null, null, null, null);
            var midnight = new DateTime(2026, 8, 5, 0, 0, 0);
            var late = MakeTarget("Late", "a.csv");
            late.ImageTime = midnight.AddHours(2);
            var lowPriority = MakeTarget("LowPriority", "a.csv");
            lowPriority.ImageTime = midnight;
            lowPriority.Priority = 1;
            var highPriority = MakeTarget("HighPriority", "b.csv");
            highPriority.ImageTime = midnight;
            highPriority.Priority = 5;

            var ordered = service.Order(new[] { late, lowPriority, highPriority });

            Assert.Equal(new[] { "HighPriority", "LowPriority", "Late" }, ordered.Select(target => target.Name1));
        }

        private static string Stamp() {
            return DateTime.Now.AddHours(-12).ToString("yyyy-MM-dd");
        }

        private static SpeckleTarget MakeTarget(string name, string sourceList) {
            return new SpeckleTarget {
                Name1 = name,
                Name2 = name,
                Type = "M",
                RA2000 = 10,
                Dec2000 = 20,
                SourceList = sourceList
            };
        }

        private sealed class TempDirectory : IDisposable {

            public TempDirectory() {
                Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "speckle-test-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path);
            }

            public string Path { get; }

            public void Dispose() {
                try {
                    Directory.Delete(Path, true);
                } catch (IOException) {
                }
            }
        }
    }
}
