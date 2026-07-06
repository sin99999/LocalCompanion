using LocalCompanion.Models;
using LocalCompanion.Services;

namespace LocalCompanion.Core.Tests;

public sealed class ChatExportPathResolverTests
{
    [Fact]
    public void Resolve_CustomDirectory_WritesToExistingTempFolder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "lc-export-path-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var resolution = ChatExportPathResolver.Resolve(
                new ChatExportTarget(ChatExportTargetKind.Directory, dir));

            Assert.True(resolution.Success);
            Assert.Equal(Path.GetFullPath(dir), resolution.Directory);
            Assert.False(resolution.UsedFallback);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Resolve_UserData_UsesWritableDirectory()
    {
        var resolution = ChatExportPathResolver.Resolve(
            new ChatExportTarget(ChatExportTargetKind.UserData));

        Assert.True(resolution.Success);
        Assert.False(string.IsNullOrWhiteSpace(resolution.Directory));
    }
}
