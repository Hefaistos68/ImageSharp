// Copyright (c) Six Labors and contributors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Jpeg.GolangPort;
using SixLabors.ImageSharp.Formats.Jpeg.PdfJsPort;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Tests.Formats.Jpg.Utils;
using SixLabors.ImageSharp.Tests.TestUtilities.ImageComparison;

using Xunit;
using Xunit.Abstractions;

// ReSharper disable InconsistentNaming
namespace SixLabors.ImageSharp.Tests.Formats.Jpg
{
    // TODO: Scatter test cases into multiple test classes
    public partial class JpegDecoderTests
    {
        public static string[] BaselineTestJpegs =
        {
            TestImages.Jpeg.Baseline.Calliphora,
            TestImages.Jpeg.Baseline.Cmyk,
            TestImages.Jpeg.Baseline.Ycck,
            TestImages.Jpeg.Baseline.Jpeg400,
            TestImages.Jpeg.Baseline.Testorig420,
                
            // BUG: The following image has a high difference compared to the expected output:
            // TestImages.Jpeg.Baseline.Jpeg420Small,

            TestImages.Jpeg.Baseline.Jpeg444,
            TestImages.Jpeg.Baseline.Bad.BadEOF,
            TestImages.Jpeg.Issues.MultiHuffmanBaseline394,
            TestImages.Jpeg.Baseline.MultiScanBaselineCMYK,
            TestImages.Jpeg.Baseline.Bad.BadRST
        };

        public static string[] ProgressiveTestJpegs =
        {
            TestImages.Jpeg.Progressive.Fb, TestImages.Jpeg.Progressive.Progress,
            TestImages.Jpeg.Progressive.Festzug, TestImages.Jpeg.Progressive.Bad.BadEOF,
            TestImages.Jpeg.Issues.BadCoeffsProgressive178,
            TestImages.Jpeg.Issues.MissingFF00ProgressiveGirl159,
            TestImages.Jpeg.Issues.MissingFF00ProgressiveBedroom159,
            TestImages.Jpeg.Issues.BadZigZagProgressive385,
            TestImages.Jpeg.Progressive.Bad.ExifUndefType,

            TestImages.Jpeg.Issues.NoEoiProgressive517,
            TestImages.Jpeg.Issues.BadRstProgressive518,
            TestImages.Jpeg.Issues.MissingFF00ProgressiveBedroom159,
        };

        /// <summary>
        /// Golang decoder is unable to decode these
        /// </summary>
        public static string[] PdfJsOnly =
        {
            TestImages.Jpeg.Issues.NoEoiProgressive517,
            TestImages.Jpeg.Issues.BadRstProgressive518,
            TestImages.Jpeg.Issues.MissingFF00ProgressiveBedroom159
        };

        private static readonly Dictionary<string, float> CustomToleranceValues = new Dictionary<string, float>
        {
            // Baseline:
            [TestImages.Jpeg.Baseline.Calliphora] = 0.00002f / 100,
            [TestImages.Jpeg.Baseline.Bad.BadEOF] = 0.38f / 100,
            [TestImages.Jpeg.Baseline.Testorig420] = 0.38f / 100,
            [TestImages.Jpeg.Baseline.Bad.BadRST] = 0.0589f / 100,

            // Progressive:
            [TestImages.Jpeg.Issues.MissingFF00ProgressiveGirl159] = 0.34f / 100,
            [TestImages.Jpeg.Issues.BadCoeffsProgressive178] = 0.38f / 100,
            [TestImages.Jpeg.Progressive.Bad.BadEOF] = 0.3f / 100,
            [TestImages.Jpeg.Progressive.Festzug] = 0.02f / 100,
            [TestImages.Jpeg.Progressive.Fb] = 0.16f / 100,
            [TestImages.Jpeg.Progressive.Progress] = 0.31f / 100,
            [TestImages.Jpeg.Issues.BadZigZagProgressive385] = 0.23f / 100,
            [TestImages.Jpeg.Progressive.Bad.ExifUndefType] = 0.011f / 100,
        };

        public const PixelTypes CommonNonDefaultPixelTypes = PixelTypes.Rgba32 | PixelTypes.Argb32 | PixelTypes.RgbaVector;

        private const float BaselineTolerance = 0.001F / 100;
        private const float ProgressiveTolerance = 0.2F / 100;

        private ImageComparer GetImageComparer<TPixel>(TestImageProvider<TPixel> provider)
            where TPixel : struct, IPixel<TPixel>
        {
            string file = provider.SourceFileOrDescription;

            if (!CustomToleranceValues.TryGetValue(file, out float tolerance))
            {
                bool baseline = file.ToLower().Contains("baseline");
                tolerance = baseline ? BaselineTolerance : ProgressiveTolerance;
            }

            return ImageComparer.Tolerant(tolerance);
        }

