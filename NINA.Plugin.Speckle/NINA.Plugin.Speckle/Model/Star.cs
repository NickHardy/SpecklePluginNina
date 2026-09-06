using CsvHelper.Configuration;
using Newtonsoft.Json;

namespace NINA.Plugin.Speckle.Model;

[JsonObject(MemberSerialization.OptIn)]
public class Star : TargetBase {

    public Star() { }

    [JsonProperty]
    public long TargetRecno { get; set; }

    [JsonProperty]
    public long Recno { get; set; }

    [JsonProperty]
    public string Proj { get; set; }

    [JsonProperty]
    public string Obs { get; set; }

    [JsonProperty]
    public string Type { get; set; }

    [JsonProperty]
    public string Name1 { get; set; }

    [JsonProperty]
    public string Name2 { get; set; }

    [JsonProperty]
    public int Priority { get; set; }

    [JsonProperty]
    public string Template { get; set; }

    [JsonProperty]
    public double RA2000 { get; set; }

    [JsonProperty]
    public double Dec2000 { get; set; }

    [JsonProperty]
    public double Bp { get; set; }

    [JsonProperty]
    public double Rp { get; set; }

    [JsonProperty]
    public double Gmag { get; set; }

    [JsonProperty]
    public string GaiaNum { get; set; }

    [JsonProperty]
    public double Sep { get; set; }

    [JsonProperty]
    public double PA { get; set; }

    [JsonProperty]
    public double Parallax { get; set; }

    [JsonProperty]
    public string Spectrum { get; set; }

    [JsonProperty]
    public double Pmag { get; set; }

    [JsonProperty]
    public double Smag { get; set; }

    [JsonProperty]
    public string Filter { get; set; }

    [JsonProperty]
    public double Exp { get; set; }

    [JsonProperty]
    public int NExp { get; set; }

    [JsonProperty]
    public int NoEC { get; set; }

    [JsonProperty]
    public int GetRef { get; set; }

    [JsonProperty]
    public double GPrime { get; set; }

    [JsonProperty]
    public double RPrime { get; set; }

    [JsonProperty]
    public double IPrime { get; set; }

    [JsonProperty]
    public double ZPrime { get; set; }

    [JsonProperty]
    public double RUWE { get; set; }

    [JsonProperty]
    public double FDBL { get; set; }

    [JsonProperty]
    public string Note1 { get; set; }

    [JsonProperty]
    public string Note2 { get; set; }

    public NINA.Astrometry.Coordinates Coordinates() {
        return new NINA.Astrometry.Coordinates(NINA.Astrometry.Angle.ByDegree(RA2000),
                                               NINA.Astrometry.Angle.ByDegree(Dec2000),
                                               NINA.Astrometry.Epoch.J2000);
    }
}
