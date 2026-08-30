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

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"D:\")]
    public void Resolve_CustomDirectory_DriveRoot_IsDenied(string root)
    {
        var resolution = ChatExportPathResolver.Resolve(
            new ChatExportTarget(ChatExportTargetKind.Directory, root));

        Assert.False(resolution.Success);
        Assert.False(string.IsNullOrWhiteSpace(resolution.ErrorMessage));
    }

    [Fact]
    public void IsDriveRoot_DetectsDriveLetterRoot()
    {
        Assert.True(ChatExportPathResolver.IsDriveRoot(@"C:\"));
        Assert.False(ChatExportPathResolver.IsDriveRoot(@"C:\work"));
    }

    [Fact]
    public void Resolve_AppRoot_RedirectsToUserDataExports()
    {
        var resolution = ChatExportPathResolver.Resolve(
            new ChatExportTarget(ChatExportTargetKind.AppRoot));

        Assert.True(resolution.Success);
        Assert.True(resolution.UsedFallback);
        Assert.NotNull(resolution.Directory);
        Assert.EndsWith("exports", resolution.Directory, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            Path.GetFullPath(AppPaths.Current.Root).TrimEnd('\\'),
            Path.GetFullPath(resolution.Directory).TrimEnd('\\'),
            StringComparison.OrdinalIgnoreCase);
    }
}
