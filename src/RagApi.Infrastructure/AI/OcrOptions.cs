namespace RagApi.Infrastructure.AI;

public class OcrOptions
{
    public bool Enabled { get; set; } = false;

    // Default is the Debian/Ubuntu path after: apt-get install tesseract-ocr tesseract-ocr-eng
    public string TessDataPath { get; set; } = "/usr/share/tesseract-ocr/5/tessdata";

    public string Language { get; set; } = "eng";
}
