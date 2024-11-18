using System;
using System.Text;
using System.Collections.Generic;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using Microsoft.SemanticKernel;
using System.ComponentModel;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.IO.Image;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using Tesseract;

namespace MultiAgent.Plugins
{
    public sealed class PdfExtractorizer
    {
        public static string ExtractTextFromPdf(string path)
        {
            using (PdfReader pdfReader = new PdfReader(path))
            using (PdfDocument pdfDocument = new PdfDocument(pdfReader))
            {
                StringWriter stringWriter = new StringWriter();
                bool hasText = false;

                for (int page = 1; page <= pdfDocument.GetNumberOfPages(); page++)
                {
                    var strategy = new SimpleTextExtractionStrategy();
                    string pageText = PdfTextExtractor.GetTextFromPage(pdfDocument.GetPage(page), strategy);
                    if (!string.IsNullOrWhiteSpace(pageText))
                    {
                        hasText = true;
                        stringWriter.WriteLine(pageText);
                    }
                }

                if (!hasText)
                {
                    return "IMAGE";
                }

                return stringWriter.ToString();
            }
        }

        public static string ExtractTextFromImage(byte[] imageBytes)
        {
            string tessDataPath = Environment.GetEnvironmentVariable("TESSDATA_PREFIX");
            if (string.IsNullOrEmpty(tessDataPath))
            {
                throw new InvalidOperationException("TESSDATA_PREFIX environment variable is not set.");
            }

            try
            {
                using var engine = new TesseractEngine(tessDataPath, "eng", EngineMode.Default);
                using var img = Pix.LoadFromMemory(imageBytes);
                using var page = engine.Process(img);
                return page.GetText();
            }
            catch (TesseractException ex)
            {
                // Handle the exception (e.g., log it, rethrow it, return an error message, etc.)
                throw new InvalidOperationException("Failed to initialize Tesseract engine.", ex);
            }
        }

        public static List<byte[]> ExtractImagesFromPdf(string path)
        {
            List<byte[]> images = new List<byte[]>();

            using (PdfReader pdfReader = new PdfReader(path))
            using (PdfDocument pdfDocument = new PdfDocument(pdfReader))
            {
                for (int page = 1; page <= pdfDocument.GetNumberOfPages(); page++)
                {
                    var strategy = new ImageRenderListener(images);
                    PdfCanvasProcessor parser = new PdfCanvasProcessor(strategy);
                    parser.ProcessPageContent(pdfDocument.GetPage(page));
                }
            }

            return images;
        }

        private class ImageRenderListener : IEventListener
        {
            private readonly List<byte[]> _images;

            public ImageRenderListener(List<byte[]> images)
            {
                _images = images;
            }

            public void EventOccurred(IEventData data, EventType type)
            {
                if (type == EventType.RENDER_IMAGE)
                {
                    var renderInfo = (ImageRenderInfo)data;
                    var image = renderInfo.GetImage();
                    var imageBytes = image.GetImageBytes(true);
                    _images.Add(imageBytes);
                }
            }

            public ICollection<EventType> GetSupportedEvents()
            {
                return new HashSet<EventType> { EventType.RENDER_IMAGE };
            }
        }

        public sealed class PdfExtractionPlugin
        {
            [KernelFunction, Description("Extracts text from a PDF file.")]
            public static string ExtractText(string pdfPath)
            {
                return PdfExtractorizer.ExtractTextFromPdf(pdfPath);
            }

            [KernelFunction, Description("Extracts images from a PDF file.")]
            public static List<byte[]> ExtractImages(string pdfPath)
            {
                return PdfExtractorizer.ExtractImagesFromPdf(pdfPath);
            }
        }

        public sealed class FunctionCallResultType{
            public string filePath { get; set; }
        }
    }
}