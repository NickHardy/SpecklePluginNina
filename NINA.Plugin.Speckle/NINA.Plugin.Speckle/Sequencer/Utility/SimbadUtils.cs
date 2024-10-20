#region "copyright"

/*
    Copyright © 2016 - 2021 Stefan Berg <isbeorn86+NINA@googlemail.com> and the N.I.N.A. contributors

    This file is part of N.I.N.A. - Nighttime Imaging 'N' Astronomy.

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Plugin.Speckle.Sequencer.Container;
using NINA.Astrometry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NINA.Sequencer.Container;
using NINA.Core.Utility;
using NINA.Core.Model;
using System.Threading;
using NINA.Plugin.Speckle.Model;
using System.Net;
using System.IO;
using Newtonsoft.Json;
using NINA.Core.Utility.Notification;
using System.Net.Http;
using System.Globalization;
using System.Reflection;

namespace NINA.Plugin.Speckle.Sequencer.Utility {

    public class SimbadUtils {

        public async Task<List<SimbadStarCluster>> FindSimbadStarClusters(IProgress<ApplicationStatus> externalProgress, CancellationToken token, Coordinates coords, double maxDistance = 5d) {
            List<SimbadStarCluster> starClusters = new List<SimbadStarCluster>();
            try {
                using (var localCTS = CancellationTokenSource.CreateLinkedTokenSource(token)) {
                    localCTS.CancelAfter(TimeSpan.FromSeconds(30));
                    externalProgress.Report(new ApplicationStatus() { Status = "Retrieving star clusters from simbad" });
                    var url = $"http://simbad.u-strasbg.fr/simbad/sim-tap/sync";

                    Dictionary<string, string> dictionary = new Dictionary<string, string>();
                    dictionary.Add("request", "doQuery");
                    dictionary.Add("lang", "adql");
                    dictionary.Add("format", "json");
                    dictionary.Add("maxrec", "100");
                    dictionary.Add("runid", "");
                    dictionary.Add("phase", "run");
                    dictionary.Add("query", "SELECT TOP 10 main_id, ra, dec, DISTANCE(POINT('ICRS', " + coords.RADegrees + ", " + coords.Dec + "), POINT('ICRS', ra, dec)) as dist FROM basic WHERE (otype_txt = 'Cl*' OR otype_txt = 'C?*') AND CONTAINS(POINT('ICRS', ra, dec), CIRCLE('ICRS', " + coords.RADegrees + ", " + coords.Dec + ", " + maxDistance + ")) = 1 AND galdim_majaxis IS NOT NULL AND ra IS NOT NULL AND dec IS NOT NULL ORDER BY galdim_majaxis DESC;");
                    VoTable voTable = await PostForm(url, dictionary, localCTS.Token);
                    if (voTable != null) {
                        foreach (List<object> obj in voTable.Data) {
                            starClusters.Add(new SimbadStarCluster(obj));
                        }
                    }
                }
            } catch (OperationCanceledException) {
            } catch (Exception ex) {
                Logger.Error(ex);
                Notification.ShowError(ex.Message);
            } finally {
                externalProgress.Report(new ApplicationStatus() { Status = "" });
            }
            return starClusters;
        }

        public async Task<List<ReferenceStar>> FindSimbadSaoStars(IProgress<ApplicationStatus> externalProgress, CancellationToken token, Coordinates coords, double maxDistance = 5d, double minMag = 0.0d, double maxMag = 10.0d) {
            List<ReferenceStar> stars = new List<ReferenceStar>();
            try {
                using (var localCTS = CancellationTokenSource.CreateLinkedTokenSource(token)) {
                    localCTS.CancelAfter(TimeSpan.FromSeconds(30));
                    externalProgress.Report(new ApplicationStatus() { Status = "Retrieving stars in the SAO catalogue from Simbad" });
                    var url = "http://simbad.u-strasbg.fr/simbad/sim-tap/sync";
                    double maxDistanceRadians = maxDistance * Math.PI / 180;
                    double minDistanceRadians = (1.0 / 60.0) * (Math.PI / 180); // 1 arcminute in radians
                    // TODO check for wds reference
                    string query = $"SELECT TOP 100 basic.main_id, basic.ra, basic.dec, basic.otype_txt, allfluxes.v, allfluxes.b - allfluxes.v AS color, " +
                                   $"2 * ASIN(SQRT(POWER(SIN(({coords.Dec} - basic.dec) * PI() / 360), 2) + COS({coords.Dec} * PI() / 180) * COS(basic.dec * PI() / 180) * POWER(SIN(({coords.RADegrees} - basic.ra) * PI() / 360), 2))) AS dist " +
                                   $"FROM basic " +
                                   $"JOIN ident ON basic.oid = ident.oidref " +
                                   $"JOIN ids ON basic.oid = ids.oidref " +
                                   $"JOIN allfluxes ON basic.oid = allfluxes.oidref " +
                                   $"WHERE ident.id LIKE 'SAO%' AND basic.otype_txt = '*' AND allfluxes.v BETWEEN {minMag} AND {maxMag} " +
                                   $"AND ids.ids NOT LIKE '%WDS%' AND ids.ids NOT LIKE '%IDS%' AND ids.ids NOT LIKE '%CCDM%' " +
                                   $"AND basic.ra IS NOT NULL AND basic.dec IS NOT NULL " +
                                   $"AND 2 * ASIN(SQRT(POWER(SIN(({coords.Dec} - basic.dec) * PI() / 360), 2) + COS({coords.Dec} * PI() / 180) * COS(basic.dec * PI() / 180) * POWER(SIN(({coords.RADegrees} - basic.ra) * PI() / 360), 2))) BETWEEN {minDistanceRadians} AND {maxDistanceRadians} " +
                                   $"ORDER BY dist;";

                    Dictionary<string, string> dictionary = new Dictionary<string, string>();
                    dictionary.Add("request", "doQuery");
                    dictionary.Add("lang", "adql");
                    dictionary.Add("format", "json");
                    dictionary.Add("maxrec", "100");
                    dictionary.Add("runid", "");
                    dictionary.Add("phase", "run");
                    dictionary.Add("query", query);

                    VoTable voTable = await PostForm(url, dictionary, localCTS.Token).ConfigureAwait(false);
                    if (voTable != null) {
                        foreach (List<object> obj in voTable.Data) {
                            ReferenceStar star = new ReferenceStar {
                                Name2 = obj[0].ToString(),
                                RA2000 = Convert.ToDouble(obj[1]),
                                Dec2000 = Convert.ToDouble(obj[2]),
                                Rp = Convert.ToDouble(obj[4]),
                                color = Convert.ToDouble(obj[5]),
                                distance = Convert.ToDouble(obj[6]) * (180 / Math.PI) // Convert radians to degrees
                            };
                            stars.Add(star);
                        }
                    }
                }
            } catch (OperationCanceledException) {
            } catch (Exception ex) {
                Logger.Error(ex);
                Notification.ShowError(ex.Message);
            } finally {
                externalProgress.Report(new ApplicationStatus() { Status = "" });
            }
            return stars;
        }

        public async Task<List<ReferenceStar>> FindSingleBrightStars(IProgress<ApplicationStatus> externalProgress, CancellationToken token, Coordinates coords, double maxDistance = 5d, double minMag = 0.0d, double maxMag = 10.0d) {
            List<ReferenceStar> stars = new List<ReferenceStar>();
            var assemblyFolder = new Uri(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)).LocalPath;
            var i = 1;
            try {
                using (StreamReader sr = new StreamReader(Path.Combine(assemblyFolder, "SingleBrightStarsUSNO2006.txt"))) {
                    externalProgress.Report(new ApplicationStatus() { Status = "Retrieving stars from the SingleBrightStars list" });
                    string line;
                    // Skip the header line
                    sr.ReadLine();

                    while ((line = sr.ReadLine()) != null) {
                        i++;
                        token.ThrowIfCancellationRequested();
                        ReferenceStar record = new ReferenceStar {
                            Name2 = $"{line.Substring(0, 7).Trim()} {line.Substring(32, 10).Trim()}",
                            RA2000 = AstroUtil.HMSToDegrees($"{line.Substring(47, 10).Trim()}"),
                            Dec2000 = AstroUtil.DMSToDegrees($"{line.Substring(58, 10).Trim()}"),
                            Rp = double.Parse($"{line.Substring(73, 5).Trim()}", CultureInfo.InvariantCulture)
                        };
                        var color = line.Length >= 84 ? line.Substring(79, line.Length - 79).Trim() + "0" : "0";
                        record.color = double.Parse(color, CultureInfo.InvariantCulture);
                        Separation sep = coords - record.Coordinates();
                        record.distance = sep?.Distance?.Degree ?? double.NaN;
                        if (record.distance == double.NaN || record.distance > maxDistance)
                            continue;
                        if (record.Rp < minMag || record.Rp > maxMag)
                            continue;
                        stars.Add(record);
                    }
                }
            }
            catch (OperationCanceledException) {
            }
            catch (Exception ex) {
                Logger.Error(ex);
                Notification.ShowError(ex.Message);
            }
            finally {
                externalProgress.Report(new ApplicationStatus() { Status = "" });
            }
            return stars;
        }

        public async Task<ReferenceStar> GetStarByPosition(IProgress<ApplicationStatus> externalProgress, CancellationToken token, double ra, double dec, double targetMag) {
            ReferenceStar starDetails = null;
            try {
                using (var localCTS = CancellationTokenSource.CreateLinkedTokenSource(token)) {
                    localCTS.CancelAfter(TimeSpan.FromSeconds(30));
                    externalProgress.Report(new ApplicationStatus() { Status = "Retrieving target star from Simbad" });
                    var url = "http://simbad.u-strasbg.fr/simbad/sim-tap/sync";
                    var query = $"SELECT basic.main_id, basic.ra, basic.dec, basic.otype_txt, allfluxes.b, allfluxes.v, allfluxes.b - allfluxes.v AS color ";
                        query += $"FROM basic JOIN allfluxes ON(basic.oid = allfluxes.oidref) ";
                        query += $"WHERE CONTAINS(POINT('ICRS', basic.ra, basic.dec), CIRCLE('ICRS', {ra}, {dec}, 0.083333)) = 1";

                    Dictionary<string, string> dictionary = new Dictionary<string, string>
                    {
                        ["request"] = "doQuery",
                        ["lang"] = "adql",
                        ["format"] = "json",
                        ["query"] = query
                    };

                    VoTable voTable = await PostForm(url, dictionary, localCTS.Token);
                    if (voTable != null && voTable.Data.Count > 0) {
                        var obj = voTable.Data.First();
                        starDetails = new ReferenceStar {
                            Name2 = obj[0].ToString(),
                            RA2000 = Convert.ToDouble(obj[1]),
                            Dec2000 = Convert.ToDouble(obj[2]),
                            Note1 = obj[3].ToString(),
                            Bp = Convert.ToDouble(obj[4]),
                            Rp = Convert.ToDouble(obj[5]),
                            color = Convert.ToDouble(obj[6])
                        };
                    }
                }
            } catch (OperationCanceledException) {
            } catch (Exception ex) {
                Logger.Error(ex);
                Notification.ShowError(ex.Message);
            } finally {
                externalProgress.Report(new ApplicationStatus() { Status = "Completed" });
            }
            return starDetails;
        }

        public async Task<List<SimbadBinaryStar>> FindSimbadBinaryStars(IProgress<ApplicationStatus> externalProgress, CancellationToken token, Coordinates coords, double maxDistance = 5d) {
            List<SimbadBinaryStar> stars = new List<SimbadBinaryStar>();
            try {
                using (var localCTS = CancellationTokenSource.CreateLinkedTokenSource(token)) {
                    localCTS.CancelAfter(TimeSpan.FromSeconds(30));
                    externalProgress.Report(new ApplicationStatus() { Status = "Retrieving stars in the SAO catalogue from simbad" });
                    var url = $"http://simbad.u-strasbg.fr/simbad/sim-tap/sync";

                    Dictionary<string, string> dictionary = new Dictionary<string, string>();
                    dictionary.Add("request", "doQuery");
                    dictionary.Add("lang", "adql");
                    dictionary.Add("format", "json");
                    dictionary.Add("maxrec", "100");
                    dictionary.Add("runid", "");
                    dictionary.Add("phase", "run");
                    dictionary.Add("query", "SELECT TOP 10 basic.main_id, basic.ra, basic.dec, allfluxes.v, DISTANCE(POINT('ICRS', " + coords.RADegrees + ", " + coords.Dec + "), POINT('ICRS', basic.ra, basic.dec)) as dist " +
                        "FROM basic " +
                        "JOIN ident on(basic.oid = ident.oidref) " +
                        "JOIN allfluxes using (oidref) " +
                        "WHERE ident.id like 'WDS%' and (basic.otype_txt = '**?' OR basic.otype_txt = '**') " +
                        "AND CONTAINS(POINT('ICRS', basic.ra, basic.dec), CIRCLE('ICRS', " + coords.RADegrees + ", " + coords.Dec + ", " + maxDistance + ")) = 1 " +
                        "AND basic.ra IS NOT NULL " +
                        "AND basic.dec IS NOT NULL " +
                        "ORDER BY dist;");
                    VoTable voTable = await PostForm(url, dictionary, localCTS.Token);
                    if (voTable != null) {
                        foreach (List<object> obj in voTable.Data) {
                            stars.Add(new SimbadBinaryStar(obj));
                        }
                    }
                }
            } catch (OperationCanceledException) {
            } catch (Exception ex) {
                Logger.Error(ex);
                Notification.ShowError(ex.Message);
            } finally {
                externalProgress.Report(new ApplicationStatus() { Status = "" });
            }
            return stars;
        }

        public async Task<List<SimbadGalaxy>> FindSimbadGalaxies(IProgress<ApplicationStatus> externalProgress, CancellationToken token, Coordinates coords, double maxDistance = 10d, double maxMag = 18.0d) {
            List<SimbadGalaxy> galaxies = new List<SimbadGalaxy>();
            try {
                using (var localCTS = CancellationTokenSource.CreateLinkedTokenSource(token)) {
                    localCTS.CancelAfter(TimeSpan.FromSeconds(30));
                    externalProgress.Report(new ApplicationStatus() { Status = "Retrieving galaxies from simbad" });
                    var url = $"http://simbad.u-strasbg.fr/simbad/sim-tap/sync";

                    Dictionary<string, string> dictionary = new Dictionary<string, string>();
                    dictionary.Add("request", "doQuery");
                    dictionary.Add("lang", "adql");
                    dictionary.Add("format", "json");
                    dictionary.Add("maxrec", "100");
                    dictionary.Add("runid", "");
                    dictionary.Add("phase", "run");
                    dictionary.Add("query", "SELECT DISTINCT TOP 10 basic.main_id, basic.ra, basic.dec, allfluxes.v, DISTANCE(POINT('ICRS', " + coords.RADegrees + ", " + coords.Dec + "), POINT('ICRS', basic.ra, basic.dec)) as dist, galdim_majaxis as sizemax , galdim_minaxis as sizemin " +
                        "FROM basic " +
                        "JOIN ident on(basic.oid = ident.oidref) " +
                        "JOIN allfluxes using (oidref) " +
                        "WHERE basic.otype = 'Galaxy..' and allfluxes.v <= " + maxMag + " " +
                        "AND CONTAINS(POINT('ICRS', basic.ra, basic.dec), CIRCLE('ICRS', " + coords.RADegrees + ", " + coords.Dec + ", " + maxDistance + ")) = 1 " +
                        "AND basic.ra IS NOT NULL " +
                        "AND basic.dec IS NOT NULL " +
                        "ORDER BY dist;");
                    VoTable voTable = await PostForm(url, dictionary, localCTS.Token);
                    if (voTable != null) {
                        foreach (List<object> obj in voTable.Data) {
                            galaxies.Add(new SimbadGalaxy(obj));
                        }
                    }
                }
            }
            catch (OperationCanceledException) {
            }
            catch (Exception ex) {
                Logger.Error(ex);
                Notification.ShowError(ex.Message);
            }
            finally {
                externalProgress.Report(new ApplicationStatus() { Status = "" });
            }
            return galaxies;
        }

        private async Task<VoTable> PostForm(string url, Dictionary<string, string> dictionary, CancellationToken token) {

            using var httpClient = new HttpClient();

            // Define your form data
            var formData = new MultipartFormDataContent();

            foreach (string key in dictionary.Keys) {
                formData.Add(new StringContent(dictionary[key]), key);
            }

            try {
                HttpResponseMessage response = await httpClient.PostAsync(url, formData, token);

                var serializer = new JsonSerializer();

                using var sr = new StreamReader(await response.Content?.ReadAsStreamAsync(), Encoding.UTF8);
                using var jsonTextReader = new JsonTextReader(sr);
                return serializer.Deserialize<VoTable>(jsonTextReader);
            }
            catch {
                Logger.Info("Couldn't process simbad comparison stars.");
                return null;
            }
        }
    }
}