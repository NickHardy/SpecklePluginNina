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
    public long GaiaNum { get; set; }

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
    public double ExpTime { get; set; }

    [JsonProperty]
    public int NumExp { get; set; }

    [JsonProperty]
    public int NoExpCalc { get; set; }

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
}

public sealed class StarMap : ClassMap<Star> {
    public StarMap() {
        Map(m => m.TargetRecno).Name("targetrecno").Optional().Default(0);
        Map(m => m.Recno).Name("recno").Optional().Default(0);
        Map(m => m.Proj).Name("Proj").Optional().Default("");
        Map(m => m.Obs).Name("Obs").Optional().Default("");
        Map(m => m.Type).Name("Type").Optional().Default("");
        Map(m => m.Name1).Name("Name1").Optional().Default("");
        Map(m => m.Name2).Name("Name2").Optional().Default("");
        Map(m => m.Priority).Name("Priority").Optional().Default(1);
        Map(m => m.Template).Name("Template").Optional().Default("");
        Map(m => m.RA2000).Name("RA2000").Optional().Default(0);
        Map(m => m.Dec2000).Name("Dec2000").Optional().Default(0);
        Map(m => m.Bp).Name("Bp").Optional().Default(0);
        Map(m => m.Rp).Name("Rp").Optional().Default(0);
        Map(m => m.Gmag).Name("Gmag").Optional().Default(0);
        Map(m => m.GaiaNum).Name("GaiaNum").Optional().Default(0);
        Map(m => m.Sep).Name("Sep").Optional().Default(0);
        Map(m => m.PA).Name("PA").Optional().Default(0);
        Map(m => m.Parallax).Name("Parallax").Optional().Default(0);
        Map(m => m.Spectrum).Name("Spectrum").Optional().Default("");
        Map(m => m.Pmag).Name("Pmag").Optional().Default(0);
        Map(m => m.Smag).Name("Smag").Optional().Default(0);
        Map(m => m.Filter).Name("Filter").Optional().Default("");
        Map(m => m.ExpTime).Name("ExpTime").Optional().Default(0);
        Map(m => m.NumExp).Name("NumExp").Optional().Default(0);
        Map(m => m.NoExpCalc).Name("NoExpCalc").Optional().Default(0);
        Map(m => m.GetRef).Name("GetRef").Optional().Default(1);
        Map(m => m.GPrime).Name("GPrime").Optional().Default(0);
        Map(m => m.RPrime).Name("RPrime").Optional().Default(0);
        Map(m => m.IPrime).Name("IPrime").Optional().Default(0);
        Map(m => m.ZPrime).Name("ZPrime").Optional().Default(0);
        Map(m => m.RUWE).Name("RUWE").Optional().Default(0);
        Map(m => m.FDBL).Name("FDBL").Optional().Default(0);
        Map(m => m.Note1).Name("Note1").Optional().Default("");
        Map(m => m.Note2).Name("Note2").Optional().Default("");
    }
}
