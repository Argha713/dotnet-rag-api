namespace RagApi.Application.Models;

// Argha - 2026-03-18 - #52 - Application-layer vision options; mirrors relevant fields from Infrastructure VisionConfiguration
public class VisionOptions
{
    public const string SectionName = "Vision";

    // Argha - 2026-03-18 - #52 - Cap GPT-4o-mini calls per upload to control cost; ~$0.003 ceiling at default 20
    public int MaxImagesPerDocument { get; set; } = 20;
}