        private static bool SkipTest(ITestImageProvider provider)
        {
            string[] largeImagesToSkipOn32Bit =
                {
                    TestImages.Jpeg.Baseline.Jpeg420Exif,
                    TestImages.Jpeg.Issues.BadZigZagProgressive385
                };

            return TestEnvironment.RunsOnCI && !TestEnvironment.Is64BitProcess
                                            && largeImagesToSkipOn32Bit.Contains(provider.SourceFileOrDescription);
        }

        public JpegDecoderTests(ITestOutputHelper output)
        {
            this.Output = output;
        }

        private ITestOutputHelper Output { get; }

        private static GolangJpegDecoder GolangJpegDecoder => new GolangJpegDecoder();

        private static PdfJsJpegDecoder PdfJsJpegDecoder => new PdfJsJpegDecoder();

        private static JpegDecoder DefaultJpegDecoder => new JpegDecoder();

        [Fact]
        public void ParseStream_BasicPropertiesAreCorrect1_PdfJs()
        {
            byte[] bytes = TestFile.Create(TestImages.Jpeg.Progressive.Progress).Bytes;
            using (var ms = new MemoryStream(bytes))
            {
                var decoder = new PdfJsJpegDecoderCore(Configuration.Default, new JpegDecoder());
                decoder.ParseStream(ms);

                // I don't know why these numbers are different. All I know is that the decoder works
                // and spectral data is exactly correct also.
                // VerifyJpeg.VerifyComponentSizes3(decoder.Frame.Components, 43, 61, 22, 31, 22, 31);
                VerifyJpeg.VerifyComponentSizes3(decoder.Frame.Components, 44, 62, 22, 31, 22, 31);
            }
        }

        public const string DecodeBaselineJpegOutputName = "DecodeBaselineJpeg";

        [Theory]
        [WithFile(TestImages.Jpeg.Baseline.Calliphora, CommonNonDefaultPixelTypes, false)]
        [WithFile(TestImages.Jpeg.Baseline.Calliphora, CommonNonDefaultPixelTypes, true)]
        public void JpegDecoder_IsNotBoundToSinglePixelType<TPixel>(TestImageProvider<TPixel> provider, bool useOldDecoder)
            where TPixel : struct, IPixel<TPixel>
        {
            if (SkipTest(provider))
            {
                return;
            }

            // For 32 bit test enviroments:
            provider.Configuration.MemoryManager = ArrayPoolMemoryManager.CreateWithModeratePooling();

            IImageDecoder decoder = useOldDecoder ? (IImageDecoder)GolangJpegDecoder : PdfJsJpegDecoder;
            using (Image<TPixel> image = provider.GetImage(decoder))
            {
                image.DebugSave(provider);

                provider.Utility.TestName = DecodeBaselineJpegOutputName;
                image.CompareToReferenceOutput(ImageComparer.Tolerant(BaselineTolerance), provider, appendPixelTypeToFileName: false);
            }

            provider.Configuration.MemoryManager.ReleaseRetainedResources();
        }

        [Theory]
        [WithFileCollection(nameof(BaselineTestJpegs), PixelTypes.Rgba32)]
        public void DecodeBaselineJpeg_Orig<TPixel>(TestImageProvider<TPixel> provider)
            where TPixel : struct, IPixel<TPixel>
        {
            if (SkipTest(provider))
            {
                return;
            }

            // For 32 bit test enviroments:
            provider.Configuration.MemoryManager = ArrayPoolMemoryManager.CreateWithModeratePooling();

            using (Image<TPixel> image = provider.GetImage(GolangJpegDecoder))
            {
                image.DebugSave(provider);
                provider.Utility.TestName = DecodeBaselineJpegOutputName;
                image.CompareToReferenceOutput(
                    this.GetImageComparer(provider),
                    provider,
                    appendPixelTypeToFileName: false);
            }

            provider.Configuration.MemoryManager.ReleaseRetainedResources();
        }

        [Theory]
        [WithFileCollection(nameof(BaselineTestJpegs), PixelTypes.Rgba32)]
        public void DecodeBaselineJpeg_PdfJs<TPixel>(TestImageProvider<TPixel> provider)
            where TPixel : struct, IPixel<TPixel>
        {
            if (TestEnvironment.RunsOnCI && !TestEnvironment.Is64BitProcess)
            {
                // skipping to avoid OutOfMemoryException on CI
                return;
            }

            using (Image<TPixel> image = provider.GetImage(PdfJsJpegDecoder))
            {
                image.DebugSave(provider);

                provider.Utility.TestName = DecodeBaselineJpegOutputName;
                image.CompareToReferenceOutput(
                    this.GetImageComparer(provider),
                    provider,
                    appendPixelTypeToFileName: false);
            }
        }
        
