using CsvHelper;
using CsvHelper.Configuration;
using NINA.Plugin.Speckle.Model;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Xunit;

namespace NINA.Plugin.Speckle.Test {

    public class TargetListCsvFormatTests {
        private const string ExpectedHeader =
            "Proj,Obs,Type,Name1,Name2,Priority,Template,RA2000,D2000,Bp,Rp,Gmag,GaiaNum,Sep,PA,Parallax,Spectrum,"
            + "Pmag,Smag,Filter,Exp,NExp,NoEC,GetRef,GPrime,RPrime,IPrime,ZPrime,RUWE,FDBL,Note1,Note2,Nights,Cycles,"
            + "Airmass,AirmassMax,MinAltitude,RefGaiaNum,Completed_cycles,Completed_ref_cycles,Completed_nights";

        private static string Write(IEnumerable<SpeckleTarget> targets) {
            using (var writer = new StringWriter())
            using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture)) {
                csv.Context.RegisterClassMap<SpeckleTargetMap>();
                csv.WriteRecords(targets);
                return writer.ToString();
            }
        }

        private static List<SpeckleTarget> Read(string text) {
            var config = new CsvConfiguration(CultureInfo.InvariantCulture) { MissingFieldFound = null, TrimOptions = TrimOptions.Trim };
            using (var reader = new StringReader(text))
            using (var csv = new CsvReader(reader, config)) {
                csv.Context.RegisterClassMap<SpeckleTargetMap>();
                return csv.GetRecords<SpeckleTarget>().ToList();
            }
        }

        private static SpeckleTarget Populated() {
            return new SpeckleTarget {
                Proj = "SG33", Obs = "NWDS", Type = "M", Name1 = "00126+4419", Name2 = "LSCM- 5",
                Priority = 3, Template = "Speckle Narrow", RA2000 = 3.149208, Dec2000 = 44.316306,
                Bp = 11.2, Rp = 9.8, Gmag = 10.4, GaiaNum = "393000000000000000", Sep = 0.6, PA = 118.4,
                Parallax = 12.5, Spectrum = "G2V", Pmag = 7.0, Smag = 11.9, Filter = "SloanR",
                Exp = 0.04, NExp = 1000, NoEC = 1, GetRef = 1, GPrime = 10.1, RPrime = 9.9,
                IPrime = 9.7, ZPrime = 9.6, RUWE = 1, FDBL = 0, Note1 = "note one", Note2 = "note two",
                Nights = 2, Cycles = 3, AirmassMin = 1.0, AirmassMax = 2.5, MinAltitude = 35,
                RefGaiaNum = "393000000000000001", Completed_cycles = 1, Completed_ref_cycles = 1,
                Completed_nights = 1
            };
        }

        [Fact]
        public void WrittenHeaderMatchesTheAgreedContract() {
            var header = Write(new[] { Populated() }).Split('\n')[0].TrimEnd('\r');

            Assert.Equal(ExpectedHeader, header);
        }

        [Fact]
        public void NoColumnIsWrittenTwice() {
            var header = Write(new[] { Populated() }).Split('\n')[0].TrimEnd('\r').Split(',');

            Assert.Equal(header.Length, header.Distinct().Count());
        }

        [Fact]
        public void EveryPopulatedFieldSurvivesARoundTrip() {
            var original = Populated();

            var restored = Assert.Single(Read(Write(new[] { original })));

            Assert.Equal(original.Proj, restored.Proj);
            Assert.Equal(original.Obs, restored.Obs);
            Assert.Equal(original.Type, restored.Type);
            Assert.Equal(original.Name1, restored.Name1);
            Assert.Equal(original.Name2, restored.Name2);
            Assert.Equal(original.Priority, restored.Priority);
            Assert.Equal(original.Template, restored.Template);
            Assert.Equal(original.RA2000, restored.RA2000);
            Assert.Equal(original.Dec2000, restored.Dec2000);
            Assert.Equal(original.Bp, restored.Bp);
            Assert.Equal(original.Rp, restored.Rp);
            Assert.Equal(original.Gmag, restored.Gmag);
            Assert.Equal(original.GaiaNum, restored.GaiaNum);
            Assert.Equal(original.Sep, restored.Sep);
            Assert.Equal(original.PA, restored.PA);
            Assert.Equal(original.Parallax, restored.Parallax);
            Assert.Equal(original.Spectrum, restored.Spectrum);
            Assert.Equal(original.Pmag, restored.Pmag);
            Assert.Equal(original.Smag, restored.Smag);
            Assert.Equal(original.Filter, restored.Filter);
            Assert.Equal(original.Exp, restored.Exp);
            Assert.Equal(original.NExp, restored.NExp);
            Assert.Equal(original.NoEC, restored.NoEC);
            Assert.Equal(original.GetRef, restored.GetRef);
            Assert.Equal(original.GPrime, restored.GPrime);
            Assert.Equal(original.RPrime, restored.RPrime);
            Assert.Equal(original.IPrime, restored.IPrime);
            Assert.Equal(original.ZPrime, restored.ZPrime);
            Assert.Equal(original.RUWE, restored.RUWE);
            Assert.Equal(original.FDBL, restored.FDBL);
            Assert.Equal(original.Note1, restored.Note1);
            Assert.Equal(original.Note2, restored.Note2);
            Assert.Equal(original.Nights, restored.Nights);
            Assert.Equal(original.Cycles, restored.Cycles);
            Assert.Equal(original.AirmassMin, restored.AirmassMin);
            Assert.Equal(original.AirmassMax, restored.AirmassMax);
            Assert.Equal(original.MinAltitude, restored.MinAltitude);
            Assert.Equal(original.RefGaiaNum, restored.RefGaiaNum);
            Assert.Equal(original.Completed_cycles, restored.Completed_cycles);
            Assert.Equal(original.Completed_ref_cycles, restored.Completed_ref_cycles);
            Assert.Equal(original.Completed_nights, restored.Completed_nights);
        }

        [Fact]
        public void ALegacyTelescopeHeaderStillParses() {
            var legacy = "RA2000*,D2000*,Name1*,Name2*,Proj~,Obs~,Type~,Priority~,Bp~,Rp~,Gmag~,GaiaNum~,RefGaiaNum~,"
                + "Sep~,Spec~,Pmag~,Smag~,Filter~,Exp~,NExp~,Temp,PA,Prix,NoEC,GetRef,Gprime,Rprime,Iprime,Zprime,"
                + "RUWE,FDBL,Note1,Note2\n"
                + "3.149208,44.316306,00126+4419,LSCM- 5,SG33,NWDS,M,,,,,,,0.6,,7,11.9,,,,,,,,,,,,,,,,\n";

            var target = Assert.Single(Read(legacy));

            Assert.Equal("00126+4419", target.Name1);
            Assert.Equal("LSCM- 5", target.Name2);
            Assert.Equal("SG33", target.Proj);
            Assert.Equal("M", target.Type);
            Assert.Equal(3.149208, target.RA2000);
            Assert.Equal(44.316306, target.Dec2000);
            Assert.Equal(0.6, target.Sep);
            Assert.Equal(7, target.Pmag);
            Assert.Equal(11.9, target.Smag);
            Assert.Equal(1, target.GetRef);
        }

        [Fact]
        public void ABlankGetRefStillDefaultsToOne() {
            var text = "Name1,GetRef\nOnlyName,\n";

            var target = Assert.Single(Read(text));

            Assert.Equal(1, target.GetRef);
        }
    }
}
