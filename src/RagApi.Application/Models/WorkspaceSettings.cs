namespace RagApi.Application.Models;

// Argha - 2026-03-04 - #17 - Config POCO for workspace feature; registered in DI for future toggles
public class WorkspaceSettings
{
    public const string SectionName = "Workspace";

    public bool Enabled { get; set; } = true;
}