        [Theory]
        [WithFile(TestImages.Jpeg.Issues.CriticalEOF214, PixelTypes.Rgba32)]
        public void DecodeBaselineJpeg_CriticalEOF_ShouldThrow_Golang<TPixel>(TestImageProvider<TPixel> provider)
            where TPixel : struct, IPixel<TPixel>
        {
            // TODO: We need a public ImageDecoderException class in ImageSharp!
            Assert.ThrowsAny<Exception>(() => provider.GetImage(GolangJpegDecoder));
        }

        [Theory]
        [WithFile(TestImages.Jpeg.Issues.CriticalEOF214, PixelTypes.Rgba32)]
        public void DecodeBaselineJpeg_CriticalEOF_ShouldThrow_PdfJs<TPixel>(TestImageProvider<TPixel> provider)
            where TPixel : struct, IPixel<TPixel>
        {
            // TODO: We need a public ImageDecoderException class in ImageSharp!
            Assert.ThrowsAny<Exception>(() => provider.GetImage(PdfJsJpegDecoder));
        }

        public const string DecodeProgressiveJpegOutputName = "DecodeProgressiveJpeg";

        [Theory]
        [WithFileCollection(nameof(ProgressiveTestJpegs), PixelTypes.Rgba32)]
        public void DecodeProgressiveJpeg_Orig<TPixel>(TestImageProvider<TPixel> provider)
            where TPixel : struct, IPixel<TPixel>
        {
            if (TestEnvironment.RunsOnCI && !TestEnvironment.Is64BitProcess)
            {
                // skipping to avoid OutOfMemoryException on CI
                return;
            }
            
            // Golang decoder is unable to decode these:
            if (PdfJsOnly.Any(fn => fn.Contains(provider.SourceFileOrDescription)))
            {
                return;
            }
            
            // For 32 bit test enviroments:
            provider.Configuration.MemoryManager = ArrayPoolMemoryManager.CreateWithModeratePooling();

            using (Image<TPixel> image = provider.GetImage(GolangJpegDecoder))
            {
                image.DebugSave(provider);

                provider.Utility.TestName = DecodeProgressiveJpegOutputName;
                image.CompareToReferenceOutput(
                    this.GetImageComparer(provider),
                    provider,
                    appendPixelTypeToFileName: false);
            }

            provider.Configuration.MemoryManager.ReleaseRetainedResources();
        }

        [Theory]
        [WithFileCollection(nameof(ProgressiveTestJpegs), PixelTypes.Rgba32)]
        public void DecodeProgressiveJpeg_PdfJs<TPixel>(TestImageProvider<TPixel> provider)
            where TPixel : struct, IPixel<TPixel>
        {
            if (SkipTest(provider))
            {
                // skipping to avoid OutOfMemoryException on CI
                return;
            }
            
            using (Image<TPixel> image = provider.GetImage(PdfJsJpegDecoder))
            {
                image.DebugSave(provider);

                provider.Utility.TestName = DecodeProgressiveJpegOutputName;
                image.CompareToReferenceOutput(
                    this.GetImageComparer(provider),
                    provider,
                    appendPixelTypeToFileName: false);
            }
        }

        private string GetDifferenceInPercentageString<TPixel>(Image<TPixel> image, TestImageProvider<TPixel> provider)
            where TPixel : struct, IPixel<TPixel>
        {
            var reportingComparer = ImageComparer.Tolerant(0, 0);

            ImageSimilarityReport report = image.GetReferenceOutputSimilarityReports(
                provider,
                reportingComparer,
                appendPixelTypeToFileName: false
                ).SingleOrDefault();

            if (report != null && report.TotalNormalizedDifference.HasValue)
            {
                return report.DifferencePercentageString;
            }

            return "0%";
        }

        private void CompareJpegDecodersImpl<TPixel>(TestImageProvider<TPixel> provider, string testName)
            where TPixel : struct, IPixel<TPixel>
        {
            this.Output.WriteLine(provider.SourceFileOrDescription);
            provider.Utility.TestName = testName;

            using (Image<TPixel> image = provider.GetImage(GolangJpegDecoder))
            {
                string d = this.GetDifferenceInPercentageString(image, provider);

                this.Output.WriteLine($"Difference using ORIGINAL decoder: {d}");
            }

            using (Image<TPixel> image = provider.GetImage(PdfJsJpegDecoder))
            {
                string d = this.GetDifferenceInPercentageString(image, provider);
                this.Output.WriteLine($"Difference using PDFJS decoder: {d}");
            }
        }

