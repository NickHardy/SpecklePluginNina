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

        public async Task<List<SimbadStar>> FindSimbadSaoStars(IProgress<ApplicationStatus> externalProgress, CancellationToken token, Coordinates coords, double maxDistance = 5d, double targetMag = 8.0d, double maxMag = 10.0d) {
            List<SimbadStar> stars = new List<SimbadStar>();
            try {
                using (var localCTS = CancellationTokenSource.CreateLinkedTokenSource(token)) {
                    localCTS.CancelAfter(TimeSpan.FromSeconds(30));
                    externalProgress.Report(new ApplicationStatus() { Status = "Retrieving stars in the SAO catalogue from Simbad" });
                    var url = "http://simbad.u-strasbg.fr/simbad/sim-tap/sync";

                    string query = $"SELECT TOP 200 basic.main_id, basic.ra, basic.dec, basic.otype_txt, allfluxes.v, allfluxes.b - allfluxes.v AS color, DISTANCE(POINT('ICRS', {coords.RADegrees}, {coords.Dec}), POINT('ICRS', basic.ra, basic.dec)) as dist " +
                                   "FROM basic " +
                                   "JOIN ident ON (basic.oid = ident.oidref) " +
                                   "JOIN allfluxes USING (oidref) " +
                                   "WHERE ident.id LIKE 'SAO%' AND basic.otype_txt = '*' AND allfluxes.v BETWEEN " + targetMag + " AND " + maxMag + " " +
                                   "AND CONTAINS(POINT('ICRS', " + coords.RADegrees + ", " + coords.Dec + "), CIRCLE('ICRS', " + coords.RADegrees + ", " + coords.Dec + ", " + maxDistance + ")) = 1 " +
                                   "AND basic.ra IS NOT NULL " +
                                   "AND basic.dec IS NOT NULL " +
                                   "ORDER BY dist;";

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
                            SimbadStar star = new SimbadStar {
                                main_id = obj[0].ToString(),
                                ra = Convert.ToDouble(obj[1]),
                                dec = Convert.ToDouble(obj[2]),
                                v_mag = Convert.ToDouble(obj[4]),
                                color = Convert.ToDouble(obj[5]),
                                distance = Convert.ToDouble(obj[6])
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

        public async Task<SimbadStar> GetStarByPosition(IProgress<ApplicationStatus> externalProgress, CancellationToken token, double ra, double dec, double targetMag) {
            SimbadStar starDetails = null;
            try {
                using (var localCTS = CancellationTokenSource.CreateLinkedTokenSource(token)) {
                    localCTS.CancelAfter(TimeSpan.FromSeconds(30));
                    externalProgress.Report(new ApplicationStatus() { Status = "Retrieving target star data from Simbad" });
                    var url = "http://simbad.u-strasbg.fr/simbad/sim-tap/sync";
                    var query = $"SELECT TOP 1 basic.main_id, basic.ra, basic.dec, basic.otype_txt, allfluxes.b, allfluxes.v, allfluxes.b - allfluxes.v AS color, DISTANCE(POINT('ICRS', basic.ra, basic.dec), POINT('ICRS', {ra}, {dec})) AS \"distance\" " +
                                $"FROM basic " +
                                $"JOIN allfluxes ON (basic.oid = allfluxes.oidref) " +
                                $"WHERE allfluxes.v BETWEEN {targetMag - 0.5} AND {targetMag + 0.5} " +
                                $"AND DISTANCE(POINT('ICRS', basic.ra, basic.dec), POINT('ICRS', {ra}, {dec})) <= 0.083333 " +  // 5 arcmin in °
                                $"ORDER BY \"distance\";";

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
                        starDetails = new SimbadStar {
                            main_id = obj[0].ToString(),
                            ra = Convert.ToDouble(obj[1]),
                            dec = Convert.ToDouble(obj[2]),
                            otype_txt = obj[3].ToString(),
                            b_mag = Convert.ToDouble(obj[4]),
                            v_mag = Convert.ToDouble(obj[5]),
                            color = Convert.ToDouble(obj[6]),
                            distance = Convert.ToDouble(obj[7])
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

        //private VoTable PostFormOld(string url, Dictionary<string, string> dictionary) {
        //    var boundary = "---------------------------" + DateTime.Now.Ticks.ToString("x");
        //    string FormDataTemplate = "--{0}\r\nContent-Disposition: form-data; name=\"{1}\"\r\n\r\n{2}\r\n";

        //    HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
        //    request.Timeout = 30 * 60 * 1000;
        //    request.Method = "POST";
        //    request.KeepAlive = true;
        //    request.ContentType = "multipart/form-data; boundary=" + boundary;
        //    Stream requestStream = request.GetRequestStream();
        //    foreach (string key in dictionary.Keys) {
        //        string item = String.Format(FormDataTemplate, boundary, key, dictionary[key]);
        //        byte[] itemBytes = System.Text.Encoding.UTF8.GetBytes(item);
        //        requestStream.Write(itemBytes, 0, itemBytes.Length);
        //    }
        //    byte[] endBytes = System.Text.Encoding.UTF8.GetBytes("--" + boundary + "--");
        //    requestStream.Write(endBytes, 0, endBytes.Length);
        //    requestStream.Close();
        //    WebResponse response = (WebResponse)request.GetResponse();

        //    // For debugging
        //    /*            ITraceWriter traceWriter = new MemoryTraceWriter();

        //                var settings = new JsonSerializerSettings {
        //                    NullValueHandling = NullValueHandling.Ignore,
        //                    MissingMemberHandling = MissingMemberHandling.Ignore,
        //                    Formatting = Newtonsoft.Json.Formatting.None,
        //                    DateFormatHandling = DateFormatHandling.IsoDateFormat,
        //                    FloatParseHandling = FloatParseHandling.Decimal,
        //                    TraceWriter = traceWriter
        //                };

        //                using (Stream stream = response.GetResponseStream()) {
        //                    StreamReader reader = new StreamReader(stream, Encoding.UTF8);
        //                    String responseString = reader.ReadToEnd();
        //                    return JsonConvert.DeserializeObject<SimbadCompStarChart>(responseString, settings);
        //                }*/

        //    try {
        //        var serializer = new JsonSerializer();

        //        using (var sr = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
        //        using (var jsonTextReader = new JsonTextReader(sr)) {
        //            return serializer.Deserialize<VoTable>(jsonTextReader);
        //        }
        //    } catch {
        //        Logger.Info("Failed to get information from url:" + url);
        //        return null;
        //    }
        //}

    }
}