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
        Map(column => column.AllRec).Name("allrec");
        Map(column => column.RecNo).Name("recno");
        Map(column => column.SourceId).Name("source_id");
        Map(column => column.Raj2000).Name("raj2000");
        Map(column => column.Dej2000).Name("dej2000");
        Map(column => column.Ra).Name("ra");
        Map(column => column.Dec).Name("dec");
        Map(column => column.Parallax).Name("parallax");
        Map(column => column.PmRa).Name("pmra");
        Map(column => column.PmDec).Name("pmdec");
        Map(column => column.PhotGMeanMag).Name("phot_g_mean_mag");
        Map(column => column.PhotBpMeanMag).Name("phot_bp_mean_mag");
        Map(column => column.PhotRpMeanMag).Name("phot_rp_mean_mag");
        Map(column => column.BMag).Name("bmag");
        Map(column => column.VMag).Name("vmag");
        Map(column => column.GMag).Name("g_mag");
        Map(column => column.RMag).Name("r_mag");
        Map(column => column.IMag).Name("i_mag");
        Map(column => column.BV).Name("b_v");
        Map(column => column.TeffGspphot).Name("teff_gspphot");
        Map(column => column.PhotVariableFlag).Name("phot_variable_flag");
        Map(column => column.NonSingleStar).Name("non_single_star");
        Map(column => column.Ruwe).Name("ruwe");
        Map(column => column.BpRp).Name("bp_rp");
        Map(column => column.AstrometricParamsSolved).Name("astrometric_params_solved");
        Map(column => column.IpdGofHarmonicAmplitude).Name("ipd_gof_harmonic_amplitude");
        Map(column => column.IpdFracMultiPeak).Name("ipd_frac_multi_peak");
    }
}

