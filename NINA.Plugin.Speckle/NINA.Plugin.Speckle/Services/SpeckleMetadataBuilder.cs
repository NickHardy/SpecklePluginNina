using NINA.Astrometry;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Image.ImageData;
using NINA.Plugin.Speckle.Model;
using NINA.Plugin.Speckle.Sequencer.Utility;
using System;
using System.Collections.Generic;

namespace NINA.Plugin.Speckle.Services {

    public static class SpeckleMetadataBuilder {

        public static List<IGenericMetaDataHeader> ResolveGenericHeaders(SpeckleTarget target, bool isReferenceLeg, Speckle speckle) {
            return isReferenceLeg ? target.ReferenceStar.GenericHeaders() : target?.GenericHeaders(speckle.SaveCsvToFitsHeader);
        }

        public static List<ImagePattern> BuildCustomPatterns(Speckle speckle, int speckleRun, List<IGenericMetaDataHeader> genericHeaders) {
            var customPatterns = new List<ImagePattern> {
                new ImagePattern(speckle.notePattern.Key, speckle.notePattern.Description, speckle.notePattern.Category) {
                    Value = string.Empty
                },
                new ImagePattern(speckle.speckleRunPattern.Key, speckle.speckleRunPattern.Description, speckle.speckleRunPattern.Category) {
                    Value = $"{speckleRun}"
                }
            };
            if (genericHeaders != null)
                ItemUtility.AddImagePatterns(customPatterns, speckle, genericHeaders);
            return customPatterns;
        }

        public static void AddSpeckleRunHeader(ImageMetaData metaData, int speckleRun) {
            metaData.GenericHeaders.Add(new StringMetaDataHeader("SPECRUN", $"{speckleRun}", "Speckle run"));
        }

        public static void AddJulianDateHeaders(ImageMetaData metaData, double exposureDuration) {
            metaData.GenericHeaders.Add(new DoubleMetaDataHeader("JD-END", AstroUtil.GetJulianDate(DateTime.Now), "Julian exposure end date"));
            metaData.GenericHeaders.Add(new DoubleMetaDataHeader("JD-BEG", AstroUtil.GetJulianDate(metaData.Image.ExposureStart), "Julian exposure start date"));
            metaData.GenericHeaders.Add(new DoubleMetaDataHeader("JD-OBS", AstroUtil.GetJulianDate(metaData.Image.ExposureStart.AddSeconds(exposureDuration / 2)), "Julian exposure mid date"));
        }

        public static void AddRoiHeaders(ImageMetaData metaData, ObservableRectangle rect) {
            metaData.GenericHeaders.Add(new DoubleMetaDataHeader("ROIX", rect.X, "X-position of the ROI"));
            metaData.GenericHeaders.Add(new DoubleMetaDataHeader("ROIY", rect.Y, "Y-position of the ROI"));
            AddSubFrameHeaders(metaData, rect);
        }

        public static void AddSubFrameHeaders(ImageMetaData metaData, ObservableRectangle rect) {
            metaData.GenericHeaders.Add(new DoubleMetaDataHeader("XORGSUBF", rect.X, "X-position of the ROI"));
            metaData.GenericHeaders.Add(new DoubleMetaDataHeader("YORGSUBF", rect.Y, "Y-position of the ROI"));
        }

        public static void ApplyTarget(ImageMetaData metaData, InputTarget target) {
            if (target != null) {
                metaData.Target.Name = target.DeepSkyObject?.NameAsAscii;
                metaData.Target.Coordinates = target.InputCoordinates.Coordinates;
                metaData.Target.PositionAngle = target.PositionAngle;
            }
        }
    }
}
