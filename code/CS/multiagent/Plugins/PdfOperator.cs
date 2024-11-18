using System.ComponentModel;
using Microsoft.SemanticKernel;
using MultiAgent.Plugins;

public sealed class PdfOperatorPlugin
{

    [KernelFunction, Description("Extracts text from a PDF file.")]
    public static string ExtractPdfContent(string filePath)
    {
        try
        {
            // Call FilePlugin method to read the PDF
            string pdfContent = PdfExtractorizer.ExtractTextFromPdf(filePath);

            return pdfContent;
        }
        catch (Exception ex)
        {
            // Handle exceptions
            Console.WriteLine($"An error occurred: {ex.Message}");
            return string.Empty;
        }
    }
}

