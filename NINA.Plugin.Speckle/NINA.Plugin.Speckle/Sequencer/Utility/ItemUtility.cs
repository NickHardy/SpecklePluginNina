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
using NINA.Plugin.Speckle.Model;
using NINA.Image.Interfaces;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.IO;
using NINA.Image.ImageAnalysis;
using NINA.Image.ImageData;
using NINA.Equipment.Equipment.MyTelescope;

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

        //public static BitmapSource GetDFTImage(BitmapSource image) {
        //    //var grayImage = ConvertTo16BppSource(image);
        //    var img = Mat.ImDecode(BitmapSourceToByte(image), ImreadModes.Grayscale); //Cv2.ImRead(ImagePath.Lenna, ImreadModes.Grayscale);

        //    // expand input image to optimal size
        //    var padded = new Mat();
        //    int m = Cv2.GetOptimalDFTSize(img.Rows);
        //    int n = Cv2.GetOptimalDFTSize(img.Cols); // on the border add zero values
        //    Cv2.CopyMakeBorder(img, padded, 0, m - img.Rows, 0, n - img.Cols, BorderTypes.Constant, Scalar.All(0));

        //    // Add to the expanded another plane with zeros
        //    var paddedF32 = new Mat();
        //    padded.ConvertTo(paddedF32, MatType.CV_32F);
        //    Mat[] planes = { paddedF32, Mat.Zeros(padded.Size(), MatType.CV_32F) };
        //    var complex = new Mat();
        //    Cv2.Merge(planes, complex);

        //    // this way the result may fit in the source matrix
        //    var dft = new Mat();
        //    Cv2.Dft(complex, dft);

        //    // compute the magnitude and switch to logarithmic scale
        //    // => log(1 + sqrt(Re(DFT(I))^2 + Im(DFT(I))^2))
        //    Cv2.Split(dft, out var dftPlanes);  // planes[0] = Re(DFT(I), planes[1] = Im(DFT(I))

        //    // planes[0] = magnitude
        //    var magnitude = new Mat();
        //    Cv2.Magnitude(dftPlanes[0], dftPlanes[1], magnitude);

        //    Mat magnitude1 = magnitude + Scalar.All(1);  // switch to logarithmic scale
        //    Cv2.Log(magnitude1, magnitude1);

        //    // crop the spectrum, if it has an odd number of rows or columns
        //    var spectrum = magnitude1[
        //        new Rect(0, 0, magnitude1.Cols & -2, magnitude1.Rows & -2)];

        //    // rearrange the quadrants of Fourier image  so that the origin is at the image center
        //    int cx = spectrum.Cols / 2;
        //    int cy = spectrum.Rows / 2;

        //    var q0 = new Mat(spectrum, new Rect(0, 0, cx, cy));   // Top-Left - Create a ROI per quadrant
        //    var q1 = new Mat(spectrum, new Rect(cx, 0, cx, cy));  // Top-Right
        //    var q2 = new Mat(spectrum, new Rect(0, cy, cx, cy));  // Bottom-Left
        //    var q3 = new Mat(spectrum, new Rect(cx, cy, cx, cy)); // Bottom-Right

        //    // swap quadrants (Top-Left with Bottom-Right)
        //    var tmp = new Mat();
        //    q0.CopyTo(tmp);
        //    q3.CopyTo(q0);
        //    tmp.CopyTo(q3);

        //    // swap quadrant (Top-Right with Bottom-Left)
        //    q1.CopyTo(tmp);
        //    q2.CopyTo(q1);
        //    tmp.CopyTo(q2);

        //    // Transform the matrix with float values into a
        //    Cv2.Normalize(spectrum, spectrum, 0, 255, NormTypes.MinMax);
        //    spectrum.ConvertTo(spectrum, MatType.CV_8U);

        //    //var point = Cv2.PhaseCorrelate(spectrum, previousSpectrum);
            
        //    // calculating the idft
        //    /*            var inverseTransform = new Mat();
        //                Cv2.Dft(dft, inverseTransform, DftFlags.Inverse | DftFlags.RealOutput);
        //                Cv2.Normalize(inverseTransform, inverseTransform, 0, 255, NormTypes.MinMax);
        //                inverseTransform.ConvertTo(inverseTransform, MatType.CV_8U);*/

        //    var returnImage = ImageUtility.ConvertBitmap(BitmapConverter.ToBitmap(spectrum), PixelFormats.Gray8);
        //    returnImage.Freeze();
        //    return returnImage;
        //}

    }
}