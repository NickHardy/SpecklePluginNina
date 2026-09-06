#region "copyright"

/*
    Copyright © 2016 - 2021 Stefan Berg <isbeorn86+NINA@googlemail.com> and the N.I.N.A. contributors

    This file is part of N.I.N.A. - Nighttime Imaging 'N' Astronomy.

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Astrometry;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Equipment.Equipment.MyTelescope;
using NINA.Image.ImageData;
using NINA.Plugin.Speckle.Model;
using NINA.Plugin.Speckle.Sequencer.Container;
using NINA.Sequencer.Container;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;

namespace NINA.Plugin.Speckle.Sequencer.Utility {

    public class ItemUtility {

        public static SpeckleTargetContainer RetrieveSpeckleContainer(ISequenceContainer parent) {
            if (parent != null) {
                var container = parent as SpeckleTargetContainer;
                if (container != null) {
                    return container;
                }
                else {
                    return RetrieveSpeckleContainer(parent.Parent);
                }
            }
            else {
                return null;
            }
        }

        public static AsyncObservableCollection<SpeckleTargetContainer> RetrieveSpeckleTemplates(ISequenceContainer parent) {
            if (parent != null) {
                var container = parent as SpeckleTargetListContainer;
                if (container != null) {
                    return container.SpeckleTemplates;
                }
                else {
                    return RetrieveSpeckleTemplates(parent.Parent);
                }
            }
            else {
                return null;
            }
        }

        public static SpeckleTargetListContainer RetrieveSpeckleListContainer(ISequenceContainer parent) {
            if (parent != null) {
                var container = parent as SpeckleTargetListContainer;
                if (container != null) {
                    return container;
                }
                else {
                    return RetrieveSpeckleListContainer(parent.Parent);
                }
            }
            else {
                return null;
            }
        }

        public static ObservableRectangle RetrieveSpeckleTargetRoi(ISequenceContainer parent) {
            if (parent != null) {
                var container = parent as SpeckleTargetContainer;
                if (container != null) {
                    return container.SubSampleRectangle;
                }
                else {
                    return RetrieveSpeckleTargetRoi(parent.Parent);
                }
            }
            else {
                return null;
            }
        }

        public static string RetrieveSpeckleTitle(ISequenceContainer parent) {
            if (parent != null) {
                var container = parent as SpeckleTargetContainer;
                if (container != null) {
                    return container.Title;
                }
                else {
                    return RetrieveSpeckleTitle(parent.Parent);
                }
            }
            else {
                return null;
            }
        }

        public static InputTarget RetrieveInputTarget(ISequenceContainer parent) {
            if (parent != null) {
                var container = parent as SpeckleTargetContainer;
                if (container != null) {
                    return container.Target;
                }
                else {
                    return RetrieveInputTarget(parent.Parent);
                }
            }
            else {
                return null;
            }
        }

        public static SpeckleTarget RetrieveSpeckleTarget(ISequenceContainer parent) {
            if (parent != null) {
                var container = parent as SpeckleTargetContainer;
                if (container != null && container.SpeckleTarget != null) {
                    return container.SpeckleTarget;
                }
                var listContainer = parent as SpeckleTargetListContainer;
                if (listContainer != null) {
                    return listContainer.SpeckleTarget;
                }
                else {
                    return RetrieveSpeckleTarget(parent.Parent);
                }
            }
            else {
                return null;
            }
        }

        public static void FromTelescopeInfo(ImageMetaData data, TelescopeInfo info) {
            if (info.Connected) {
                if (string.IsNullOrWhiteSpace(data.Telescope.Name)) {
                    data.Telescope.Name = info.Name;
                }
                data.Observer.Elevation = info.SiteElevation;
                data.Telescope.Coordinates = info.Coordinates;
                data.Telescope.Altitude = info.Altitude;
                data.Telescope.Azimuth = info.Azimuth;
                data.Telescope.Airmass = Astrometry.AstroUtil.Airmass(info.Altitude);
                data.Telescope.SideOfPier = info.SideOfPier;
            }
        }

        public static byte[] BitmapSourceToByte(BitmapSource source) {
            var encoder = new PngBitmapEncoder();
            var frame = BitmapFrame.Create(source);
            encoder.Frames.Add(frame);
            var stream = new MemoryStream();

            encoder.Save(stream);
            return stream.ToArray();
        }

        public static BitmapSource ConvertTo16BppSource(BitmapSource source) {
            FormatConvertedBitmap s = new FormatConvertedBitmap();
            s.BeginInit();
            s.Source = source;
            s.DestinationFormat = System.Windows.Media.PixelFormats.Gray16;
            s.EndInit();
            s.Freeze();
            return s;
        }

        public static string GetHeaderValue(IEnumerable<IGenericMetaDataHeader> genericHeaders, params string[] keys) {
            return genericHeaders?.OfType<StringMetaDataHeader>().FirstOrDefault(header => keys.Contains(header.Key))?.Value ?? string.Empty;
        }

        public static void AddImagePatterns(List<ImagePattern> customPatterns, Speckle speckle, List<IGenericMetaDataHeader> genericHeaders) {
            if (genericHeaders == null)
                return;

            customPatterns.Add(new ImagePattern(speckle.name1Pattern.Key, speckle.name1Pattern.Description, speckle.name1Pattern.Category) {
                Value = GetHeaderValue(genericHeaders, "Name1*", "Name1")
            });
            customPatterns.Add(new ImagePattern(speckle.name2Pattern.Key, speckle.name2Pattern.Description, speckle.name2Pattern.Category) {
                Value = GetHeaderValue(genericHeaders, "Name2*", "Name2")
            });
            customPatterns.Add(new ImagePattern(speckle.projectPattern.Key, speckle.projectPattern.Description, speckle.projectPattern.Category) {
                Value = GetHeaderValue(genericHeaders, "Proj~", "Proj")
            });
            customPatterns.Add(new ImagePattern(speckle.observerPattern.Key, speckle.observerPattern.Description, speckle.observerPattern.Category) {
                Value = GetHeaderValue(genericHeaders, "Obs~", "Obs")
            });
            customPatterns.Add(new ImagePattern(speckle.gaiaNumberPattern.Key, speckle.gaiaNumberPattern.Description, speckle.gaiaNumberPattern.Category) {
                Value = GetHeaderValue(genericHeaders, "GaiaNum~", "GaiaNum")
            });
        }

    }
}