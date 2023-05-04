using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Text;
using Newtonsoft.Json;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace app.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FileUploadController : ControllerBase
{
    private readonly ILogger<FileUploadController> logger;
    private readonly IHubContext<ProgressHub> hubContext;
    private readonly IConfiguration config;
    private readonly IKernel kernel;
    private readonly int DocumentLineSplitMaxTokens;
    private readonly int DocumentParagraphSplitMaxLines;

    public FileUploadController(ILogger<FileUploadController> logger, IHubContext<ProgressHub> hubContext, IConfiguration config, IKernel kernel)
    {
        this.logger = logger;
        this.hubContext = hubContext;
        this.config = config;
        this.kernel = kernel;
        DocumentLineSplitMaxTokens = config.GetValue<int>("DocumentMemory:DocumentLineSplitMaxTokens" );
        DocumentParagraphSplitMaxLines = config.GetValue<int>("DocumentMemory:DocumentParagraphSplitMaxLines");
    }

    [HttpPost]
    public async Task<IActionResult> Upload(IFormFile file)
    {
        if (file == null || file.Length == 0 || Path.GetExtension(file.FileName).ToLowerInvariant() != ".pdf")
        {
            return BadRequest("Invalid file format. Please upload a PDF file.");
        }
    
        var stream = new MemoryStream();
        await file.CopyToAsync(stream);
        stream.Position =0;
        // Start the background job to extract content from the PDF
        _ = Task.Run(async () => await ExtractPdfContent(stream, file.FileName));

         return Ok($"PDF file uploaded successfully. Content processing is in progress.{file.FileName}");
    }

    private async Task ExtractPdfContent(Stream stream, string fileName, string topic="global")
    {
        // Update progress
        await hubContext.Clients.All.SendAsync("ProgressUpdate", JsonConvert.SerializeObject(new { Progress = 0, FileName = fileName }));

        try
        {
            var pdf = PdfDocument.Open(stream);
            var result = String.Empty;
            foreach (var page in pdf.GetPages())
            {
                // Either extract based on order in the underlying document with newlines and spaces.
                var text = ContentOrderTextExtractor.GetText(page);
                var progress = (page.Number * 100 / pdf.NumberOfPages);
                await hubContext.Clients.All.SendAsync("Parsing file", JsonConvert.SerializeObject(new { Progress = progress, FileName = $"p:{page.Number}" }));
                result = $"{result}{text}";
                logger.LogDebug($"Indexed the PDF content successfully: {result}");
            }
            // Split the document into lines of text and then combine them into paragraphs.
            var lines = TextChunker.SplitPlainTextLines(result, DocumentLineSplitMaxTokens);
            var paragraphs = TextChunker.SplitPlainTextParagraphs(lines, DocumentParagraphSplitMaxLines);
            await hubContext.Clients.All.SendAsync("Adding to memory ", JsonConvert.SerializeObject(new { Progress = 0, FileName = "" }));
            int i = 0;
            foreach (var paragraph in paragraphs)
            {
                i++;
                await kernel.Memory.SaveInformationAsync(
                    collection: topic,
                    text: paragraph,
                    id: Guid.NewGuid().ToString(),
                    description: $"Document: {fileName}");

                await hubContext.Clients.All.SendAsync("Adding to memory ", JsonConvert.SerializeObject(new { Progress = (i * 100 / (paragraphs.Count+1)), FileName = "" }));
            }
            logger.LogDebug($"Document content memorized successfully: {result}");
            await hubContext.Clients.All.SendAsync("Complete", JsonConvert.SerializeObject(new { Progress = 100, FileName = "" }));
        }
        catch (Exception ex)
        {
            logger.LogError($"Error indexing the PDF content: {ex}");
        }


        // Update progress
        await hubContext.Clients.All.SendAsync("ProgressUpdate", JsonConvert.SerializeObject(new { Progress = 100, FileName = fileName }));
    }
}