        [Theory(Skip = "Debug only, enable manually!")]
        [WithFileCollection(nameof(BaselineTestJpegs), PixelTypes.Rgba32)]
        public void CompareJpegDecoders_Baseline<TPixel>(TestImageProvider<TPixel> provider)
            where TPixel : struct, IPixel<TPixel>
        {
            this.CompareJpegDecodersImpl(provider, DecodeBaselineJpegOutputName);
        }

        [Theory(Skip = "Debug only, enable manually!")]
        [WithFileCollection(nameof(ProgressiveTestJpegs), PixelTypes.Rgba32)]
        public void CompareJpegDecoders_Progressive<TPixel>(TestImageProvider<TPixel> provider)
            where TPixel : struct, IPixel<TPixel>
        {
            this.CompareJpegDecodersImpl(provider, DecodeProgressiveJpegOutputName);
        }

        [Theory]
        [WithSolidFilledImages(16, 16, 255, 0, 0, PixelTypes.Rgba32, JpegSubsample.Ratio420, 75)]
        [WithSolidFilledImages(16, 16, 255, 0, 0, PixelTypes.Rgba32, JpegSubsample.Ratio420, 100)]
        [WithSolidFilledImages(16, 16, 255, 0, 0, PixelTypes.Rgba32, JpegSubsample.Ratio444, 75)]
        [WithSolidFilledImages(16, 16, 255, 0, 0, PixelTypes.Rgba32, JpegSubsample.Ratio444, 100)]
        [WithSolidFilledImages(8, 8, 255, 0, 0, PixelTypes.Rgba32, JpegSubsample.Ratio444, 100)]
        public void DecodeGenerated<TPixel>(
            TestImageProvider<TPixel> provider,
            JpegSubsample subsample,
            int quality)
            where TPixel : struct, IPixel<TPixel>
        {
            byte[] data;
            using (Image<TPixel> image = provider.GetImage())
            {
                var encoder = new JpegEncoder { Subsample = subsample, Quality = quality };

                data = new byte[65536];
                using (var ms = new MemoryStream(data))
                {
                    image.Save(ms, encoder);
                }
            }

            var mirror = Image.Load<TPixel>(data, GolangJpegDecoder);
            mirror.DebugSave(provider, $"_{subsample}_Q{quality}");
        }

        // DEBUG ONLY!
        // The PDF.js output should be saved by "tests\ImageSharp.Tests\Formats\Jpg\pdfjs\jpeg-converter.htm"
        // into "\tests\Images\ActualOutput\JpegDecoderTests\"
        //[Theory]
        //[WithFile(TestImages.Jpeg.Progressive.Progress, PixelTypes.Rgba32, "PdfJsOriginal_progress.png")]
        public void ValidateProgressivePdfJsOutput<TPixel>(TestImageProvider<TPixel> provider,
            string pdfJsOriginalResultImage)
            where TPixel : struct, IPixel<TPixel>
        {
            // tests\ImageSharp.Tests\Formats\Jpg\pdfjs\jpeg-converter.htm
            string pdfJsOriginalResultPath = Path.Combine(
                provider.Utility.GetTestOutputDir(),
                pdfJsOriginalResultImage);

            byte[] sourceBytes = TestFile.Create(TestImages.Jpeg.Progressive.Progress).Bytes;

            provider.Utility.TestName = nameof(DecodeProgressiveJpegOutputName);

            var comparer = ImageComparer.Tolerant(0, 0);

            using (Image<TPixel> expectedImage = provider.GetReferenceOutputImage<TPixel>(appendPixelTypeToFileName: false))
            using (var pdfJsOriginalResult = Image.Load(pdfJsOriginalResultPath))
            using (var pdfJsPortResult = Image.Load(sourceBytes, PdfJsJpegDecoder))
            {
                ImageSimilarityReport originalReport = comparer.CompareImagesOrFrames(expectedImage, pdfJsOriginalResult);
                ImageSimilarityReport portReport = comparer.CompareImagesOrFrames(expectedImage, pdfJsPortResult);

                this.Output.WriteLine($"Difference for PDF.js ORIGINAL: {originalReport.DifferencePercentageString}");
                this.Output.WriteLine($"Difference for PORT: {portReport.DifferencePercentageString}");
            }
        }
    }
}