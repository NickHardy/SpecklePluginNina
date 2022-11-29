#region "copyright"

/*
    Copyright © 2016 - 2021 Stefan Berg <isbeorn86+NINA@googlemail.com> and the N.I.N.A. contributors

    This file is part of N.I.N.A. - Nighttime Imaging 'N' Astronomy.

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using Accord.Imaging;
using Accord.Imaging.Filters;
using Accord.Math.Geometry;
using NINA.Core.Enum;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Image.ImageData;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NINA.Image.Interfaces;
using NINA.Image.ImageAnalysis;

namespace NINA.Plugin.Speckle.Sequencer.Utility {

    public class StarDetection : IStarDetection {
        private static int _maxWidth = 1552;

        public string Name => "NINA";

        public string ContentId => this.GetType().FullName;

        private class State {
            public IImageArray _iarr;
            public ImageProperties imageProperties;
            public BitmapSource _originalBitmapSource;
            public double _resizefactor;
            public double _inverseResizefactor;
            public int _minStarSize;
            public int _maxStarSize;
        }

        private static State GetInitialState(IRenderedImage renderedImage, System.Windows.Media.PixelFormat pf, StarDetectionParams p) {
            var state = new State();
            var imageData = renderedImage.RawImageData;
            state.imageProperties = imageData.Properties;

            state._iarr = imageData.Data;
            //If image was debayered, use debayered array for star HFR and local maximum identification
            if (state.imageProperties.IsBayered && (renderedImage is IDebayeredImage)) {
                var debayeredImage = (IDebayeredImage)renderedImage;
                var debayeredData = debayeredImage.DebayeredData;
                if (debayeredData != null && debayeredData.Lum != null && debayeredData.Lum.Length > 0) {
                    state._iarr = new ImageArray(debayeredData.Lum);
                }
            }

            state._originalBitmapSource = renderedImage.Image;

            state._resizefactor = 1.0;
            if (state.imageProperties.Width > _maxWidth) {
                if (p.Sensitivity == StarSensitivityEnum.Highest) {
                    state._resizefactor = Math.Max(0.625, (double)_maxWidth / state.imageProperties.Width);
                }
                else {
                    state._resizefactor = (double)_maxWidth / state.imageProperties.Width;
                }
            }
            state._inverseResizefactor = 1.0 / state._resizefactor;

            state._minStarSize = (int)Math.Floor(5 * state._resizefactor);
            //Prevent Hotpixels to be detected
            if (state._minStarSize < 2) {
                state._minStarSize = 2;
            }

            state._maxStarSize = (int)Math.Ceiling(350 * state._resizefactor);
            if (pf == PixelFormats.Rgb48) {
                using (var source = ImageUtility.BitmapFromSource(state._originalBitmapSource, System.Drawing.Imaging.PixelFormat.Format48bppRgb)) {
                    using (var img = new Grayscale(0.2125, 0.7154, 0.0721).Apply(source)) {
                        state._originalBitmapSource = ImageUtility.ConvertBitmap(img, System.Windows.Media.PixelFormats.Gray16);
                        state._originalBitmapSource.Freeze();
                    }
                }
            }

            return state;
        }

        private BlobCounter _blobCounter;
        
        public class Star {
            public double radius;
            public double HFR;
            public Accord.Point Position;
            public double distanceCenter;
            public double meanBrightness;
            private List<PixelData> pixelData;
            public double Average { get; private set; } = 0;
            public double SurroundingMean { get; set; } = 0;
            public double maxPixelValue { get; set; } = 0;

            public Rectangle Rectangle;

            public Star() {
                pixelData = new List<PixelData>();
            }

            public void AddPixelData(PixelData value) {
                this.pixelData.Add(value);
            }

            public void CalculateHfr() {
                double hfr = 0.0d;
                if (this.pixelData.Count > 0) {
                    double outerRadius = this.radius * 1.2;
                    double sum = 0, sumDist = 0, allSum = 0;

                    double centerX = this.Position.X;
                    double centerY = this.Position.Y;

                    foreach (PixelData data in this.pixelData) {
                        double value = Math.Round(data.value - SurroundingMean);
                        if (value < 0) {
                            value = 0;
                        }
                        data.value = (ushort)Math.Round(value);

                        allSum += data.value;
                        if (InsideCircle(data.PosX, data.PosY, this.Position.X, this.Position.Y, outerRadius)) {
                            sum += data.value;
                            sumDist += data.value * Math.Sqrt(Math.Pow((double)data.PosX - (double)centerX, 2.0d) + Math.Pow((double)data.PosY - (double)centerY, 2.0d));
                        }
                    }

                    if (sum > 0) {
                        hfr = sumDist / sum;
                    } else {
                        hfr = Math.Sqrt(2) * outerRadius;
                    }
                    this.Average = allSum / this.pixelData.Count;
                }
                this.HFR = hfr;
                this.pixelData.Clear();
            }

            internal bool InsideCircle(double x, double y, double centerX, double centerY, double radius) {
                return (Math.Pow(x - centerX, 2) + Math.Pow(y - centerY, 2) <= Math.Pow(radius, 2));
            }

            public DetectedStar ToDetectedStar() {
                return new DetectedStar() {
                    HFR = HFR,
                    Position = Position,
                    AverageBrightness = Average,
                    MaxBrightness = maxPixelValue,
                    Background = SurroundingMean,
                    BoundingBox = Rectangle
                };
            }
        }

        public class PixelData {
            public int PosX;
            public int PosY;
            public ushort value;

            public override string ToString() {
                return value.ToString();
            }
        }

        public async Task<StarDetectionResult> Detect(IRenderedImage image, PixelFormat pf, StarDetectionParams p, IProgress<ApplicationStatus> progress, CancellationToken token) {
            var result = new StarDetectionResult();
            Bitmap _bitmapToAnalyze = null;
            try {
                using (MyStopWatch.Measure()) {
                    progress?.Report(new ApplicationStatus() { Status = "Preparing image for star detection" });

                    var state = GetInitialState(image, pf, p);
                    _bitmapToAnalyze = ImageUtility.Convert16BppTo8Bpp(state._originalBitmapSource);

                    token.ThrowIfCancellationRequested();

                    /* Resize to speed up manipulation */
                    _bitmapToAnalyze = DetectionUtility.ResizeForDetection(_bitmapToAnalyze, _maxWidth, state._resizefactor);

                    /* prepare image for structure detection */
                    PrepareForStructureDetection(_bitmapToAnalyze, p, state, token);

                    progress?.Report(new ApplicationStatus() { Status = "Detecting structures" });

                    /* get structure info */
                    _blobCounter = DetectStructures(_bitmapToAnalyze, token);

                    progress?.Report(new ApplicationStatus() { Status = "Analyzing stars" });

                    result.StarList = IdentifyStars(p, state, _bitmapToAnalyze, result, token, out var detectedStars);

                    token.ThrowIfCancellationRequested();

                    if (result.StarList.Count > 0) {
                        var mean = (from star in result.StarList select star.HFR).Average();
                        var stdDev = double.NaN;
                        if (result.StarList.Count > 1) {
                            stdDev = Math.Sqrt((from star in result.StarList select (star.HFR - mean) * (star.HFR - mean)).Sum() / (result.StarList.Count() - 1));
                        }

                        Logger.Info($"Average HFR: {mean}, HFR σ: {stdDev}, Detected Stars {detectedStars}");

                        result.AverageHFR = mean;
                        result.HFRStdDev = stdDev;
                        result.DetectedStars = detectedStars;
                    }

                    _blobCounter = null;
                }
            }
            catch (OperationCanceledException) {
            }
            finally {
                progress?.Report(new ApplicationStatus() { Status = string.Empty });
                _bitmapToAnalyze?.Dispose();
            }
            return result;
        }

        public async Task<DetectedStar> GetBiggestStar(IRenderedImage image, PixelFormat pf, StarDetectionParams p, IProgress<ApplicationStatus> progress, CancellationToken token) {
            var result = new StarDetectionResult();
            Bitmap _bitmapToAnalyze = null;
            try {
                using (MyStopWatch.Measure()) {
                    progress?.Report(new ApplicationStatus() { Status = "Preparing image for star detection" });

                    var state = GetInitialState(image, pf, p);
                    _bitmapToAnalyze = ImageUtility.Convert16BppTo8Bpp(state._originalBitmapSource);

                    token.ThrowIfCancellationRequested();

                    /* Resize to speed up manipulation */
                    _bitmapToAnalyze = DetectionUtility.ResizeForDetection(_bitmapToAnalyze, _maxWidth, state._resizefactor);

                    /* prepare image for structure detection */
                    PrepareForStructureDetection(_bitmapToAnalyze, p, state, token);

                    progress?.Report(new ApplicationStatus() { Status = "Detecting structures" });

                    /* get structure info */
                    _blobCounter = DetectStructures(_bitmapToAnalyze, token);

                    progress?.Report(new ApplicationStatus() { Status = "Analyzing stars" });

                    result.StarList = IdentifyLargestStars(p, state, _bitmapToAnalyze, result, token, out var detectedStars);

                    token.ThrowIfCancellationRequested();

                    _blobCounter = null;
                }
            }
            catch (OperationCanceledException) {
            }
            finally {
                progress?.Report(new ApplicationStatus() { Status = string.Empty });
                _bitmapToAnalyze?.Dispose();
            }
            return result.StarList.FirstOrDefault();
        }

        private List<DetectedStar> IdentifyStars(StarDetectionParams p, State state, Bitmap _bitmapToAnalyze, StarDetectionResult result, CancellationToken token, out int detectedStars) {
            detectedStars = 0;
            Blob[] blobs = _blobCounter.GetObjectsInformation();
            SimpleShapeChecker checker = new SimpleShapeChecker();
            List<Star> starlist = new List<Star>();
            double sumRadius = 0;
            double sumSquares = 0;
            foreach (Blob blob in blobs) {
                token.ThrowIfCancellationRequested();

                if (blob.Rectangle.Width > state._maxStarSize
                    || blob.Rectangle.Height > state._maxStarSize
                    || blob.Rectangle.Width < state._minStarSize
                    || blob.Rectangle.Height < state._minStarSize) {
                    continue;
                }

                var points = _blobCounter.GetBlobsEdgePoints(blob);
                Accord.Point centerpoint;
                float radius;
                var rect = new Rectangle((int)Math.Floor(blob.Rectangle.X * state._inverseResizefactor), (int)Math.Floor(blob.Rectangle.Y * state._inverseResizefactor), (int)Math.Ceiling(blob.Rectangle.Width * state._inverseResizefactor), (int)Math.Ceiling(blob.Rectangle.Height * state._inverseResizefactor));

                //Build a rectangle that encompasses the blob
                int largeRectXPos = Math.Max(rect.X - rect.Width, 0);
                int largeRectYPos = Math.Max(rect.Y - rect.Height, 0);
                int largeRectWidth = rect.Width * 3;
                if (largeRectXPos + largeRectWidth > state.imageProperties.Width) { largeRectWidth = state.imageProperties.Width - largeRectXPos; }
                int largeRectHeight = rect.Height * 3;
                if (largeRectYPos + largeRectHeight > state.imageProperties.Height) { largeRectHeight = state.imageProperties.Height - largeRectYPos; }
                var largeRect = new Rectangle(largeRectXPos, largeRectYPos, largeRectWidth, largeRectHeight);

                //Star is circle
                Star s;
                if (checker.IsCircle(points, out centerpoint, out radius)) {
                    s = new Star { Position = new Accord.Point(centerpoint.X * (float)state._inverseResizefactor, centerpoint.Y * (float)state._inverseResizefactor), radius = radius * state._inverseResizefactor, Rectangle = rect };
                }
                else { //Star is elongated
                    /*                    var eccentricity = CalculateEccentricity(rect.Width, rect.Height);
                                        //Discard highly elliptical shapes.
                                        if (eccentricity > 0.8) {
                                            continue;
                                        }*/
                    s = new Star { Position = new Accord.Point(centerpoint.X * (float)state._inverseResizefactor, centerpoint.Y * (float)state._inverseResizefactor), radius = Math.Max(rect.Width, rect.Height) / 2, Rectangle = rect };
                }

                /* get pixeldata */
                double starPixelSum = 0;
                int starPixelCount = 0;
                double largeRectPixelSum = 0;
                double largeRectPixelSumSquares = 0;
                List<ushort> innerStarPixelValues = new List<ushort>();

                for (int x = largeRect.X; x < largeRect.X + largeRect.Width; x++) {
                    for (int y = largeRect.Y; y < largeRect.Y + largeRect.Height; y++) {
                        var pixelValue = state._iarr.FlatArray[x + (state.imageProperties.Width * y)];
                        if (x >= s.Rectangle.X && x < s.Rectangle.X + s.Rectangle.Width && y >= s.Rectangle.Y && y < s.Rectangle.Y + s.Rectangle.Height) { //We're in the small rectangle directly surrounding the star
                            if (s.InsideCircle(x, y, s.Position.X, s.Position.Y, s.radius)) { // We're in the inner sanctum of the star
                                starPixelSum += pixelValue;
                                starPixelCount++;
                                innerStarPixelValues.Add(pixelValue);
                                s.maxPixelValue = Math.Max(s.maxPixelValue, pixelValue);
                            }
                            ushort value = pixelValue;
                            PixelData pd = new PixelData { PosX = x, PosY = y, value = (ushort)value };
                            s.AddPixelData(pd);
                        }
                        else { //We're in the larger surrounding holed rectangle, providing local background
                            largeRectPixelSum += pixelValue;
                            largeRectPixelSumSquares += pixelValue * pixelValue;
                        }
                    }
                }

                s.meanBrightness = starPixelSum / (double)starPixelCount;
                double largeRectPixelCount = largeRect.Height * largeRect.Width - rect.Height * rect.Width;
                double largeRectMean = largeRectPixelSum / largeRectPixelCount;
                s.SurroundingMean = largeRectMean;
                double largeRectStdev = Math.Sqrt((largeRectPixelSumSquares - largeRectPixelCount * largeRectMean * largeRectMean) / largeRectPixelCount);
                int minimumNumberOfPixels = (int)Math.Ceiling(Math.Max(state._originalBitmapSource.PixelWidth, state._originalBitmapSource.PixelHeight) / 1000d);

                if (s.meanBrightness >= largeRectMean + Math.Min(0.1 * largeRectMean, largeRectStdev) && innerStarPixelValues.Count(pv => pv > largeRectMean + 1.5 * largeRectStdev) > minimumNumberOfPixels) { //It's a local maximum, and has enough bright pixels, so likely to be a star. Let's add it to our star dictionary.
                    sumRadius += s.radius;
                    sumSquares += s.radius * s.radius;
                    s.CalculateHfr();
                    starlist.Add(s);
                }
            }

            // No stars could be found. Return.
            if (starlist.Count() == 0) {
                return new List<DetectedStar>();
            }

            // Ensure we provide the list of detected stars, even if NumberOfAF stars is used
            detectedStars = starlist.Count;

            return starlist.Select(s => s.ToDetectedStar()).ToList();
        }

        private List<DetectedStar> IdentifyLargestStars(StarDetectionParams p, State state, Bitmap _bitmapToAnalyze, StarDetectionResult result, CancellationToken token, out int detectedStars) {
            detectedStars = 0;
            int maxArea = _blobCounter.GetObjectsInformation().Max(x => x.Area);
            Blob[] blobs = _blobCounter.GetObjectsInformation().Where(x => x.Area >= maxArea * 0.7).ToArray();
            SimpleShapeChecker checker = new SimpleShapeChecker();
            List<Star> starlist = new List<Star>();
            double sumRadius = 0;
            double sumSquares = 0;
            foreach (Blob blob in blobs) {
                token.ThrowIfCancellationRequested();

                var points = _blobCounter.GetBlobsEdgePoints(blob);
                Accord.Point centerpoint;
                float radius;
                var rect = new Rectangle((int)Math.Floor(blob.Rectangle.X * state._inverseResizefactor), (int)Math.Floor(blob.Rectangle.Y * state._inverseResizefactor), (int)Math.Ceiling(blob.Rectangle.Width * state._inverseResizefactor), (int)Math.Ceiling(blob.Rectangle.Height * state._inverseResizefactor));

                //Build a rectangle that encompasses the blob
                int largeRectXPos = Math.Max(rect.X - rect.Width, 0);
                int largeRectYPos = Math.Max(rect.Y - rect.Height, 0);
                int largeRectWidth = rect.Width * 3;
                if (largeRectXPos + largeRectWidth > state.imageProperties.Width) { largeRectWidth = state.imageProperties.Width - largeRectXPos; }
                int largeRectHeight = rect.Height * 3;
                if (largeRectYPos + largeRectHeight > state.imageProperties.Height) { largeRectHeight = state.imageProperties.Height - largeRectYPos; }
                var largeRect = new Rectangle(largeRectXPos, largeRectYPos, largeRectWidth, largeRectHeight);

                //Star is circle
                Star s;
                if (checker.IsCircle(points, out centerpoint, out radius)) {
                    s = new Star { Position = new Accord.Point(centerpoint.X * (float)state._inverseResizefactor, centerpoint.Y * (float)state._inverseResizefactor), radius = radius * state._inverseResizefactor, Rectangle = rect };
                }
                else { //Star is elongated
                    s = new Star { Position = new Accord.Point(centerpoint.X * (float)state._inverseResizefactor, centerpoint.Y * (float)state._inverseResizefactor), radius = Math.Max(rect.Width, rect.Height) / 2, Rectangle = rect };
                }

                /* get pixeldata */
                double starPixelSum = 0;
                int starPixelCount = 0;
                double largeRectPixelSum = 0;
                double largeRectPixelSumSquares = 0;
                List<ushort> innerStarPixelValues = new List<ushort>();

                for (int x = largeRect.X; x < largeRect.X + largeRect.Width; x++) {
                    for (int y = largeRect.Y; y < largeRect.Y + largeRect.Height; y++) {
                        var pixelValue = state._iarr.FlatArray[x + (state.imageProperties.Width * y)];
                        if (x >= s.Rectangle.X && x < s.Rectangle.X + s.Rectangle.Width && y >= s.Rectangle.Y && y < s.Rectangle.Y + s.Rectangle.Height) { //We're in the small rectangle directly surrounding the star
                            if (s.InsideCircle(x, y, s.Position.X, s.Position.Y, s.radius)) { // We're in the inner sanctum of the star
                                starPixelSum += pixelValue;
                                starPixelCount++;
                                innerStarPixelValues.Add(pixelValue);
                                s.maxPixelValue = Math.Max(s.maxPixelValue, pixelValue);
                            }
                            ushort value = pixelValue;
                            PixelData pd = new PixelData { PosX = x, PosY = y, value = (ushort)value };
                            s.AddPixelData(pd);
                        }
                        else { //We're in the larger surrounding holed rectangle, providing local background
                            largeRectPixelSum += pixelValue;
                            largeRectPixelSumSquares += pixelValue * pixelValue;
                        }
                    }
                }

                s.meanBrightness = starPixelSum / (double)starPixelCount;
                double largeRectPixelCount = largeRect.Height * largeRect.Width - rect.Height * rect.Width;
                double largeRectMean = largeRectPixelSum / largeRectPixelCount;
                s.SurroundingMean = largeRectMean;
                double largeRectStdev = Math.Sqrt((largeRectPixelSumSquares - largeRectPixelCount * largeRectMean * largeRectMean) / largeRectPixelCount);
                int minimumNumberOfPixels = (int)Math.Ceiling(Math.Max(state._originalBitmapSource.PixelWidth, state._originalBitmapSource.PixelHeight) / 1000d);

                if (s.meanBrightness >= largeRectMean + Math.Min(0.1 * largeRectMean, largeRectStdev) && innerStarPixelValues.Count(pv => pv > largeRectMean + 1.5 * largeRectStdev) > minimumNumberOfPixels) { //It's a local maximum, and has enough bright pixels, so likely to be a star. Let's add it to our star dictionary.
                    sumRadius += s.radius;
                    sumSquares += s.radius * s.radius;
                    s.CalculateHfr();
                    s.distanceCenter = DistanceCenter(state, s.Position.X, s.Position.Y);
                    starlist.Add(s);
                }
                if (starlist.Count() >= 5) {
                    break;
                }
            }

            // No stars could be found. Return.
            if (starlist.Count() == 0) {
                return new List<DetectedStar>();
            }

            // Ensure we provide the list of detected stars, even if NumberOfAF stars is used
            detectedStars = starlist.Count;

            return starlist.OrderBy(x => x.distanceCenter).Select(s => s.ToDetectedStar()).ToList();
        }

        private double DistanceCenter(State state, double x, double y) {
            var centerX = state.imageProperties.Width / 2;
            var centerY = state.imageProperties.Height / 2;
            return Math.Sqrt(Math.Pow(x - centerX, 2) + Math.Pow(y - centerY, 2)); //TODO return percentage?
        }

        private double CalculateEccentricity(double width, double height) {
            var x = Math.Max(width, height);
            var y = Math.Min(width, height);
            double focus = Math.Sqrt(Math.Pow(x, 2) - Math.Pow(y, 2));
            return focus / x;
        }

        private BlobCounter DetectStructures(Bitmap bmp, CancellationToken token) {
            var sw = Stopwatch.StartNew();

            /* detect structures */
            BlobCounter blobCounter = new BlobCounter();
            blobCounter.ProcessImage(bmp);

            token.ThrowIfCancellationRequested();

            sw.Stop();
            Debug.Print("Time for structure detection: " + sw.Elapsed);
            sw = null;

            return blobCounter;
        }

        private void PrepareForStructureDetection(Bitmap bmp, StarDetectionParams p, State state, CancellationToken token) {
            var sw = Stopwatch.StartNew();

            if (p.Sensitivity == StarSensitivityEnum.Normal) {
                if (p.NoiseReduction == NoiseReductionEnum.None || p.NoiseReduction == NoiseReductionEnum.Median) {
                    //Still need to apply Gaussian blur, using normal Canny
                    new CannyEdgeDetector(10, 80).ApplyInPlace(bmp);
                } else {
                    //Gaussian blur already applied, using no-blur Canny
                    new NoBlurCannyEdgeDetector(10, 80).ApplyInPlace(bmp);
                }
            } else {
                int kernelSize = (int)Math.Max(Math.Floor(Math.Max(state._originalBitmapSource.PixelWidth, state._originalBitmapSource.PixelHeight) * state._resizefactor / 500), 3);
                //Apply blur or sharpen operation prior to applying the Canny Edge Detector
                if (state._inverseResizefactor > 1.6) {
                    //Strong blur occurred while resizing, apply fairly strong Gaussian Sharpen
                    new GaussianSharpen(1.8, kernelSize).ApplyInPlace(bmp);
                } else if (state._inverseResizefactor > 1) {
                    //Some blur occurred during resizing, apply Gaussian Sharpen with relative strength proportional to resize factor
                    double sigma = (state._inverseResizefactor - 1) * 3;
                    new GaussianSharpen(sigma, kernelSize).ApplyInPlace(bmp);
                } else {
                    if (p.NoiseReduction == NoiseReductionEnum.None || p.NoiseReduction == NoiseReductionEnum.Median) {
                        //No resizing or gaussian blur occurred, apply weak Gaussian blur
                        new GaussianBlur(0.7, 5).ApplyInPlace(bmp);
                    } else {
                        //Gaussian blur already occurred, do nothing
                    }
                }
                token.ThrowIfCancellationRequested();
                new NoBlurCannyEdgeDetector(10, 80).ApplyInPlace(bmp);
            }
            token.ThrowIfCancellationRequested();
            new SISThreshold().ApplyInPlace(bmp);
            token.ThrowIfCancellationRequested();
            new BinaryDilation3x3().ApplyInPlace(bmp);
            token.ThrowIfCancellationRequested();

            sw.Stop();
            Debug.Print("Time for image preparation: " + sw.Elapsed);
            sw = null;
        }

        public IStarDetectionAnalysis CreateAnalysis() {
            return new StarDetectionAnalysis();
        }

        public void UpdateAnalysis(IStarDetectionAnalysis analysis, StarDetectionParams p, StarDetectionResult result) {
            analysis.HFR = result.AverageHFR;
            analysis.HFRStDev = result.HFRStdDev;
            analysis.DetectedStars = result.DetectedStars;
        }
    }
}