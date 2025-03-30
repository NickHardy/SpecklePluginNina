using CsvHelper.Configuration;
using Newtonsoft.Json;
using NINA.Astrometry;

namespace NINA.Plugin.Speckle.Model;

public class GaiaReferenceStar {
    [JsonProperty]
    public int AllRec { get; set; }

    [JsonProperty]
    public int RecNo { get; set; }

    [JsonProperty]
    public long SourceId { get; set; }

    [JsonProperty]
    public double Raj2000 { get; set; }

    [JsonProperty]
    public double Dej2000 { get; set; }

    [JsonProperty]
    public double Ra { get; set; }

    [JsonProperty]
    public double Dec { get; set; }

    [JsonProperty]
    public double Parallax { get; set; }

    [JsonProperty]
    public double PmRa { get; set; }

    [JsonProperty]
    public double PmDec { get; set; }

    [JsonProperty]
    public double PhotGMeanMag { get; set; }

    [JsonProperty]
    public double PhotBpMeanMag { get; set; }

    [JsonProperty]
    public double PhotRpMeanMag { get; set; }

    [JsonProperty]
    public double BMag { get; set; }

    [JsonProperty]
    public double VMag { get; set; }

    [JsonProperty]
    public double GMag { get; set; }

    [JsonProperty]
    public double RMag { get; set; }

    [JsonProperty]
    public double IMag { get; set; }

    [JsonProperty]
    public double BV { get; set; }

    [JsonProperty]
    public double TeffGspphot { get; set; }

    [JsonProperty]
    public string PhotVariableFlag { get; set; }

    [JsonProperty]
    public int NonSingleStar { get; set; }

    [JsonProperty]
    public double Ruwe { get; set; }

    [JsonProperty]
    public double BpRp { get; set; }

    [JsonProperty]
    public int AstrometricParamsSolved { get; set; }

    [JsonProperty]
    public double IpdGofHarmonicAmplitude { get; set; }

    [JsonProperty]
    public double IpdFracMultiPeak { get; set; }

    public Coordinates Coordinates() {
        return new Coordinates(Angle.ByDegree(Raj2000), Angle.ByDegree(Dej2000), Epoch.J2000);
    }
}

public sealed class GaiaReferenceStarMap : ClassMap<GaiaReferenceStar> {
    public GaiaReferenceStarMap() {
        Map(m => m.AllRec).Name("allrec");
        Map(m => m.RecNo).Name("recno");
        Map(m => m.SourceId).Name("source_id");
        Map(m => m.Raj2000).Name("raj2000");
        Map(m => m.Dej2000).Name("dej2000");
        Map(m => m.Ra).Name("ra");
        Map(m => m.Dec).Name("dec");
        Map(m => m.Parallax).Name("parallax");
        Map(m => m.PmRa).Name("pmra");
        Map(m => m.PmDec).Name("pmdec");
        Map(m => m.PhotGMeanMag).Name("phot_g_mean_mag");
        Map(m => m.PhotBpMeanMag).Name("phot_bp_mean_mag");
        Map(m => m.PhotRpMeanMag).Name("phot_rp_mean_mag");
        Map(m => m.BMag).Name("bmag");
        Map(m => m.VMag).Name("vmag");
        Map(m => m.GMag).Name("g_mag");
        Map(m => m.RMag).Name("r_mag");
        Map(m => m.IMag).Name("i_mag");
        Map(m => m.BV).Name("b_v");
        Map(m => m.TeffGspphot).Name("teff_gspphot");
        Map(m => m.PhotVariableFlag).Name("phot_variable_flag");
        Map(m => m.NonSingleStar).Name("non_single_star");
        Map(m => m.Ruwe).Name("ruwe");
        Map(m => m.BpRp).Name("bp_rp");
        Map(m => m.AstrometricParamsSolved).Name("astrometric_params_solved");
        Map(m => m.IpdGofHarmonicAmplitude).Name("ipd_gof_harmonic_amplitude");
        Map(m => m.IpdFracMultiPeak).Name("ipd_frac_multi_peak");
    }
}

